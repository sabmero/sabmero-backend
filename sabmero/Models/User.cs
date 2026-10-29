using Microsoft.AspNetCore.Mvc.ViewEngines;
using System.Numerics;

namespace sabmero.Models;

// This represents the "Users" table in PostgreSQL.
// One row = one person (Customer, Vendor, Admin, Technician, or Rider).
public class User
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;   // used as login ID
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;

    // Possible values: "Customer" | "Vendor" | "Admin" | "Technician" | "Rider"
    public string Role { get; set; } = "Customer";

    // ── KYC ──────────────────────────────────────────────────────────────────
    // KycStatus tracks where the user is in verification so the app can show a
    // clear state to the user:
    //   "NotSubmitted" : no document uploaded yet
    //   "Pending"      : document submitted, waiting for admin review
    //   "Approved"     : admin verified the document
    //   "Rejected"     : admin rejected it → KycRejectionReason explains why
    public string KycStatus { get; set; } = "NotSubmitted";
    public string? KycRejectionReason { get; set; }     // set only when KycStatus == "Rejected"

    // Kept for backward compatibility / quick checks. Mirrors KycStatus == "Approved".
    public bool IsKycVerified { get; set; } = false;
    public string? KycDocumentPath { get; set; }        // file path on server

    // Relative path of the profile picture on the server, e.g. "/uploads/profile/abc.jpg".
    public string? ProfilePicturePath { get; set; }

    // Latest FCM device token for push notifications (FCM HTTP v1).
    public string? FcmToken { get; set; }

    // Settings screen toggles (both default ON).
    //   PushEnabled        → "Push Notifications" (order & repair updates, vendor requests…)
    //   PromoAlertsEnabled → "Promotional Alerts" (deals and offers)
    public bool PushEnabled { get; set; } = true;
    public bool PromoAlertsEnabled { get; set; } = true;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties (EF Core uses these to JOIN tables)
    public Vendor? Vendor { get; set; }
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<ServiceBooking> ServiceBookings { get; set; } = new List<ServiceBooking>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
}