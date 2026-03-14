// ─────────────────────────────────────────────────────────────────────────────
//  FILE 6:  backend/Controllers/SupportController.cs
//  ACTION:  REPLACE entire file
//
//  Changes from original:
//  1. Both endpoints now accept latitude + longitude from the form
//  2. Both catch LocationException → return 403 with the exact error message
//     (403 Forbidden is the correct HTTP status — user is authenticated but
//      not permitted to submit from their current location)
//  3. The [FromForm] parameter list in CreateSupportWithMedia now includes
//     latitude and longitude
// ─────────────────────────────────────────────────────────────────────────────

using DailyTrackerAPI.Data;
using DailyTrackerAPI.DTOs;
using DailyTrackerAPI.Helpers;
using DailyTrackerAPI.Services.Tasks;
using DailyTrackerAPI.Services.Team;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DailyTrackerAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class SupportController : ControllerBase
    {
        private readonly ISupportService _supportService;
        private readonly IMediaStorageService _mediaStorage;
        private readonly AppDbContext _db;

        public SupportController(
            ISupportService supportService,
            IMediaStorageService mediaStorage,
            AppDbContext db)
        {
            _supportService = supportService;
            _mediaStorage = mediaStorage;
            _db = db;
        }

        /// <summary>
        /// Create support log (JSON body, no media).
        /// Latitude + Longitude are required — backend validates location.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateSupport([FromBody] CreateSupportDto dto)
        {
            try
            {
                var result = await _supportService.CreateSupportAsync(User.GetUserId(), dto);
                if (result == null)
                    return BadRequest(new { message = "Check in first or developer not found." });
                return Ok(result);
            }
            catch (LocationException ex)
            {
                // 403 = authenticated but not permitted from this location
                return StatusCode(403, new { message = ex.Message });
            }
        }

        /// <summary>
        /// Create support log WITH media evidence (multipart/form-data).
        /// latitude + longitude fields are required in the form.
        /// </summary>
        [HttpPost("with-media")]
        public async Task<IActionResult> CreateSupportWithMedia(
            [FromForm] int supportedDeveloperId,
            [FromForm] string issueDescription,
            [FromForm] string? resolution,
            [FromForm] int timeSpentMinutes,
            [FromForm] string supportType,
            [FromForm] double latitude,      // ← NEW
            [FromForm] double longitude,     // ← NEW
            [FromForm] IFormFileCollection? files)
        {
            var dto = new CreateSupportDto
            {
                SupportedDeveloperId = supportedDeveloperId,
                IssueDescription = issueDescription,
                Resolution = resolution,
                TimeSpentMinutes = timeSpentMinutes,
                SupportType = supportType ?? "Technical",
                Latitude = latitude,
                Longitude = longitude
            };

            IFormFileCollection? fileCollection = null;
            if (Request.Form.Files is { Count: > 0 })
                fileCollection = Request.Form.Files;

            try
            {
                var result = await _supportService.CreateSupportWithMediaAsync(
                    User.GetUserId(), dto, fileCollection);

                if (result == null)
                    return BadRequest(new { message = "Check in first or developer not found." });

                return Ok(result);
            }
            catch (LocationException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
        }

        [HttpDelete("{supportId}")]
        public async Task<IActionResult> DeleteSupport(int supportId)
        {
            var success = await _supportService.DeleteSupportAsync(User.GetUserId(), supportId);
            return success ? NoContent() : NotFound();
        }

        [HttpGet("today")]
        public async Task<IActionResult> GetTodaySupport()
        {
            var result = await _supportService.GetTodaySupportAsync(User.GetUserId());
            return Ok(result);
        }

        /// <summary>Secured media stream. Own media only; Manager sees all.</summary>
        [HttpGet("media/{mediaId:int}")]
        public async Task<IActionResult> GetMedia(int mediaId)
        {
            var userId = User.GetUserId();
            var isManager = User.IsInRole("Manager");

            var evidence = await _db.MediaEvidences
                .Include(m => m.SupportLog)
                .ThenInclude(s => s!.DailyLog)
                .FirstOrDefaultAsync(m => m.Id == mediaId);

            if (evidence?.SupportLog?.DailyLog == null)
                return NotFound();

            var ownerId = evidence.SupportLog!.DailyLog!.UserId;
            if (!isManager && userId != ownerId)
                return Forbid();

            var fileInfo = await _mediaStorage.GetMediaFileInfoAsync(mediaId);
            if (fileInfo == null) return NotFound();

            var (fullPath, mimeType, fileName) = fileInfo.Value;
            return PhysicalFile(fullPath, mimeType, fileName, enableRangeProcessing: true);
        }
    }
}