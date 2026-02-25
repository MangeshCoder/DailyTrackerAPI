using DailyTrackerAPI.Helpers;
using DailyTrackerAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DailyTrackerAPI.Controllers
{
    // ─── Analytics Controller ─────────────────────────────────────────────────
    [ApiController, Route("api/analytics"), Authorize]
    public class AnalyticsController : ControllerBase
    {
        private readonly IAnalyticsService _analyticsSvc;
        public AnalyticsController(IAnalyticsService analyticsSvc) => _analyticsSvc = analyticsSvc;

        [HttpGet]
        public async Task<IActionResult> GetAdvanced([FromQuery] int days = 90)
        {
            var data = await _analyticsSvc.GetAdvancedAnalyticsAsync(User.GetUserId(), days);
            return Ok(data);
        }

        [HttpGet("heatmap")]
        public async Task<IActionResult> GetHeatmap([FromQuery] int days = 365)
        {
            var data = await _analyticsSvc.GetHeatmapAsync(User.GetUserId(), days);
            return Ok(data);
        }

        [HttpGet("projects")]
        public async Task<IActionResult> GetProjects([FromQuery] int days = 30)
        {
            var data = await _analyticsSvc.GetProjectBreakdownAsync(User.GetUserId(), days);
            return Ok(data);
        }

        [HttpGet("peak-hours")]
        public async Task<IActionResult> GetPeakHours([FromQuery] int days = 30)
        {
            var data = await _analyticsSvc.GetPeakHoursAsync(User.GetUserId(), days);
            return Ok(data);
        }

        // Manager: get any user's analytics
        [HttpGet("user/{userId}"), Authorize(Roles = "Manager")]
        public async Task<IActionResult> GetUserAnalytics(int userId, [FromQuery] int days = 90)
        {
            var data = await _analyticsSvc.GetAdvancedAnalyticsAsync(userId, days);
            return Ok(data);
        }
    }
}
