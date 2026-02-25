using DailyTrackerAPI.Data;
using DailyTrackerAPI.DTOs;
using DailyTrackerAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace DailyTrackerAPI.Services
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Feature 1: Notification Service
    // ─────────────────────────────────────────────────────────────────────────
    public interface IAppNotificationService
    {
        Task<List<NotificationDto>> GetMyNotificationsAsync(int userId, bool unreadOnly = false);
        Task MarkReadAsync(int notifId, int userId);
        Task MarkAllReadAsync(int userId);
        Task<int> GetUnreadCountAsync(int userId);
        Task CreateAsync(int userId, string title, string message, string type = "Info", string? actionUrl = null);
    }

    public class AppNotificationService : IAppNotificationService
    {
        private readonly AppDbContext _db;
        private readonly INotificationSender _sender;

        public AppNotificationService(AppDbContext db, INotificationSender sender)
        {
            _db = db;
            _sender = sender;
        }

        public async Task<List<NotificationDto>> GetMyNotificationsAsync(int userId, bool unreadOnly = false)
        {
            var query = _db.Notifications.Where(n => n.UserId == userId);
            if (unreadOnly) query = query.Where(n => !n.IsRead);

            return await query
                .OrderByDescending(n => n.CreatedAt)
                .Take(50)
                .Select(n => new NotificationDto
                {
                    Id = n.Id,
                    Title = n.Title,
                    Message = n.Message,
                    Type = n.Type,
                    ActionUrl = n.ActionUrl,
                    IsRead = n.IsRead,
                    CreatedAt = n.CreatedAt
                }).ToListAsync();
        }

        public async Task MarkReadAsync(int notifId, int userId)
        {
            var n = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == notifId && n.UserId == userId);
            if (n != null) { n.IsRead = true; await _db.SaveChangesAsync(); }
        }

        public async Task MarkAllReadAsync(int userId)
        {
            await _db.Notifications
                .Where(n => n.UserId == userId && !n.IsRead)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
        }

        public Task<int> GetUnreadCountAsync(int userId) =>
            _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);

        public async Task CreateAsync(int userId, string title, string message,
            string type = "Info", string? actionUrl = null)
        {
            _db.Notifications.Add(new AppNotification
            {
                UserId = userId,
                Title = title,
                Message = message,
                Type = type,
                ActionUrl = actionUrl
            });
            await _db.SaveChangesAsync();

            // Push real-time
            await _sender.SendToUser(userId, "ReceiveNotification", new
            {
                Title = title,
                Message = message,
                Type = type,
                ActionUrl = actionUrl
            });
        }
    }
}
