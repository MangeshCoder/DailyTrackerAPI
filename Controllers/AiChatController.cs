using DailyTrackerAPI.Models;
using DailyTrackerAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DailyTrackerAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AiChatController : ControllerBase
    {
        private readonly IAiService _aiService;
        private readonly ILogger<AiChatController> _logger;

        public AiChatController(IAiService aiService, ILogger<AiChatController> logger)
        {
            _aiService = aiService;
            _logger = logger;
        }

        // POST api/aichat/send
        //
        // ADDED [Authorize] — prevents anonymous users from consuming your
        // Gemini API quota. Your cookie-based JWT handles this automatically.
        //
        // ADDED userId extraction — reads the NameIdentifier claim set by
        // JwtHelper.GenerateAccessToken() when it writes:
        //   new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
        // This is the same pattern used in AuthController.Me() and other
        // authenticated endpoints throughout your application.
        [Authorize]
        [HttpPost("send")]
        public async Task<ActionResult<ChatResponse>> Send([FromBody] ChatRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Message))
                return BadRequest(new ChatResponse
                {
                    Success = false,
                    Error = "Message cannot be empty."
                });

            // Extract the authenticated user's integer Id from the JWT claim
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
                return Unauthorized(new ChatResponse
                {
                    Success = false,
                    Error = "Invalid or missing user token."
                });

            try
            {
                // Pass userId so GeminiService can query this user's real data
                // (DailyLogs, TaskLogs, SupportLogs, LeaveRequests, etc.)
                var reply = await _aiService.GetChatResponseAsync(
                    request.Message,
                    request.History,
                    userId);

                return Ok(new ChatResponse { Reply = reply, Success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Gemini API for userId={UserId}", userId);
                return StatusCode(500, new ChatResponse
                {
                    Success = false,
                    Error = "AI service is unavailable. Please try again."
                });
            }
        }
    }
}