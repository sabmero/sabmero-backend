using System.ComponentModel.DataAnnotations;

namespace sabmero.DTOs.Auth;

// Flutter app sends this JSON body to POST /api/auth/register
public class RegisterDto
{
    [Required(ErrorMessage = "Full name is required")]
    [MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Phone number is required")]
    [RegularExpression(@"^\d{10}$", ErrorMessage = "Phone must be exactly 10 digits")]
    public string Phone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required")]
    [MinLength(6, ErrorMessage = "Password must be at least 6 characters")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Address is required")]
    [MaxLength(300)]
    public string Address { get; set; } = string.Empty;

    // NOTE: There is intentionally NO Role property here. Role is an
    // authorization attribute and must be assigned by the server, never
    // chosen by the client. Public registration always creates a Customer;
    // privileged roles are created only through their controlled flows:
    //   Vendor      → POST /api/Vendors/register + admin approval
    //   Staff/Tech/Rider → POST /api/admin/staff (admin only)
    //   Admin       → seeded in the database
    // If a client still sends "role" in the JSON body it is silently
    // ignored by the model binder, so old app versions keep working.
}