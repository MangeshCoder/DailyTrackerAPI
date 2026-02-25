using DailyTrackerAPI.Data;
using DailyTrackerAPI.DTOs;
using DailyTrackerAPI.Models;
using Microsoft.EntityFrameworkCore;


namespace DailyTrackerAPI.Services
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Feature 4: Goal Service  –  daily targets + productivity score
    // ─────────────────────────────────────────────────────────────────────────
    public interface IGoalService
    {
        Task<DailyGoal> SetOrUpdateGoalAsync(int userId, SetGoalDto dto);
        Task<GoalProgressDto> GetTodayProgressAsync(int userId);
        Task<List<ProductivityTrendDto>> GetProductivityTrendAsync(int userId, int days = 14);
    }

    public class GoalService : IGoalService
    {
        private readonly AppDbContext _db;

        public GoalService(AppDbContext db) { _db = db; }

        public async Task<DailyGoal> SetOrUpdateGoalAsync(int userId, SetGoalDto dto)
        {
            var today = DateTime.UtcNow.Date;
            var goal = await _db.DailyGoals
                .FirstOrDefaultAsync(g => g.UserId == userId && g.GoalDate == today);

            if (goal == null)
            {
                goal = new DailyGoal { UserId = userId, GoalDate = today };
                _db.DailyGoals.Add(goal);
            }

            goal.TargetWorkMinutes = dto.TargetWorkMinutes;
            goal.TargetTasksCompleted = dto.TargetTasksCompleted;
            goal.TargetBreakMinutes = dto.TargetBreakMinutes;
            goal.ManagerSetNote = dto.ManagerSetNote;

            await _db.SaveChangesAsync();
            return goal;
        }

        public async Task<GoalProgressDto> GetTodayProgressAsync(int userId)
        {
            var today = DateTime.UtcNow.Date;

            // Load or create today's goal with defaults
            var goal = await _db.DailyGoals
                .FirstOrDefaultAsync(g => g.UserId == userId && g.GoalDate == today)
                ?? new DailyGoal
                {
                    TargetWorkMinutes = 480,
                    TargetTasksCompleted = 5,
                    TargetBreakMinutes = 60
                };

            // Load today's log
            var log = await _db.DailyLogs
                .Include(d => d.TaskLogs)
                .Include(d => d.BreakLogs)
                .FirstOrDefaultAsync(d => d.UserId == userId && d.LogDate == today);

            // Calculate live work minutes
            int actualWork = 0, actualBreak = 0, actualTasks = 0;
            if (log != null)
            {
                actualWork = log.TotalWorkMinutes;
                if (log.CheckInTime != null && log.CheckOutTime == null)
                {
                    var elapsed = (int)(DateTime.UtcNow - log.CheckInTime.Value).TotalMinutes;
                    var breaks = log.BreakLogs.Where(b => !b.IsActive).Sum(b => b.DurationMinutes);
                    var active = log.BreakLogs.FirstOrDefault(b => b.IsActive);
                    if (active != null) breaks += (int)(DateTime.UtcNow - active.StartTime).TotalMinutes;
                    actualWork = Math.Max(0, elapsed - breaks);
                }

                actualBreak = log.BreakLogs.Where(b => !b.IsActive).Sum(b => b.DurationMinutes);
                actualTasks = log.TaskLogs.Count(t => t.Status == "Completed");
            }

            // Calculate progress percentages (capped at 100)
            double workPct = goal.TargetWorkMinutes > 0
                ? Math.Min(100, Math.Round((double)actualWork / goal.TargetWorkMinutes * 100, 1)) : 0;
            double taskPct = goal.TargetTasksCompleted > 0
                ? Math.Min(100, Math.Round((double)actualTasks / goal.TargetTasksCompleted * 100, 1)) : 0;
            double breakPct = goal.TargetBreakMinutes > 0
                ? Math.Min(100, Math.Round((double)actualBreak / goal.TargetBreakMinutes * 100, 1)) : 0;

            // Productivity score: 50% work hours, 40% tasks, 10% break (wellness)
            double score = Math.Round(workPct * 0.5 + taskPct * 0.4 + breakPct * 0.1, 1);

            string grade = score >= 90 ? "A" : score >= 75 ? "B" : score >= 60 ? "C" : "D";

            // Insights
            var insights = new List<string>();
            if (workPct >= 100) insights.Add("🎯 Work hours target achieved!");
            else if (workPct >= 50) insights.Add($"⏱️ {100 - workPct:F0}% more to hit your work target");
            else insights.Add("🚀 Keep going — you're getting there!");

            if (taskPct >= 100) insights.Add("✅ All tasks done for today!");
            else if (actualTasks > 0) insights.Add($"📋 {goal.TargetTasksCompleted - actualTasks} more tasks to complete");

            if (actualBreak == 0 && actualWork > 120) insights.Add("☕ Don't forget to take a break!");
            if (score >= 80) insights.Add("🌟 Excellent productivity today!");

            return new GoalProgressDto
            {
                Goal = new SetGoalDto
                {
                    TargetWorkMinutes = goal.TargetWorkMinutes,
                    TargetTasksCompleted = goal.TargetTasksCompleted,
                    TargetBreakMinutes = goal.TargetBreakMinutes
                },
                ActualWorkMinutes = actualWork,
                ActualTasksCompleted = actualTasks,
                ActualBreakMinutes = actualBreak,
                WorkProgress = workPct,
                TaskProgress = taskPct,
                BreakProgress = breakPct,
                ProductivityScore = score,
                ScoreGrade = grade,
                Insights = insights
            };
        }

        public async Task<List<ProductivityTrendDto>> GetProductivityTrendAsync(int userId, int days = 14)
        {
            var from = DateTime.UtcNow.Date.AddDays(-days);
            var logs = await _db.DailyLogs
                .Include(d => d.TaskLogs)
                .Where(d => d.UserId == userId && d.LogDate >= from)
                .OrderBy(d => d.LogDate)
                .ToListAsync();

            return logs.Select(log =>
            {
                var tasks = log.TaskLogs.Count(t => t.Status == "Completed");
                // Simple score: 50% of work ratio + 50% task ratio (vs 8h/5 tasks baseline)
                var workPct = Math.Min(100.0, log.TotalWorkMinutes / 480.0 * 100);
                var taskPct = Math.Min(100.0, tasks / 5.0 * 100);
                return new ProductivityTrendDto
                {
                    Date = log.LogDate,
                    Score = Math.Round(workPct * 0.5 + taskPct * 0.5, 1),
                    WorkMinutes = log.TotalWorkMinutes,
                    TasksCompleted = tasks,
                    DayName = log.LogDate.ToString("ddd")
                };
            }).ToList();
        }
    }
}
