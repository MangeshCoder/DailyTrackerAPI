using DailyTrackerAPI.Custom;
using DailyTrackerAPI.Data;
using DailyTrackerAPI.DTOs;
using DailyTrackerAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace DailyTrackerAPI.Services
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Feature 9: Leave Service
    // ─────────────────────────────────────────────────────────────────────────
    public interface ILeaveService
    {
        Task<LeaveResponseDto> ApplyAsync(int userId, ApplyLeaveDto dto);
        Task<List<LeaveResponseDto>> GetMyLeavesAsync(int userId);
        Task<List<LeaveResponseDto>> GetAllLeavesAsync(string? status = null); // Manager
        Task ReviewAsync(int leaveId, int managerId, ReviewLeaveDto dto);
        Task CancelAsync(int leaveId, int userId);
        Task<List<LeaveBalanceDto>> GetMonthlyBalanceAsync(int? userId = null);
    }

    public class LeaveService : ILeaveService
    {
        private readonly AppDbContext _db;
        private readonly INotificationSender _notif;
        private readonly IEmailService _email;
        private readonly IEmailActionService _emailAction;

        public LeaveService(AppDbContext db, INotificationSender notif, IEmailService email, IEmailActionService emailAction)
        {
            _db = db;
            _notif = notif;
            _email = email;
            _emailAction = emailAction;
        }

        public async Task<LeaveResponseDto> ApplyAsync(int userId, ApplyLeaveDto dto)
        {
            if (dto.FromDate.Date > dto.ToDate.Date)
                throw new Exception("From date cannot be after To date.");

            var user = await _db.Users.FindAsync(userId)
                ?? throw new Exception("User not found");

            // ✅ STEP 1: Count total requested working days
            int totalRequestedDays = CountWorkingDays(dto.FromDate, dto.ToDate);

            if (totalRequestedDays <= 0)
                throw new Exception("Selected dates contain only weekends.");

            // ✅ STEP 2: Validate month-by-month (handles cross-month leave)
            var currentMonth = new DateTime(dto.FromDate.Year, dto.FromDate.Month, 1);

            while (currentMonth <= dto.ToDate)
            {
                var monthStart = new DateTime(currentMonth.Year, currentMonth.Month, 1);
                var monthEnd = monthStart.AddMonths(1).AddDays(-1);

                // Count Approved + Pending leaves
                var existingLeaves = await _db.LeaveRequests
                    .Where(l =>
                        l.UserId == userId &&
                        (l.Status == "Approved" || l.Status == "Pending") &&
                        l.FromDate <= monthEnd &&
                        l.ToDate >= monthStart)
                    .ToListAsync();

                int usedDays = 0;

                foreach (var leaves in existingLeaves)
                {
                    var overlapStart = leaves.FromDate < monthStart ? monthStart : leaves.FromDate;
                    var overlapEnd = leaves.ToDate > monthEnd ? monthEnd : leaves.ToDate;

                    usedDays += CountWorkingDays(overlapStart, overlapEnd);
                }

                // Count requested days for this specific month
                var requestedStart = dto.FromDate < monthStart ? monthStart : dto.FromDate;
                var requestedEnd = dto.ToDate > monthEnd ? monthEnd : dto.ToDate;

                int requestedDaysThisMonth = CountWorkingDays(requestedStart, requestedEnd);

                if (usedDays + requestedDaysThisMonth > 2)
                {
                    throw new ValidationException(
                        $"Monthly leave limit exceeded for {monthStart:MMMM yyyy}. " +
                        $"Used: {usedDays}, Requested: {requestedDaysThisMonth}, Max allowed: 2 working days."
                    );
                }

                currentMonth = currentMonth.AddMonths(1);
            }

            // ✅ STEP 3: Create Leave (UNCHANGED from your code)
            var leave = new LeaveRequest
            {
                UserId = userId,
                FromDate = dto.FromDate.Date,
                ToDate = dto.ToDate.Date,
                LeaveType = dto.LeaveType,
                Reason = dto.Reason,
                Status = "Pending"
            };

            _db.LeaveRequests.Add(leave);
            await _db.SaveChangesAsync();

            // 🔔 EXISTING Notification (UNCHANGED)
            await _notif.SendToManagers("LeaveApplied", new
            {
                UserId = userId,
                LeaveId = leave.Id,
                Message = "New leave request pending review"
            });

            // 📧 EXISTING Email Logic (UNCHANGED)
            var managers = await _db.Users
                .Where(u => u.Role == "Manager")
                .ToListAsync();

            foreach (var manager in managers)
            {
                var token = await _emailAction
                    .CreateTokenAsync(leave.Id, manager.Id);

                await _email.SendLeaveAppliedEmailAsync(
                    manager.Email,
                    manager.FullName,
                    user.FullName,
                    leave,
                    token
                );
            }

            return await MapLeave(leave);
        }

        public async Task<List<LeaveResponseDto>> GetMyLeavesAsync(int userId)
        {
            var leaves = await _db.LeaveRequests
                .Include(l => l.User)
                .Include(l => l.ReviewedBy)
                .Where(l => l.UserId == userId)
                .OrderByDescending(l => l.AppliedAt)
                .ToListAsync();

            var result = new List<LeaveResponseDto>();
            foreach (var l in leaves) result.Add(await MapLeave(l));
            return result;
        }

        public async Task<List<LeaveResponseDto>> GetAllLeavesAsync(string? status = null)
        {
            var query = _db.LeaveRequests
                .Include(l => l.User)
                .Include(l => l.ReviewedBy)
                .AsQueryable();

            if (!string.IsNullOrEmpty(status))
                query = query.Where(l => l.Status == status);

            var leaves = await query.OrderByDescending(l => l.AppliedAt).ToListAsync();
            var result = new List<LeaveResponseDto>();
            foreach (var l in leaves) result.Add(await MapLeave(l));
            return result;
        }

        public async Task ReviewAsync(int leaveId, int managerId, ReviewLeaveDto dto)
        {
            var leave = await _db.LeaveRequests
                .Include(l => l.User)
                .FirstOrDefaultAsync(l => l.Id == leaveId)
                ?? throw new ValidationException("Leave request not found.");

            var manager = await _db.Users.FindAsync(managerId)
                ?? throw new ValidationException("Manager not found");

            leave.Status = dto.Status;
            leave.ReviewedByUserId = managerId;
            leave.ReviewNote = dto.ReviewNote;
            leave.ReviewedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            // 🔔 Existing notification
            await _notif.SendToUser(leave.UserId, "ReceiveNotification", new
            {
                Title = $"Leave {dto.Status}",
                Message = $"Your leave request has been {dto.Status.ToLower()}",
                Type = dto.Status == "Approved" ? "Success" : "Warning"
            });

            // 📧 NEW: Email to Employee
            await _email.SendLeaveReviewedEmailAsync(
                leave.User.Email,
                leave.User.FullName,
                manager.FullName,
                leave,
                dto.Status,
                dto.ReviewNote
            );
        }

        public async Task CancelAsync(int leaveId, int userId)
        {
            var leave = await _db.LeaveRequests
                .FirstOrDefaultAsync(l => l.Id == leaveId && l.UserId == userId && l.Status == "Pending")
                ?? throw new ValidationException("Leave request not found or cannot be cancelled.");

            _db.LeaveRequests.Remove(leave);
            await _db.SaveChangesAsync();
        }

        public async Task<List<LeaveBalanceDto>> GetMonthlyBalanceAsync(int? userId = null)
        {
            var now = DateTime.UtcNow;
            var monthStart = new DateTime(now.Year, now.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            var query = _db.LeaveRequests
                .Where(l =>
                    l.Status != "Rejected" &&
                    l.FromDate <= monthEnd &&
                    l.ToDate >= monthStart);

            if (userId.HasValue)
                query = query.Where(l => l.UserId == userId.Value);

            var leaves = await query
                .Include(l => l.User)
                .ToListAsync();

            var grouped = leaves.GroupBy(l => l.UserId);

            var result = new List<LeaveBalanceDto>();

            foreach (var group in grouped)
            {
                int usedDays = 0;

                foreach (var leaveItem in group)
                {
                    var start = leaveItem.FromDate < monthStart ? monthStart : leaveItem.FromDate;
                    var end = leaveItem.ToDate > monthEnd ? monthEnd : leaveItem.ToDate;

                    usedDays += CountWorkingDays(start, end);
                }

                result.Add(new LeaveBalanceDto
                {
                    UserId = group.Key,
                    UserName = group.First().User.FullName,
                    UsedDays = usedDays,
                    RemainingDays = Math.Max(0, 2 - usedDays)
                });
            }

            return result;
        }

        private static Task<LeaveResponseDto> MapLeave(LeaveRequest l) =>
            Task.FromResult(new LeaveResponseDto
            {
                Id = l.Id,
                UserName = l.User?.FullName ?? "Unknown",
                FromDate = l.FromDate,
                ToDate = l.ToDate,
                LeaveDays = CountDays(l.FromDate, l.ToDate),
                LeaveType = l.LeaveType,
                Reason = l.Reason,
                Status = l.Status,
                ReviewerName = l.ReviewedBy?.FullName,
                ReviewNote = l.ReviewNote,
                ReviewedAt = l.ReviewedAt,
                AppliedAt = l.AppliedAt
            });

        private static int CountDays(DateTime from, DateTime to)
        {
            int days = 0;
            for (var d = from; d <= to; d = d.AddDays(1))
                if (d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                    days++;
            return Math.Max(1, days);
        }

        private int CountWorkingDays(DateTime from, DateTime to)
        {
            int count = 0;

            for (var date = from.Date; date <= to.Date; date = date.AddDays(1))
            {
                if (date.DayOfWeek != DayOfWeek.Saturday &&
                    date.DayOfWeek != DayOfWeek.Sunday)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
