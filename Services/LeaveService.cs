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
    }

    public class LeaveService : ILeaveService
    {
        private readonly AppDbContext _db;
        private readonly INotificationSender _notif;

        public LeaveService(AppDbContext db, INotificationSender notif)
        {
            _db = db;
            _notif = notif;
        }

        public async Task<LeaveResponseDto> ApplyAsync(int userId, ApplyLeaveDto dto)
        {
            var leave = new LeaveRequest
            {
                UserId = userId,
                FromDate = dto.FromDate.Date,
                ToDate = dto.ToDate.Date,
                LeaveType = dto.LeaveType,
                Reason = dto.Reason
            };

            _db.LeaveRequests.Add(leave);
            await _db.SaveChangesAsync();

            // Notify managers
            await _notif.SendToManagers("LeaveApplied", new
            {
                UserId = userId,
                LeaveId = leave.Id,
                Message = "New leave request pending review"
            });

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
            var leave = await _db.LeaveRequests.FindAsync(leaveId)
                ?? throw new KeyNotFoundException("Leave request not found.");

            leave.Status = dto.Status;
            leave.ReviewedByUserId = managerId;
            leave.ReviewNote = dto.ReviewNote;
            leave.ReviewedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            // Notify applicant
            await _notif.SendToUser(leave.UserId, "ReceiveNotification", new
            {
                Title = $"Leave {dto.Status}",
                Message = $"Your leave request has been {dto.Status.ToLower()}{(dto.ReviewNote != null ? $": {dto.ReviewNote}" : "")}",
                Type = dto.Status == "Approved" ? "Success" : "Warning"
            });
        }

        public async Task CancelAsync(int leaveId, int userId)
        {
            var leave = await _db.LeaveRequests
                .FirstOrDefaultAsync(l => l.Id == leaveId && l.UserId == userId && l.Status == "Pending")
                ?? throw new InvalidOperationException("Leave request not found or cannot be cancelled.");

            _db.LeaveRequests.Remove(leave);
            await _db.SaveChangesAsync();
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
    }
}
