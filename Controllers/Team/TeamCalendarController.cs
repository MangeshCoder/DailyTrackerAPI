using DailyTrackerAPI.Data;
using DailyTrackerAPI.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DailyTrackerAPI.Controllers.Team
{
    // ─── Team Availability Calendar ──────────────────────────────────────────
    // GET /api/team-calendar?month=6&year=2025
    //
    // Accessible by ALL authenticated users (not manager-only).
    // Returns a per-day, per-member status grid for the requested month.
    //
    // Status priority for each (user, day) pair:
    //   Weekend  → skip (IsWeekend = true, no member rows)
    //   Leave    → approved LeaveRequest spanning that date
    //   WFH      → approved WFHRequest for that date (type = WFH)
    //   HalfDay  → approved WFHRequest for that date (type = HalfDay)
    //   Present  → DailyLog exists for that date with CheckInTime
    //   Absent   → past working day, no log
    //   Unknown  → today or future working day, no log yet
    [ApiController, Route("api/team-calendar"), Authorize]
    public class TeamCalendarController : ControllerBase
    {
        private readonly AppDbContext _db;

        public TeamCalendarController(AppDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> GetCalendar(
            [FromQuery] int? month = null,
            [FromQuery] int? year = null)
        {
            var today = DateTime.UtcNow.Date;
            var m = month ?? today.Month;
            var y = year ?? today.Year;

            var from = new DateTime(y, m, 1);
            var to = from.AddMonths(1);               // exclusive upper bound
            var daysInMonth = DateTime.DaysInMonth(y, m);

            // ── 1. Load all active users ──────────────────────────────────────
            var users = await _db.Users
                .Where(u => u.IsActive)
                .Select(u => new { u.Id, u.FullName, u.Role, u.ProfilePhotoUrl })
                .OrderBy(u => u.FullName)
                .ToListAsync();

            // ── 2. Load DailyLogs for the month ──────────────────────────────
            var logs = await _db.DailyLogs
                .Where(d => d.LogDate >= from && d.LogDate < to)
                .Select(d => new { d.UserId, Date = d.LogDate.Date, d.CheckInTime, d.DayStatus })
                .ToListAsync();

            // ── 3. Load approved WFH / HalfDay requests for the month ─────────
            var wfhRequests = await _db.WFHRequests
                .Where(r => r.RequestDate >= from && r.RequestDate < to
                         && r.Status == "Approved")
                .Select(r => new { r.UserId, Date = r.RequestDate.Date, r.RequestType })
                .ToListAsync();

            // ── 4. Load approved Leave requests overlapping this month ─────────
            var leaveRequests = await _db.LeaveRequests
                .Where(l => l.Status == "Approved"
                         && l.FromDate < to          // leave starts before month ends
                         && l.ToDate >= from)       // leave ends after month starts
                .Select(l => new { l.UserId, l.FromDate, l.ToDate, l.LeaveType })
                .ToListAsync();

            // ── 5. Load holidays for this year ────────────────────────────────
            var holidays = await _db.Holidays
                .Where(h => h.Year == y)
                .Select(h => new { Date = h.Date.Date, h.Name })
                .ToDictionaryAsync(h => h.Date);

            // ── 6. Pre-index for fast lookup ──────────────────────────────────
            // logs: (userId, date) → log
            var logIndex = logs.ToLookup(l => (l.UserId, l.Date));
            // wfh: (userId, date) → requestType
            var wfhIndex = wfhRequests.ToLookup(r => (r.UserId, r.Date));

            // ── 7. Build day-by-day grid ──────────────────────────────────────
            var days = new List<CalendarDayDto>(daysInMonth);

            for (int d = 1; d <= daysInMonth; d++)
            {
                var date = new DateTime(y, m, d);
                var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                holidays.TryGetValue(date, out var holiday);

                var day = new CalendarDayDto
                {
                    Date = date.ToString("yyyy-MM-dd"),
                    Weekday = date.ToString("ddd"),   // Mon, Tue …
                    IsWeekend = isWeekend,
                    IsHoliday = holiday != null,
                    HolidayName = holiday?.Name,
                    IsToday = date == today,
                };

                // Skip member rows for weekends (calendar still shows the cell,
                // just with no attendance data)
                if (!isWeekend)
                {
                    foreach (var user in users)
                    {
                        var status = "Unknown";
                        string? leaveType = null;

                        // Priority 1 — approved leave spanning this date
                        var onLeave = leaveRequests.FirstOrDefault(l =>
                            l.UserId == user.Id &&
                            l.FromDate.Date <= date &&
                            l.ToDate.Date >= date);

                        if (onLeave != null)
                        {
                            status = "Leave";
                            leaveType = onLeave.LeaveType;
                        }
                        else
                        {
                            // Priority 2 — approved WFH / HalfDay request
                            var wfh = wfhIndex[(user.Id, date)].FirstOrDefault();
                            if (wfh != null)
                            {
                                status = wfh.RequestType; // "WFH" or "HalfDay"
                            }
                            else
                            {
                                // Priority 3 — DailyLog
                                var log = logIndex[(user.Id, date)].FirstOrDefault();
                                if (log != null)
                                {
                                    status = log.DayStatus; // Present, WFH, HalfDay
                                }
                                else if (date < today && !isWeekend && holiday == null)
                                {
                                    // Past working day with no data → Absent
                                    status = "Absent";
                                }
                                // Future or today with no data → stays "Unknown"
                            }
                        }

                        day.Members.Add(new CalendarMemberDayDto
                        {
                            UserId = user.Id,
                            FullName = user.FullName,
                            ProfilePhotoUrl = user.ProfilePhotoUrl,
                            Role = user.Role,
                            Status = status,
                            LeaveType = leaveType,
                        });
                    }
                }

                days.Add(day);
            }

            return Ok(new TeamCalendarResponseDto
            {
                Month = m,
                Year = y,
                Label = from.ToString("MMMM yyyy"),
                Days = days,
            });
        }
    }
}
