using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using sabmero.DTOs.Auth;
using sabmero.Services;
using System.Security.Claims;
using sabmero.Helpers;

namespace sabmero.Controllers;

// ── ALL AUTH ENDPOINTS ────────────────────────────────────────────────────────
//
//  POST   /api/auth/register       → Register new user
//  POST   /api/auth/login          → Login with phone + password
//  POST   /api/auth/send-otp       → Send OTP to phone
//  POST   /api/auth/verify-otp     → Verify OTP, get token
//  GET    /api/auth/me             → Get my own profile (requires token)
//
// ─────────────────────────────────────────────────────────────────────────────

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth)
    {
        _auth = auth;
    }

    // ── POST /api/auth/register ───────────────────────────────────────────────
    // Register a new Customer, Vendor, Technician, or Rider.
    // Request Body (JSON):
    // {
    //   "fullName": "Ram Bahadur",
    //   "phone": "9812345678",
    //   "email": "ram@gmail.com",       ← optional
    //   "password": "MyPass123",
    //   "address": "Kathmandu, Nepal",
    //   "role": "Customer"              ← optional, defaults to "Customer"
    // }
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var (success, message, data) = await _auth.RegisterAsync(dto);

        if (!success)
            return BadRequest(new { success = false, message });

        return Ok(new { success = true, message, data });
    }

    // ── POST /api/auth/login ──────────────────────────────────────────────────
    // Login with phone number + password.
    // Request Body (JSON):
    // {
    //   "phone": "9812345678",
    //   "password": "MyPass123"
    // }
    // Response: JWT token + user profile
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var (success, message, data) = await _auth.LoginAsync(dto);

        if (!success)
            return Unauthorized(new { success = false, message });

        return Ok(new { success = true, message, data });
    }

    // ── POST /api/auth/send-otp ───────────────────────────────────────────────
    // Request a 6-digit OTP sent to the given phone number.
    // In development: the OTP is printed in server logs (check Railway logs).
    // In production: plug in your Bulk SMS API here.
    // Request Body (JSON):
    // {
    //   "phone": "9812345678"
    // }
    [HttpPost("send-otp")]
    public async Task<IActionResult> SendOtp([FromBody] SendOtpDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var (success, message) = await _auth.SendOtpAsync(dto);

        if (!success)
            return BadRequest(new { success = false, message });

        return Ok(new { success = true, message });
    }

    // ── POST /api/auth/verify-otp ─────────────────────────────────────────────
    // Verify the OTP code. If correct, returns a JWT token.
    // If phone is new (not yet registered), creates a Customer account automatically.
    // Request Body (JSON):
    // {
    //   "phone": "9812345678",
    //   "code": "847291"
    // }
    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var (success, message, data) = await _auth.VerifyOtpAsync(dto);

        if (!success)
            return BadRequest(new { success = false, message });

        return Ok(new { success = true, message, data });
    }

    // ── GET /api/auth/me ──────────────────────────────────────────────────────
    // Returns the currently logged-in user's profile.
    // Flutter must send: Authorization: Bearer <token>  in the request header.
    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        // Read user info from the JWT token claims
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var phone = User.FindFirst(ClaimTypes.MobilePhone)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        var fullName = User.FindFirst(ClaimTypes.Name)?.Value;

        return Ok(new
        {
            success = true,
            data = new
            {
                id = userId,
                fullName,
                phone,
                role
            }
        });
    }

    // ── POST /api/auth/forgot-password ────────────────────────────────────────
    // Body: { "email": "ram@gmail.com" } → emails a 6-digit reset code.
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var (success, message) = await _auth.ForgotPasswordAsync(dto);
        if (!success) return BadRequest(new { success = false, message });
        return Ok(new { success = true, message });
    }

    // ── POST /api/auth/reset-password ─────────────────────────────────────────
    // Body: { "email": "...", "code": "123456", "newPassword": "NewPass123" }
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var (success, message) = await _auth.ResetPasswordAsync(dto);
        if (!success) return BadRequest(new { success = false, message });
        return Ok(new { success = true, message });
    }

    // ── PUT /api/auth/profile ─────────────────────────────────────────────────
    // multipart/form-data: fullName (text), email (text), profilePicture (file)
    // All fields optional — only what's sent gets updated.
    [Authorize]
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile(
        [FromForm] string? fullName,
        [FromForm] string? email,
        [FromForm] IFormFile? profilePicture)
    {
        var userId = User.GetUserId();
        var (success, message, data) = await _auth.UpdateProfileAsync(userId, fullName, email, profilePicture);
        if (!success) return BadRequest(new { success = false, message });
        return Ok(new { success = true, message, data });
    }

    // ── POST /api/auth/fcm-token ──────────────────────────────────────────────
    // Body: { "token": "<device FCM token>" } — the app calls this after login
    // and whenever Firebase rotates the token.
    [Authorize]
    [HttpPost("fcm-token")]
    public async Task<IActionResult> SaveFcmToken([FromBody] FcmTokenDto dto)
    {
        var userId = User.GetUserId();
        var (success, message) = await _auth.SaveFcmTokenAsync(userId, dto.Token);
        if (!success) return BadRequest(new { success = false, message });
        return Ok(new { success = true, message });
    }

    // ── GET /api/auth/notification-settings ───────────────────────────────────
    // → { pushEnabled, promoAlertsEnabled } for the Settings screen toggles.
    [Authorize]
    [HttpGet("notification-settings")]
    public async Task<IActionResult> GetNotificationSettings()
    {
        var (success, message, data) = await _auth.GetNotificationSettingsAsync(User.GetUserId());
        if (!success) return BadRequest(new { success = false, message });
        return Ok(new { success = true, data });
    }

    // ── PUT /api/auth/notification-settings ───────────────────────────────────
    // Body: { "pushEnabled": true/false, "promoAlertsEnabled": true/false }
    // Send only what changed. The app calls this when a Settings toggle is flipped.
    [Authorize]
    [HttpPut("notification-settings")]
    public async Task<IActionResult> SaveNotificationSettings([FromBody] NotificationSettingsDto dto)
    {
        var (success, message, data) = await _auth.SaveNotificationSettingsAsync(User.GetUserId(), dto);
        if (!success) return BadRequest(new { success = false, message });
        return Ok(new { success = true, message, data });
    }
}