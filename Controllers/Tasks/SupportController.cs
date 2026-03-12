using DailyTrackerAPI.Data;
using DailyTrackerAPI.DTOs;
using DailyTrackerAPI.Helpers;
using DailyTrackerAPI.Services.Tasks;
using DailyTrackerAPI.Services.Team;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DailyTrackerAPI.Controllers.Tasks
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class SupportController : ControllerBase
    {
        private readonly ISupportService _supportService;
        private readonly IMediaStorageService _mediaStorage;
        private readonly AppDbContext _db;

        public SupportController(ISupportService supportService, IMediaStorageService mediaStorage, AppDbContext db)
        {
            _supportService = supportService;
            _mediaStorage = mediaStorage;
            _db = db;
        }

        /// <summary>Create support log (JSON body, no media).</summary>
        [HttpPost]
        public async Task<IActionResult> CreateSupport([FromBody] CreateSupportDto dto)
        {
            var result = await _supportService.CreateSupportAsync(User.GetUserId(), dto);
            if (result == null) return BadRequest(new { message = "Check in first or developer not found." });
            return Ok(result);
        }

        /// <summary>Create support log with screenshots/recordings as proof. Use multipart/form-data.</summary>
        [HttpPost("with-media")]
        public async Task<IActionResult> CreateSupportWithMedia([FromForm] int supportedDeveloperId, [FromForm] string issueDescription,
            [FromForm] string? resolution, [FromForm] int timeSpentMinutes, [FromForm] string supportType, [FromForm] IFormFileCollection? files)
        {
            var dto = new CreateSupportDto
            {
                SupportedDeveloperId = supportedDeveloperId,
                IssueDescription = issueDescription,
                Resolution = resolution,
                TimeSpentMinutes = timeSpentMinutes,
                SupportType = supportType ?? "Technical"
            };

            IFormFileCollection? fileCollection = null;
            if (Request.Form.Files != null && Request.Form.Files.Count > 0)
                fileCollection = Request.Form.Files;

            var result = await _supportService.CreateSupportWithMediaAsync(User.GetUserId(), dto, fileCollection);
            if (result == null) return BadRequest(new { message = "Check in first or developer not found." });
            return Ok(result);
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

        /// <summary>
        /// Secured media stream. User sees own media only; Manager sees all.
        /// Requires Bearer token.
        /// </summary>
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

            var supportLogOwnerId = evidence.SupportLog!.DailyLog!.UserId;
            if (!isManager && userId != supportLogOwnerId)
                return Forbid();

            var fileInfo = await _mediaStorage.GetMediaFileInfoAsync(mediaId);
            if (fileInfo == null)
                return NotFound();

            var (fullPath, mimeType, fileName) = fileInfo.Value;
            return PhysicalFile(fullPath, mimeType, fileName, enableRangeProcessing: true);
        }
    }
}
