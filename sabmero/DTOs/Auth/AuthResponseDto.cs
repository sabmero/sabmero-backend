namespace sabmero.DTOs.Auth;

// This is what the API sends BACK to the Flutter app after successful login or register.
// Flutter stores the Token and sends it as "Authorization: Bearer <token>" in future requests.
public class AuthResponseDto
{
    public string Token { get; set; } = string.Empty;      // JWT token
    public UserProfileDto User { get; set; } = null!;
}

// A safe version of the User — never send PasswordHash to the app!
public class UserProfileDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Gender { get; set; }
    public string Address { get; set; } = string.Empty;
    public string? ProfilePictureUrl { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsKycVerified { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}