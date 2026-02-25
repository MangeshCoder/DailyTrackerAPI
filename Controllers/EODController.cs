using DailyTrackerAPI.DTOs;
using DailyTrackerAPI.Helpers;
using DailyTrackerAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DailyTrackerAPI.Controllers
{
    // ─── EOD Report Controller ────────────────────────────────────────────────
    [ApiController, Route("api/eod"), Authorize]
    public class EODController : ControllerBase
    {
        private readonly IEODService _eodSvc;
        public EODController(IEODService eodSvc) => _eodSvc = eodSvc;

        [HttpPost]
        public async Task<IActionResult> Submit([FromBody] CreateEODReportDto dto)
        {
            try
            {
                var report = await _eodSvc.SubmitReportAsync(User.GetUserId(), dto);
                return Ok(report);
            }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }

        [HttpGet("today")]
        public async Task<IActionResult> GetToday()
        {
            var report = await _eodSvc.GetTodayReportAsync(User.GetUserId());
            return Ok(report);
        }

        [HttpGet("history")]
        public async Task<IActionResult> GetHistory([FromQuery] int days = 14)
        {
            var reports = await _eodSvc.GetUserReportsAsync(User.GetUserId(), days);
            return Ok(reports);
        }

        [HttpGet("pending"), Authorize(Roles = "Manager")]
        public async Task<IActionResult> GetPending()
        {
            var reports = await _eodSvc.GetAllPendingReviewsAsync();
            return Ok(reports);
        }

        [HttpPut("{id}/review"), Authorize(Roles = "Manager")]
        public async Task<IActionResult> Review(int id, [FromBody] ManagerReviewEODDto dto)
        {
            await _eodSvc.ReviewReportAsync(id, User.GetUserId(), dto);
            return Ok(new { message = "Review submitted." });
        }
    }
}
