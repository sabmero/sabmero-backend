using System.ComponentModel.DataAnnotations;

namespace sabmero.DTOs.Auth;

// POST /api/auth/forgot-password
public class ForgotPasswordDto
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
}

// POST /api/auth/reset-password
public class ResetPasswordDto
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(6, MinimumLength = 6)]
    public string Code { get; set; } = string.Empty;

    [Required, MinLength(6)]
    public string NewPassword { get; set; } = string.Empty;
}

// POST /api/auth/fcm-token
public class FcmTokenDto
{
    [Required]
    public string Token { get; set; } = string.Empty;
}

// PUT /api/auth/notification-settings   (Settings screen toggles)
// Send only the toggle(s) that changed; null = leave as is.
public class NotificationSettingsDto
{
    // "Push Notifications" — order and repair updates
    public bool? PushEnabled { get; set; }

    // "Promotional Alerts" — deals and offers
    public bool? PromoAlertsEnabled { get; set; }
}