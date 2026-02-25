using DailyTrackerAPI.Helpers;
using DailyTrackerAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DailyTrackerAPI.Controllers
{
    // ─── Notifications Controller ─────────────────────────────────────────────
    [ApiController, Route("api/notifications"), Authorize]
    public class NotificationsController : ControllerBase
    {
        private readonly IAppNotificationService _notifSvc;
        public NotificationsController(IAppNotificationService notifSvc) => _notifSvc = notifSvc;

        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] bool unreadOnly = false)
        {
            var list = await _notifSvc.GetMyNotificationsAsync(User.GetUserId(), unreadOnly);
            return Ok(list);
        }

        [HttpGet("count")]
        public async Task<IActionResult> GetCount()
        {
            var count = await _notifSvc.GetUnreadCountAsync(User.GetUserId());
            return Ok(new { count });
        }

        [HttpPut("{id}/read")]
        public async Task<IActionResult> MarkRead(int id)
        {
            await _notifSvc.MarkReadAsync(id, User.GetUserId());
            return Ok();
        }

        [HttpPut("read-all")]
        public async Task<IActionResult> MarkAllRead()
        {
            await _notifSvc.MarkAllReadAsync(User.GetUserId());
            return Ok();
        }
    }
}
