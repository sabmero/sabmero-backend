using Microsoft.EntityFrameworkCore;
using sabmero.Data;
using sabmero.DTOs.Auth;
using sabmero.Models;
using BCrypt.Net;

namespace sabmero.Services;


public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly IJwtService _jwt;
    private readonly ILogger<AuthService> _logger;
    private readonly IEmailService _email;
    private readonly IFileService _files;

    public AuthService(AppDbContext db, IJwtService jwt, ILogger<AuthService> logger,
                       IEmailService email, IFileService files)
    {
        _db = db;
        _jwt = jwt;
        _logger = logger;
        _email = email;
        _files = files;
    }

    public async Task<(bool Success, string Message, AuthResponseDto? Data)> RegisterAsync(RegisterDto dto)
    {
        // Check if phone is already taken
        bool phoneExists = await _db.Users.AnyAsync(u => u.Phone == dto.Phone);
        if (phoneExists)
            return (false, "This phone number is already registered.", null);

        // Email is required and is used for password reset, so it must be unique
        var normalizedEmail = dto.Email.Trim().ToLower();
        bool emailExists = await _db.Users.AnyAsync(u => u.Email != null && u.Email.ToLower() == normalizedEmail);
        if (emailExists)
            return (false, "This email is already registered.", null);

        // Create new User row
        var user = new User
        {
            FullName = dto.FullName.Trim(),
            Phone = dto.Phone.Trim(),
            Email = dto.Email.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),   // never store plain password
            Address = dto.Address.Trim(),
            Role = "Customer",   // server-assigned; see note above
            IsKycVerified = false,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        _logger.LogInformation("New user registered: {Phone} as {Role}", user.Phone, user.Role);

        // Generate JWT token and return
        var token = _jwt.GenerateToken(user);
        var response = BuildAuthResponse(token, user);
        return (true, "Registration successful.", response);
    }

    // ── LOGIN (Phone + Password) ──────────────────────────────────────────────
    // Flutter sends: Phone, Password
    // Returns: JWT token + user profile
    public async Task<(bool Success, string Message, AuthResponseDto? Data)> LoginAsync(LoginDto dto)
    {
        // Find user by phone
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Phone == dto.Phone);

        if (user == null)
            return (false, "Phone number not found. Please register first.", null);

        if (!user.IsActive)
            return (false, "Your account has been deactivated. Contact support.", null);

        // Check password against stored hash
        bool passwordCorrect = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);
        if (!passwordCorrect)
            return (false, "Incorrect password.", null);

        _logger.LogInformation("User logged in: {Phone}", user.Phone);

        var token = _jwt.GenerateToken(user);
        var response = BuildAuthResponse(token, user);
        return (true, "Login successful.", response);
    }


    public async Task<(bool Success, string Message)> SendOtpAsync(SendOtpDto dto)
    {
        // Mark any old unused OTPs for this phone as used (prevent reuse)
        var oldOtps = await _db.OtpCodes
            .Where(o => o.Phone == dto.Phone && !o.IsUsed)
            .ToListAsync();

        foreach (var old in oldOtps)
            old.IsUsed = true;

        // Generate a new 6-digit code
        var random = new Random();
        var code = random.Next(100000, 999999).ToString();

        var otpRecord = new OtpCode
        {
            Phone = dto.Phone,
            Code = code,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),   // valid for 5 minutes
            IsUsed = false,
            CreatedAt = DateTime.UtcNow
        };

        _db.OtpCodes.Add(otpRecord);
        await _db.SaveChangesAsync();

        // ── In PRODUCTION replace this log line with your SMS API call ──────
        // Example: await _smsService.SendAsync(dto.Phone, $"Your sabmero OTP is: {code}");
        _logger.LogInformation("OTP for {Phone}: {Code} (expires in 5 min)", dto.Phone, code);
        // ─────────────────────────────────────────────────────────────────────

        return (true, $"OTP sent to {dto.Phone}. Valid for 5 minutes.");
    }


    public async Task<(bool Success, string Message, AuthResponseDto? Data)> VerifyOtpAsync(VerifyOtpDto dto)
    {
        // Find the most recent unused OTP for this phone
        var otpRecord = await _db.OtpCodes
            .Where(o => o.Phone == dto.Phone && !o.IsUsed)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        if (otpRecord == null)
            return (false, "No OTP found for this phone. Please request a new OTP.", null);

        if (otpRecord.ExpiresAt < DateTime.UtcNow)
            return (false, "OTP has expired. Please request a new one.", null);

        if (otpRecord.Code != dto.Code)
            return (false, "Incorrect OTP code.", null);

        // Mark OTP as used so it can't be reused
        otpRecord.IsUsed = true;
        await _db.SaveChangesAsync();

        // Find or create the user for this phone number
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Phone == dto.Phone);

        if (user == null)
        {
            // New user — create a basic Customer account (they can fill details later)
            user = new User
            {
                FullName = "sabmero User",
                Phone = dto.Phone,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()),  // random password
                Role = "Customer",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();
        }

        if (!user.IsActive)
            return (false, "Your account has been deactivated. Contact support.", null);

        _logger.LogInformation("OTP verified for {Phone}", dto.Phone);

        var token = _jwt.GenerateToken(user);
        var response = BuildAuthResponse(token, user);
        return (true, "OTP verified. Login successful.", response);
    }

    private static AuthResponseDto BuildAuthResponse(string token, User user)
    {
        return new AuthResponseDto
        {
            Token = token,
            User = new UserProfileDto
            {
                Id = user.Id,
                FullName = user.FullName,
                Phone = user.Phone,
                Email = user.Email,
                Address = user.Address,
                ProfilePictureUrl = user.ProfilePicturePath,
                Role = user.Role,
                IsKycVerified = user.IsKycVerified,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt
            }
        };
    }


    public async Task<(bool Success, string Message)> ForgotPasswordAsync(ForgotPasswordDto dto)
    {
        var email = dto.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == email);

        // Don't reveal whether the email exists (prevents account enumeration).
        if (user == null)
            return (true, "If that email is registered, a reset code has been sent.");

        // Invalidate any previous reset codes for this email.
        var oldOtps = await _db.OtpCodes
            .Where(o => o.Phone == email && o.Purpose == "PasswordReset" && !o.IsUsed)
            .ToListAsync();
        foreach (var old in oldOtps) old.IsUsed = true;

        var code = Random.Shared.Next(100000, 999999).ToString();
        _db.OtpCodes.Add(new OtpCode
        {
            Phone = email,                 // for reset codes, this column holds the EMAIL
            Code = code,
            Purpose = "PasswordReset",
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            IsUsed = false,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await _email.SendOtpAsync(user.Email!, code);
        _logger.LogInformation("Password reset code sent to {Email}", email);

        return (true, "If that email is registered, a reset code has been sent.");
    }


    public async Task<(bool Success, string Message)> ResetPasswordAsync(ResetPasswordDto dto)
    {
        var email = dto.Email.Trim().ToLowerInvariant();

        var otp = await _db.OtpCodes
            .Where(o => o.Phone == email && o.Purpose == "PasswordReset" && !o.IsUsed)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        if (otp == null)
            return (false, "No reset code found. Please request a new one.");
        if (otp.ExpiresAt < DateTime.UtcNow)
            return (false, "The reset code has expired. Please request a new one.");
        if (otp.Code != dto.Code)
            return (false, "Incorrect reset code.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == email);
        if (user == null)
            return (false, "Account not found.");

        otp.IsUsed = true;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Password reset for {Email}", email);
        return (true, "Password reset successful. You can now log in with your new password.");
    }

    // ── UPDATE PROFILE (name / email / picture) ──────────────────────────────
    public async Task<(bool Success, string Message, UserProfileDto? Data)> UpdateProfileAsync(
        int userId, string? fullName, string? email, Microsoft.AspNetCore.Http.IFormFile? profilePicture)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null)
            return (false, "User not found.", null);

        if (!string.IsNullOrWhiteSpace(fullName))
            user.FullName = fullName.Trim();

        if (!string.IsNullOrWhiteSpace(email))
        {
            var normalized = email.Trim();
            var taken = await _db.Users.AnyAsync(u =>
                u.Id != userId && u.Email != null && u.Email.ToLower() == normalized.ToLower());
            if (taken)
                return (false, "That email is already used by another account.", null);
            user.Email = normalized;
        }

        if (profilePicture != null)
        {
            var (ok, msg, path) = await _files.SaveAsync(profilePicture, "profile");
            if (!ok) return (false, msg, null);
            user.ProfilePicturePath = path;
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Profile updated for user {UserId}", userId);

        return (true, "Profile updated.", new UserProfileDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Phone = user.Phone,
            Email = user.Email,
            Address = user.Address,
            ProfilePictureUrl = user.ProfilePicturePath,
            Role = user.Role,
            IsKycVerified = user.IsKycVerified,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt
        });
    }

    // ── SAVE FCM TOKEN ────────────────────────────────────────────────────────
    public async Task<(bool Success, string Message)> SaveFcmTokenAsync(int userId, string token)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null)
            return (false, "User not found.");

        user.FcmToken = token;
        await _db.SaveChangesAsync();
        return (true, "Device token saved.");
    }
}