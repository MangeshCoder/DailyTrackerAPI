using DailyTrackerAPI.Data;
using DailyTrackerAPI.DTOs;
using DailyTrackerAPI.Helpers;
using DailyTrackerAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DailyTrackerAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly IRefreshTokenService _authSvc;
        private readonly IEmailOtpService _emailOtp;
        private readonly IEmailService _emailService;
        private readonly AppDbContext _db;

        public AuthController(IAuthService authService, IRefreshTokenService authSvc, IEmailOtpService emailOtp, IEmailService emailService, AppDbContext db)
        {
            _authService = authService;
            _authSvc = authSvc;
            _emailOtp = emailOtp;
            _emailService = emailService;
            _db = db;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            try
            {
                var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                var result = await _authSvc.RegisterAsync(dto, ip);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        //[HttpPost("login")]
        //public async Task<IActionResult> Login([FromBody] LoginDto dto)
        //{
        //    var result = await _authService.LoginAsync(dto);
        //    if (result == null)
        //        return Unauthorized(new { message = "Invalid email or password." });

        //    return Ok(result);
        //}

        [Authorize]
        [HttpGet("users")]
        public async Task<IActionResult> GetAllUsers()
        {
            var users = await _authService.GetAllUsersAsync();
            return Ok(users);
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto, [FromHeader(Name = "X-Device-Token")] string? deviceToken = null)
        {
            try
            {
                var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                var result = await _authSvc.LoginAsync(dto.Email, dto.Password, ip, deviceToken);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = ex.Message }); }
        }

        /// <summary>Complete login when 2FA is required. Send the TempToken from login + the 6-digit code.</summary>
        [HttpPost("verify-2fa-login")]
        public async Task<IActionResult> Verify2FALogin([FromBody] Verify2FALoginDto dto)
        {
            try
            {
                var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                var result = await _authSvc.Verify2FAAndLoginAsync(dto.TempToken, dto.Code, ip);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = ex.Message }); }
        }

        /// <summary>Get QR code to set up 2FA. Requires Bearer token. Call verify-2fa-setup to enable.</summary>
        [Authorize]
        [HttpPost("setup-2fa")]
        public async Task<IActionResult> Setup2FA()
        {
            try
            {
                var userId = User.GetUserId();
                if (userId <= 0) return Unauthorized();

                var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "";
                var result = await _authSvc.SetupTotpAsync(userId, email);
                return Ok(result);
            }
            catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
        }

        /// <summary>Verify the 6-digit code from authenticator app to enable 2FA.</summary>
        [Authorize]
        [HttpPost("verify-2fa-setup")]
        public async Task<IActionResult> Verify2FASetup([FromBody] Verify2FASetupDto dto)
        {
            try
            {
                var userId = User.GetUserId();
                if (userId <= 0) return Unauthorized();

                await _authSvc.EnableTotpAsync(userId, dto.Code);
                return Ok(new { message = "Two-factor authentication has been enabled." });
            }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = ex.Message }); }
            catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
        }

        /// <summary>Disable 2FA. Requires current 6-digit code to verify identity.</summary>
        [Authorize]
        [HttpPost("disable-2fa")]
        public async Task<IActionResult> Disable2FA([FromBody] Disable2FADto dto)
        {
            try
            {
                var userId = User.GetUserId();
                if (userId <= 0) return Unauthorized();

                await _authSvc.DisableTotpAsync(userId, dto.Code);
                return Ok(new { message = "Two-factor authentication has been disabled." });
            }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = ex.Message }); }
            catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
        }

        /// <summary>Check if current user has 2FA enabled.</summary>
        [Authorize]
        [HttpGet("2fa-status")]
        public async Task<IActionResult> Get2FAStatus()
        {
            var userId = User.GetUserId();
            if (userId <= 0) return Unauthorized();
            var enabled = await _authSvc.IsTwoFactorEnabledAsync(userId);
            return Ok(new { twoFactorEnabled = enabled });
        }

        /// <summary>Trust this device to skip 2FA for 30 days. Requires Bearer token. Pass deviceToken in body.</summary>
        [Authorize]
        [HttpPost("trust-device")]
        public async Task<IActionResult> TrustDevice([FromBody] TrustDeviceRequest dto)
        {
            try
            {
                var userId = User.GetUserId();
                if (userId <= 0) return Unauthorized();

                var deviceToken = !string.IsNullOrEmpty(dto.DeviceToken) ? dto.DeviceToken : Guid.NewGuid().ToString();
                var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                await _authSvc.TrustDeviceAsync(userId, deviceToken, dto.DeviceName ?? "Unknown", ip);
                return Ok(new { message = "Device trusted for 30 days. 2FA will be skipped on this device." });
            }
            catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequestDto dto)
        {
            try
            {
                var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                var result = await _authSvc.RefreshAsync(dto.RefreshToken, ip);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = ex.Message }); }
        }

        [HttpPost("revoke"), Authorize]
        public async Task<IActionResult> Revoke([FromBody] RefreshTokenRequestDto dto)
        {
            await _authSvc.RevokeAsync(dto.RefreshToken);
            return Ok(new { message = "Token revoked." });
        }

        [HttpPost("logout"), Authorize]
        public async Task<IActionResult> Logout()
        {
            await _authSvc.RevokeAllForUserAsync(User.GetUserId());
            return Ok(new { message = "Logged out from all sessions." });
        }

        [HttpPost("send-register-otp")]
        public async Task<IActionResult> SendRegisterOtp([FromBody] EmailRequestDto dto)
        {
            await _emailOtp.SendOtpAsync(dto.Email, "Register");
            return Ok(new { message = "OTP sent to email." });
        }

        [HttpPost("verify-register-otp")]
        public async Task<IActionResult> VerifyRegisterOtp([FromBody] RegisterWithOtpDto dto)
        {
            var valid = await _emailOtp.VerifyOtpAsync(dto.Email, dto.Code, "Register");
            if (!valid)
                return BadRequest(new { message = "Invalid or expired OTP." });

            // ✅ Convert to RegisterDto
            var registerDto = new RegisterDto
            {
                FullName = dto.FullName,
                Email = dto.Email,
                Password = dto.Password,
                Role = dto.Role
            };

            var result = await _authSvc.RegisterAsync(registerDto,
                HttpContext.Connection.RemoteIpAddress?.ToString());

            await _emailService.SendWelcomeEmailAsync(dto.Email, dto.FullName);

            return Ok(result);
        }

        [HttpPost("verify-login-otp")]
        public async Task<IActionResult> VerifyLoginOtp([FromBody] VerifyLoginOtpDto dto)
        {
            try
            {
                var valid = await _emailOtp.VerifyOtpAsync(dto.Email, dto.Code, "Login");
                if (!valid)
                    return BadRequest(new { message = "Invalid or expired OTP." });

                var result = await _authSvc.LoginWithEmailAsync(
                    dto.Email,
                    HttpContext.Connection.RemoteIpAddress?.ToString()
                );

                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
        }

        [HttpPost("send-forgot-password-otp")]
        public async Task<IActionResult> SendForgotPasswordOtp([FromBody] EmailRequestDto dto)
        {
            var userExists = await _db.Users.AnyAsync(u => u.Email == dto.Email);
            if (!userExists)
                return BadRequest(new { message = "User not found." });

            await _emailOtp.SendOtpAsync(dto.Email, "ForgotPassword");
            return Ok(new { message = "OTP sent to email." });
        }

        [HttpPost("send-login-otp")]
        public async Task<IActionResult> SendLoginOtp([FromBody] EmailRequestDto dto)
        {
            var userExists = await _db.Users.AnyAsync(u => u.Email == dto.Email);
            if (!userExists)
                return BadRequest(new { message = "User not found." });

            await _emailOtp.SendOtpAsync(dto.Email, "Login");
            return Ok(new { message = "OTP sent to email." });
        }


        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword(ResetPasswordDto dto)
        {
            var valid = await _emailOtp.VerifyOtpAsync(dto.Email, dto.Code, "ForgotPassword");
            if (!valid) return BadRequest(new { message = "Invalid OTP" });

            var user = await _db.Users.FirstAsync(u => u.Email == dto.Email);
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Password reset successful." });
        }
    }
}