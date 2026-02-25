using DailyTrackerAPI.DTOs;
using DailyTrackerAPI.Helpers;
using DailyTrackerAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DailyTrackerAPI.Controllers
{
    // ─── Goals Controller ─────────────────────────────────────────────────────
    [ApiController, Route("api/goals"), Authorize]
    public class GoalsController : ControllerBase
    {
        private readonly IGoalService _goalSvc;
        public GoalsController(IGoalService goalSvc) => _goalSvc = goalSvc;

        [HttpPost]
        public async Task<IActionResult> SetGoal([FromBody] SetGoalDto dto)
        {
            var goal = await _goalSvc.SetOrUpdateGoalAsync(User.GetUserId(), dto);
            return Ok(goal);
        }

        [HttpGet("today")]
        public async Task<IActionResult> GetProgress()
        {
            var progress = await _goalSvc.GetTodayProgressAsync(User.GetUserId());
            return Ok(progress);
        }

        [HttpGet("trend")]
        public async Task<IActionResult> GetTrend([FromQuery] int days = 14)
        {
            var trend = await _goalSvc.GetProductivityTrendAsync(User.GetUserId(), days);
            return Ok(trend);
        }
    }
}
