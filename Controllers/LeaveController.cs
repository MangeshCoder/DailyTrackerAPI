using DailyTrackerAPI.DTOs;
using DailyTrackerAPI.Helpers;
using DailyTrackerAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DailyTrackerAPI.Controllers
{
    // ─── Leave Controller ─────────────────────────────────────────────────────
    [ApiController, Route("api/leave"), Authorize]
    public class LeaveController : ControllerBase
    {
        private readonly ILeaveService _leaveSvc;
        public LeaveController(ILeaveService leaveSvc) => _leaveSvc = leaveSvc;

        [HttpPost]
        public async Task<IActionResult> Apply([FromBody] ApplyLeaveDto dto)
        {
            var leave = await _leaveSvc.ApplyAsync(User.GetUserId(), dto);
            return Ok(leave);
        }

        [HttpGet("my")]
        public async Task<IActionResult> GetMine()
        {
            var leaves = await _leaveSvc.GetMyLeavesAsync(User.GetUserId());
            return Ok(leaves);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Cancel(int id)
        {
            try
            {
                await _leaveSvc.CancelAsync(id, User.GetUserId());
                return Ok(new { message = "Leave cancelled." });
            }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }

        [HttpGet("all"), Authorize(Roles = "Manager")]
        public async Task<IActionResult> GetAll([FromQuery] string? status)
        {
            var leaves = await _leaveSvc.GetAllLeavesAsync(status);
            return Ok(leaves);
        }

        [HttpPut("{id}/review"), Authorize(Roles = "Manager")]
        public async Task<IActionResult> Review(int id, [FromBody] ReviewLeaveDto dto)
        {
            await _leaveSvc.ReviewAsync(id, User.GetUserId(), dto);
            return Ok(new { message = "Leave reviewed." });
        }
    }
}
