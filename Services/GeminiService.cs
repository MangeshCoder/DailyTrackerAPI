using DailyTrackerAPI.Data;
using DailyTrackerAPI.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;

namespace DailyTrackerAPI.Services
{
    // ─────────────────────────────────────────────────────────────────────────
    //  IAiService — int userId added so the service can fetch real user data.
    // ─────────────────────────────────────────────────────────────────────────
    public interface IAiService
    {
        Task<string> GetChatResponseAsync(string userMessage, List<MessageHistory> history, int userId);
    }

    public class GeminiService : IAiService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly AppDbContext _db;               // injected to query user data

        private const string MODEL = "gemini-2.5-flash-lite";

        public GeminiService(HttpClient httpClient, IConfiguration configuration, AppDbContext db)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _db = db;
        }

        public async Task<string> GetChatResponseAsync(
            string userMessage,
            List<MessageHistory> history,
            int userId)
        {
            var apiKey = _configuration["Gemini:ApiKey"]
                         ?? throw new Exception("Gemini API key not configured.");

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{MODEL}:generateContent?key={apiKey}";

            // Build a live snapshot of the user's real data and inject it into
            // system_instruction. Rebuilt on every request → always up to date.
            // system_instruction is never truncated by conversation history growth.
            var userContext = userId > 0
                ? await BuildUserContextAsync(userId)
                : "No authenticated user context available.";

            // Keep only last 10 turns to stay within free-tier token budget
            var recentHistory = history.TakeLast(10).ToList();

            var contents = new List<object>();
            foreach (var msg in recentHistory)
            {
                contents.Add(new
                {
                    role = msg.Role.ToLower() == "assistant" ? "model" : "user",
                    parts = new[] { new { text = msg.Content } }
                });
            }
            contents.Add(new
            {
                role = "user",
                parts = new[] { new { text = userMessage } }
            });

            var systemPrompt = $"""
                You are a smart, friendly AI assistant built into the Daily Tracker application.
                You help employees manage and understand their work day.

                RULES:
                - Be concise and professional. Use bullet points when listing multiple items.
                - Format dates as "Mon DD" (e.g. "Jan 15") and times as "hh:mm AM/PM".
                - NEVER invent or guess data — only use what is in the context below.
                - If information is not in the context, say "I don't have that data right now."
                - Today is {DateTime.Now:dddd, MMMM dd yyyy}. Current time: {DateTime.Now:hh:mm tt}.
                - When asked to "show", "list", or "what are my X" — output the actual values.
                - Proactively mention if today's log is missing, EOD report not yet submitted,
                  a leave or WFH request is pending, or if any tasks are overdue.

                ══════════════════════════════════════════════════════════
                  LIVE USER DATA  (re-fetched fresh on every message)
                ══════════════════════════════════════════════════════════
                {userContext}
                ══════════════════════════════════════════════════════════

                You can assist with:
                • Daily Logs     — log entry, hours worked, check-in/out, status
                • Task Logs      — tasks worked on this week, hours per task
                • Break Logs     — break times recorded today
                • Support Work   — support activities and whom you helped
                • Leave Requests — status, pending approvals, leave history
                • WFH Requests   — work-from-home status and approvals
                • Daily Goals    — goals set and whether they were achieved
                • EOD Reports    — whether today's end-of-day report is submitted
                • Kudos          — recognition received from teammates
                • Late Arrivals  — whether a late arrival reason was logged
                • General productivity advice and Daily Tracker usage help
                """;

            var requestBody = new
            {
                system_instruction = new { parts = new[] { new { text = systemPrompt } } },
                contents,
                generationConfig = new { maxOutputTokens = 800, temperature = 0.7 }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, httpContent);
            var body = await response.Content.ReadAsStringAsync();

            if ((int)response.StatusCode == 429)
                return "The AI is currently busy (free-tier limit reached). Please wait 60 seconds and try again.";

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Gemini API error {response.StatusCode}: {body}");

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("candidates", out var candidates)
                && candidates.GetArrayLength() > 0)
            {
                var first = candidates[0];
                if (first.TryGetProperty("content", out var resContent))
                    return resContent.GetProperty("parts")[0].GetProperty("text").GetString()
                           ?? "I couldn't think of a response.";
            }
            return "Sorry, I received an empty response from the AI.";
        }

        // ─────────────────────────────────────────────────────────────────────
        //  BuildUserContextAsync
        //
        //  All DbSet names are taken EXACTLY from AppDbContext.cs.
        //  All FK / navigation property names are confirmed from OnModelCreating.
        //
        //  ⚠️  PROPERTY NAMES marked below come from your Model classes.
        //  Open each model file and verify / replace these names:
        //
        //  DailyLog     → TotalHours, CheckInTime, CheckOutTime, Status
        //  TaskLog      → TaskName, Status, HoursSpent
        //  BreakLog     → StartTime, EndTime, DurationMinutes
        //  SupportLog   → Description, HoursSpent
        //  DailyGoal    → GoalText, IsAchieved
        //  EODReport    → Summary   (presence of the row = report submitted)
        //  LeaveRequest → LeaveType, StartDate, EndDate, Status, Reason
        //  WFHRequest   → RequestDate ✓, Status ✓, Reason
        //  Kudos        → Message, CreatedAt
        //  LateArrivalReason → Reason
        // ─────────────────────────────────────────────────────────────────────
        private async Task<string> BuildUserContextAsync(int userId)
        {
            var sb = new StringBuilder();
            var today = DateTime.Today;
            // Monday of the current week (Sunday = 0 case handled)
            var dayNum = (int)today.DayOfWeek;
            var weekStart = today.AddDays(-(dayNum == 0 ? 6 : dayNum - 1));

            try
            {
                // ── 1. USER PROFILE ───────────────────────────────────────────
                // Confirmed: Users, User.FullName, User.Email, User.Role
                var user = await _db.Users
                    .Where(u => u.Id == userId)
                    .Select(u => new { u.FullName, u.Email, u.Role })
                    .FirstOrDefaultAsync();

                if (user is not null)
                {
                    sb.AppendLine($"USER: {user.FullName} | Role: {user.Role} | Email: {user.Email}");
                    sb.AppendLine();
                }

                // ── 2. TODAY'S DAILY LOG (= attendance record) ────────────────
                //
                // CONFIRMED: _db.DailyLogs, UserId, LogDate (unique index UserId+LogDate).
                // There is NO separate Attendances table. The DailyLog row for
                // today IS the attendance record — one row per user per day.
                //
                // ⚠️  Replace property names with your actual DailyLog model:
                //   TotalHours   → your hours property (e.g. WorkHours, Hours)
                //   CheckInTime  → your check-in property (e.g. CheckIn, StartTime)
                //   CheckOutTime → your check-out property (e.g. CheckOut, EndTime)
                //   Status       → your status property (e.g. Present/WFH/Late/Absent)
                var todayLog = await _db.DailyLogs
                    .Where(d => d.UserId == userId && d.LogDate == today)
                    .Select(d => new
                    {
                        d.TotalWorkMinutes,   // ⚠️ adjust
                        d.CheckInTime,  // ⚠️ adjust
                        d.CheckOutTime, // ⚠️ adjust
                        d.Notes        // ⚠️ adjust
                    })
                    .FirstOrDefaultAsync();

                sb.AppendLine("TODAY'S LOG & ATTENDANCE:");
                if (todayLog is not null)
                {
                    var cin = todayLog.CheckInTime.HasValue
                        ? todayLog.CheckInTime.Value.ToString("hh:mm tt") : "Not recorded";
                    var cout = todayLog.CheckOutTime.HasValue
                        ? todayLog.CheckOutTime.Value.ToString("hh:mm tt") : "Not checked out yet";
                    sb.AppendLine($"  Status: {todayLog.Notes} | Check-in: {cin} | Check-out: {cout} | Hours: {todayLog.TotalWorkMinutes:F1}h");
                }
                else
                {
                    sb.AppendLine("  ⚠ No daily log submitted for today yet.");
                }

                // ── 3. THIS WEEK'S DAILY LOGS ─────────────────────────────────
                // CONFIRMED: LogDate, UserId on DailyLog.
                // ⚠️ Adjust: TotalHours, Status
                var weekLogs = await _db.DailyLogs
                    .Where(d => d.UserId == userId
                             && d.LogDate >= weekStart
                             && d.LogDate <= today)
                    .OrderByDescending(d => d.LogDate)
                    .Select(d => new { d.LogDate, d.TotalWorkMinutes, d.Notes }) // ⚠️ adjust
                    .ToListAsync();

                var weeklyHours = weekLogs.Sum(d => (decimal)(d.TotalWorkMinutes));
                sb.AppendLine();
                sb.AppendLine($"DAILY LOGS this week ({weekStart:MMM dd}–{today:MMM dd}) | Total: {weeklyHours:F1}h:");
                if (weekLogs.Any())
                {
                    foreach (var log in weekLogs)
                        sb.AppendLine($"  • {log.LogDate:ddd MMM dd}: {(log.TotalWorkMinutes):F1}h | {log.Notes}");

                    // Flag missing days so the assistant can remind the user
                    var expectedDays = (int)(today - weekStart).TotalDays + 1;
                    if (weekLogs.Count < expectedDays)
                        sb.AppendLine($"  ⚠ {expectedDays - weekLogs.Count} day(s) have no log this week.");
                }
                else
                {
                    sb.AppendLine("  No logs recorded this week.");
                }

                // ── 4. TASK LOGS (this week) ──────────────────────────────────
                //
                // CONFIRMED: _db.TaskLogs, TaskLog.DailyLogId FK, TaskLog.DailyLog nav.
                // There is NO direct UserId on TaskLog — we join through DailyLog.
                //
                // ⚠️  Replace property names with your actual TaskLog model:
                //   TaskName   → your task name property (e.g. Title, Name, Task)
                //   Status     → your task status property
                //   HoursSpent → your hours property (e.�g. Hours, Duration, TimeSpent)
                var taskLogs = await _db.TaskLogs
                    .Include(t => t.DailyLog)
                    .Where(t => t.DailyLog.UserId == userId
                             && t.DailyLog.LogDate >= weekStart
                             && t.DailyLog.LogDate <= today)
                    .OrderByDescending(t => t.DailyLog.LogDate)
                    .Select(t => new
                    {
                        t.TaskTitle,    // ⚠️ adjust
                        t.Status,      // ⚠️ adjust
                        t.TimeSpentMinutes,  // ⚠️ adjust
                        LogDate = t.DailyLog.LogDate
                    })
                    .Take(20)
                    .ToListAsync();

                sb.AppendLine();
                sb.AppendLine($"TASK LOGS this week ({taskLogs.Count} entries):");
                if (taskLogs.Any())
                {
                    var taskHours = taskLogs.Sum(t => t.TimeSpentMinutes);
                    sb.AppendLine($"  Total task hours: {taskHours:F1}h");

                    // Group by status for a quick summary
                    var byStatus = taskLogs
                        .GroupBy(t => t.Status ?? "Unknown")
                        .Select(g => $"{g.Key}: {g.Count()}");
                    sb.AppendLine($"  Status breakdown: {string.Join(" | ", byStatus)}");

                    foreach (var t in taskLogs)
                        sb.AppendLine($"  • [{t.Status}] {t.TaskTitle} — {(t.TimeSpentMinutes):F1}h on {t.LogDate:MMM dd}");
                }
                else
                {
                    sb.AppendLine("  No task logs recorded this week.");
                }

                // ── 5. BREAK LOGS (today) ─────────────────────────────────────
                //
                // CONFIRMED: _db.BreakLogs, BreakLog.DailyLogId FK, BreakLog.DailyLog nav.
                // No direct UserId on BreakLog — join through DailyLog.
                //
                // ⚠️  Replace: StartTime, EndTime, DurationMinutes with your BreakLog model.
                var breakLogs = await _db.BreakLogs
                    .Include(b => b.DailyLog)
                    .Where(b => b.DailyLog.UserId == userId
                             && b.DailyLog.LogDate == today)
                    .Select(b => new
                    {
                        b.StartTime,       // ⚠️ adjust
                        b.EndTime,         // ⚠️ adjust
                        b.DurationMinutes  // ⚠️ adjust
                    })
                    .ToListAsync();

                if (breakLogs.Any())
                {
                    var totalBreak = breakLogs.Sum(b => b.DurationMinutes);
                    sb.AppendLine();
                    sb.AppendLine($"TODAY'S BREAKS ({breakLogs.Count} break(s) | Total: {totalBreak} min):");
                    foreach (var br in breakLogs)
                        sb.AppendLine($"  • {br.StartTime:hh:mm tt} – {br.EndTime:hh:mm tt} ({br.DurationMinutes} min)");
                }

                // ── 6. SUPPORT LOGS (this week) ───────────────────────────────
                //
                // CONFIRMED: _db.SupportLogs, SupportLog.DailyLogId FK, SupportLog.DailyLog nav,
                //            SupportLog.SupportedDeveloperId FK, SupportLog.SupportedDeveloper nav (→ User).
                // No direct UserId on SupportLog — join through DailyLog.
                //
                // ⚠️  Replace: Description, HoursSpent with your SupportLog model.
                var supportLogs = await _db.SupportLogs
                    .Include(s => s.DailyLog)
                    .Include(s => s.SupportedDeveloper)
                    .Where(s => s.DailyLog.UserId == userId
                             && s.DailyLog.LogDate >= weekStart
                             && s.DailyLog.LogDate <= today)
                    .OrderByDescending(s => s.DailyLog.LogDate)
                    .Select(s => new
                    {
                        s.IssueDescription,   // ⚠️ adjust
                        s.TimeSpentMinutes,    // ⚠️ adjust
                        LogDate = s.DailyLog.LogDate,
                        HelpedName = s.SupportedDeveloper != null
                            ? s.SupportedDeveloper.FullName : "General"
                    })
                    .Take(10)
                    .ToListAsync();

                if (supportLogs.Any())
                {
                    var supportTotal = supportLogs.Sum(s => s.TimeSpentMinutes );
                    sb.AppendLine();
                    sb.AppendLine($"SUPPORT WORK this week ({supportLogs.Count} entries | {supportTotal:F1}h):");
                    foreach (var s in supportLogs)
                        sb.AppendLine($"  • {s.LogDate:MMM dd}: {s.IssueDescription} — {(s.TimeSpentMinutes):F1}h (helped: {s.HelpedName})");
                }

                // ── 7. LATE ARRIVAL REASON (today) ────────────────────────────
                //
                // CONFIRMED: _db.LateArrivalReasons, LateArrivalReason.DailyLogId FK,
                //            LateArrivalReason.UserId FK (direct).
                //
                // ⚠️  Replace: Reason with your LateArrivalReason model property.
                var lateReason = await _db.LateArrivalReasons
                    .Where(l => l.UserId == userId
                             && l.DailyLog.LogDate == today)
                    .Select(l => new { l.Reason }) // ⚠️ adjust
                    .FirstOrDefaultAsync();

                if (lateReason is not null)
                {
                    sb.AppendLine();
                    sb.AppendLine($"LATE ARRIVAL TODAY: Reason logged — \"{lateReason.Reason}\"");
                }

                // ── 8. DAILY GOALS (this week) ────────────────────────────────
                //
                // CONFIRMED: _db.DailyGoals, DailyGoal.UserId FK, GoalDate (unique index).
                //
                // ⚠️  Replace: GoalText, IsAchieved with your DailyGoal model.
                var goals = await _db.DailyGoals
                    .Where(g => g.UserId == userId
                             && g.GoalDate >= weekStart
                             && g.GoalDate <= today)
                    .OrderByDescending(g => g.GoalDate)
                    .Select(g => new { g.GoalDate,  g.TargetTasksCompleted }) // ⚠️ adjust
                    .ToListAsync();

                if (goals.Any())
                {
                    var achieved = goals.Count(g => g.TargetTasksCompleted > 0);
                    sb.AppendLine();
                    sb.AppendLine($"DAILY GOALS this week ({achieved}/{goals.Count} achieved):");
                    foreach (var g in goals)
                        sb.AppendLine($"  • {(g.TargetTasksCompleted > 0 ? "✓" : "○")} {g.GoalDate:ddd MMM dd}");

                    if (!goals.Any(g => g.GoalDate == today))
                        sb.AppendLine("  ⚠ No goal set for today yet.");
                }

                // ── 9. EOD REPORTS (this week) ────────────────────────────────
                //
                // CONFIRMED: _db.EODReports, EODReport.UserId FK, ReportDate (unique index),
                //            EODReport.DailyLogId FK (links to that day's log).
                // Presence of a row = report was submitted for that date.
                //
                // ⚠️  Replace: Summary with your EODReport model property.
                var eodReports = await _db.EODReports
                    .Where(r => r.UserId == userId
                             && r.ReportDate >= weekStart
                             && r.ReportDate <= today)
                    .OrderByDescending(r => r.ReportDate)
                    .Select(r => new { r.ReportDate, r.Learnings }) // ⚠️ adjust
                    .ToListAsync();

                sb.AppendLine();
                sb.AppendLine($"EOD REPORTS this week ({eodReports.Count} submitted):");
                if (eodReports.Any())
                {
                    foreach (var eod in eodReports)
                    {
                        var preview = eod.Learnings?.Length > 80
                            ? eod.Learnings[..80] + "…"
                            : eod.Learnings ?? "(no summary)";
                        sb.AppendLine($"  • {eod.ReportDate:ddd MMM dd}: {preview}");
                    }
                }

                if (!eodReports.Any(r => r.ReportDate == today))
                    sb.AppendLine("  ⚠ EOD report not yet submitted for today.");

                // ── 10. LEAVE REQUESTS ────────────────────────────────────────
                //
                // CONFIRMED: _db.LeaveRequests, LeaveRequest.UserId FK,
                //            LeaveRequest.ReviewedByUserId FK → ReviewedBy nav.
                //
                // ⚠️  Replace: LeaveType, StartDate, EndDate, Status, Reason
                //     with your LeaveRequest model property names.
                var leaves = await _db.LeaveRequests
                    .Where(l => l.UserId == userId)
                    .OrderByDescending(l => l.FromDate) // ⚠️ adjust if named differently
                    .Select(l => new
                    {
                        l.LeaveType, // ⚠️ adjust
                        l.FromDate, // ⚠️ adjust
                        l.ToDate,   // ⚠️ adjust
                        l.Status,    // ⚠️ adjust
                        l.Reason     // ⚠️ adjust
                    })
                    .Take(5)
                    .ToListAsync();

                sb.AppendLine();
                sb.AppendLine($"LEAVE REQUESTS (last {leaves.Count}):");
                if (leaves.Any())
                {
                    foreach (var lr in leaves)
                        sb.AppendLine($"  • [{lr.Status}] {lr.LeaveType}: {lr.FromDate:MMM dd}–{lr.ToDate:MMM dd} | {lr.Reason}");

                    var pendingCount = leaves.Count(l => l.Status == "Pending");
                    if (pendingCount > 0)
                        sb.AppendLine($"  ⚠ {pendingCount} leave request(s) awaiting manager approval.");
                }
                else
                {
                    sb.AppendLine("  No leave requests found.");
                }

                // ── 11. WFH REQUESTS ──────────────────────────────────────────
                //
                // CONFIRMED: _db.WFHRequests, WFHRequest.UserId FK,
                //            RequestDate + Status (unique index), ReviewedByUserId FK,
                //            DailyLogId nullable FK → WFHRequest.DailyLog nav.
                //
                // ⚠️  Replace: Reason with your WFHRequest model property name.
                var wfhRequests = await _db.WFHRequests
                    .Where(w => w.UserId == userId
                             && w.RequestDate >= today.AddDays(-14))
                    .OrderByDescending(w => w.RequestDate)
                    .Select(w => new
                    {
                        w.RequestDate, // ✓ confirmed
                        w.Status,      // ✓ confirmed
                        w.Reason       // ⚠️ adjust
                    })
                    .Take(5)
                    .ToListAsync();

                if (wfhRequests.Any())
                {
                    sb.AppendLine();
                    sb.AppendLine($"WFH REQUESTS (last 14 days | {wfhRequests.Count}):");
                    foreach (var w in wfhRequests)
                        sb.AppendLine($"  • [{w.Status}] {w.RequestDate:MMM dd} — {w.Reason}");

                    var pendingWfh = wfhRequests.Count(w => w.Status == "Pending");
                    if (pendingWfh > 0)
                        sb.AppendLine($"  ⚠ {pendingWfh} WFH request(s) awaiting approval.");
                }

                // ── 12. KUDOS RECEIVED (last 30 days) ────────────────────────
                //
                // CONFIRMED: _db.Kudos, Kudos.ToUserId FK → ToUser nav,
                //            Kudos.FromUserId FK → FromUser nav (User.FullName).
                //
                // ⚠️  Replace: Message, CreatedAt with your Kudos model property names.
                var kudos = await _db.Kudos
                    .Include(k => k.FromUser)
                    .Where(k => k.ToUserId == userId
                             && k.GivenAt >= today.AddDays(-30)) // ⚠️ adjust CreatedAt
                    .OrderByDescending(k => k.GivenAt)            // ⚠️ adjust CreatedAt
                    .Select(k => new
                    {
                        k.Message,    // ⚠️ adjust
                        k.GivenAt,  // ⚠️ adjust
                        FromName = k.FromUser != null ? k.FromUser.FullName : "A teammate"
                    })
                    .Take(3)
                    .ToListAsync();

                if (kudos.Any())
                {
                    sb.AppendLine();
                    sb.AppendLine($"KUDOS RECEIVED (last 30 days | {kudos.Count}):");
                    foreach (var k in kudos)
                        sb.AppendLine($"  • {k.GivenAt:MMM dd} from {k.FromName}: \"{k.Message}\"");
                }
            }
            catch (Exception ex)
            {
                // Never crash the AI request because of a DB error.
                // Return partial context built so far, plus an error note.
                sb.AppendLine();
                sb.AppendLine($"[Note: Some context could not be loaded — {ex.Message}]");
            }

            return sb.ToString();
        }
    }
}