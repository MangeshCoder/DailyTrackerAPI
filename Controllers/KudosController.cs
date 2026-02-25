using DailyTrackerAPI.DTOs;
using DailyTrackerAPI.Helpers;
using DailyTrackerAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DailyTrackerAPI.Controllers
{
    // ─── Kudos Controller ─────────────────────────────────────────────────────
    [ApiController, Route("api/kudos"), Authorize]
    public class KudosController : ControllerBase
    {
        private readonly IKudosService _kudosSvc;
        public KudosController(IKudosService kudosSvc) => _kudosSvc = kudosSvc;

        [HttpPost]
        public async Task<IActionResult> Give([FromBody] GiveKudosDto dto)
        {
            try
            {
                var kudos = await _kudosSvc.GiveKudosAsync(User.GetUserId(), dto);
                return Ok(kudos);
            }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }

        [HttpGet("recent")]
        public async Task<IActionResult> GetRecent([FromQuery] int limit = 20)
        {
            var list = await _kudosSvc.GetRecentKudosAsync(limit);
            return Ok(list);
        }

        [HttpGet("my")]
        public async Task<IActionResult> GetMySummary()
        {
            var summary = await _kudosSvc.GetUserKudosSummaryAsync(User.GetUserId());
            return Ok(summary);
        }

        [HttpGet("user/{userId}"), Authorize(Roles = "Manager")]
        public async Task<IActionResult> GetUserSummary(int userId)
        {
            var summary = await _kudosSvc.GetUserKudosSummaryAsync(userId);
            return Ok(summary);
        }
    }
}
