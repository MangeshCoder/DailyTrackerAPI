using DailyTrackerAPI.Data;
using DailyTrackerAPI.DTOs;
using DailyTrackerAPI.Helpers;
using DailyTrackerAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DailyTrackerAPI.Controllers
{
    // ─── Leave Controller ─────────────────────────────────────────────────────
    [ApiController, Route("api/leave"), Authorize]
    public class LeaveController : ControllerBase
    {
        private readonly ILeaveService _leaveSvc;
        private readonly AppDbContext _context;
        public LeaveController(ILeaveService leaveSvc, AppDbContext context)
        {
            _leaveSvc = leaveSvc;
            _context = context;
        }

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

        [AllowAnonymous]
        [HttpPost("email-review")]
        public async Task<IActionResult> EmailReview([FromBody] EmailReviewDto dto)
        {
            var action = await _context.LeaveEmailActions
                .FirstOrDefaultAsync(x => x.Token == dto.Token);

            if (action == null)
                return BadRequest("Invalid token");

            if (action.IsUsed)
                return BadRequest("This link has already been used");

            if (action.ExpiryDate < DateTime.UtcNow)
                return BadRequest("This link has expired");

            // 🔥 USE EXISTING BUSINESS LOGIC
            await _leaveSvc.ReviewAsync(
                action.LeaveId,
                action.ManagerId,
                new ReviewLeaveDto
                {
                    Status = dto.Status,
                    ReviewNote = "Reviewed via email"
                });

            action.IsUsed = true;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Leave updated successfully" });
        }

        [HttpGet("balance")]
        public async Task<IActionResult> GetBalance()
        {
            var userId = User.GetUserId();
            var role = User.FindFirst("role")?.Value;

            if (role == "Manager")
                return Ok(await _leaveSvc.GetMonthlyBalanceAsync());

            return Ok(await _leaveSvc.GetMonthlyBalanceAsync(userId));
        }
    }
}
