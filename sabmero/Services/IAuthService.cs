using sabmero.DTOs.Auth;

namespace sabmero.Services;

// Contract for all authentication operations.
// AuthController calls these methods — it doesn't care HOW they work, just that they exist.
public interface IAuthService
{
    Task<(bool Success, string Message, AuthResponseDto? Data)> RegisterAsync(RegisterDto dto);
    Task<(bool Success, string Message, AuthResponseDto? Data)> LoginAsync(LoginDto dto);
    Task<(bool Success, string Message)> SendOtpAsync(SendOtpDto dto);
    Task<(bool Success, string Message, AuthResponseDto? Data)> VerifyOtpAsync(VerifyOtpDto dto);
    Task<(bool Success, string Message)> ForgotPasswordAsync(ForgotPasswordDto dto);
    Task<(bool Success, string Message)> ResetPasswordAsync(ResetPasswordDto dto);
    Task<(bool Success, string Message, UserProfileDto? Data)> UpdateProfileAsync(int userId, string? fullName, string? email, Microsoft.AspNetCore.Http.IFormFile? profilePicture);
    Task<(bool Success, string Message)> SaveFcmTokenAsync(int userId, string token);
    Task<(bool Success, string Message, NotificationSettingsDto? Data)> GetNotificationSettingsAsync(int userId);
    Task<(bool Success, string Message, NotificationSettingsDto? Data)> SaveNotificationSettingsAsync(int userId, NotificationSettingsDto dto);
}