using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DailyTrackerAPI.DTOs;
using DailyTrackerAPI.Helpers;
using DailyTrackerAPI.Services;

namespace DailyTrackerAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DailyLogController : ControllerBase
    {
        private readonly IDailyLogService _logService;

        public DailyLogController(IDailyLogService logService)
        {
            _logService = logService;
        }

        [HttpPost("checkin")]
        public async Task<IActionResult> CheckIn([FromBody] CheckInDto dto)
        {
            var userId = User.GetUserId();
            var result = await _logService.CheckInAsync(userId, dto);
            if (result == null)
                return BadRequest(new { message = "Already checked in today." });

            return Ok(result);
        }

        [HttpPut("checkout")]
        public async Task<IActionResult> CheckOut([FromBody] CheckOutDto dto)
        {
            var userId = User.GetUserId();
            var result = await _logService.CheckOutAsync(userId, dto);
            if (result == null)
                return BadRequest(new { message = "No check-in found for today." });

            return Ok(result);
        }

        [HttpGet("today")]
        public async Task<IActionResult> GetToday()
        {
            var userId = User.GetUserId();
            var result = await _logService.GetTodayLogAsync(userId);
            return result == null ? NotFound() : Ok(result);
        }

        [HttpGet("date/{date}")]
        public async Task<IActionResult> GetByDate(DateTime date)
        {
            var userId = User.GetUserId();
            var result = await _logService.GetLogByDateAsync(userId, date);
            return result == null ? NotFound() : Ok(result);
        }

        [HttpGet("history")]
        public async Task<IActionResult> GetHistory([FromQuery] int days = 30)
        {
            var userId = User.GetUserId();
            var result = await _logService.GetHistoryAsync(userId, days);
            return Ok(result);
        }
    }
}