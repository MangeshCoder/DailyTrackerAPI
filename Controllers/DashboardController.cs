using DailyTrackerAPI.Helpers;
using DailyTrackerAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DailyTrackerAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DashboardController : ControllerBase
    {
        private readonly IDashboardService _dashboardService;
        public DashboardController(IDashboardService dashboardService) { _dashboardService = dashboardService; }

        [HttpGet("today")]
        public async Task<IActionResult> GetTodaySummary()
        {
            var result = await _dashboardService.GetTodaySummaryAsync(User.GetUserId());
            return Ok(result);
        }

        [HttpGet("weekly")]
        public async Task<IActionResult> GetWeeklyReport()
        {
            var result = await _dashboardService.GetWeeklyReportAsync(User.GetUserId());
            return Ok(result);
        }

        [HttpGet("team")]
        [Authorize(Roles = "TeamLead,Manager")]
        public async Task<IActionResult> GetTeamActivity()
        {
            var result = await _dashboardService.GetTeamActivityAsync();
            return Ok(result);
        }
    }
}
