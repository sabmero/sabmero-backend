using sabmero.Models;

namespace sabmero.Models;

// A customer's booking for an on-site repair technician.
// ServiceType: "Electrical" | "CCTV" | "Tech" | "Installation"
// Status flow:  Pending → Approved → Processing → OnTheWay → Completed
//   Pending  : just created, under admin review (no technician yet)
//   Approved : admin assigned a technician → customer can now see tech details
public class ServiceBooking
{
    public int Id { get; set; }
    public int UserId { get; set; }             // FK → Users (the customer)
    public int? TechnicianId { get; set; }      // FK → Users (assigned later by admin)
    public string ServiceType { get; set; } = string.Empty;  // "Electrical" | "CCTV" | "Tech" | "Installation"
    public DateTime BookingDate { get; set; }
    public string TimeSlot { get; set; } = string.Empty;     // e.g. "10:00 AM - 12:00 PM"
    public double Latitude { get; set; }        // from Google Maps
    public double Longitude { get; set; }
    public string ServiceAddress { get; set; } = string.Empty;
    public string? DamageImagePath { get; set; }             // photo uploaded by customer
    public string? DamageImagePathsCsv { get; set; }         // all customer-uploaded damage photos (comma-separated paths)
    public string? Description { get; set; }                 // the customer's problem description
    public string Status { get; set; } = "Pending";          // "Pending"|"Approved"|"Processing"|"OnTheWay"|"Completed"
    public DateTime? CheckInTime { get; set; }               // technician taps "Check-In"
    public DateTime? CompletedTime { get; set; }             // technician taps "Complete"
    public string PaymentMethod { get; set; } = "Cash";      // "Cash" | "QR"
    public string? PaymentScreenshotPath { get; set; }       // QR payment proof — required when PaymentMethod == "QR"
    public string PaymentStatus { get; set; } = "Pending";   // "Pending" | "Submitted" | "Verified" | "Rejected"
    public decimal? ServiceCharge { get; set; }

    // How the technician collected the final charge when completing the job.
    public string? CollectionMethod { get; set; }            // "Cash" | "Online"
    public string? CollectionAmountInWords { get; set; }     // e.g. "Five hundred rupees only" / "पाँच सय रुपैयाँ"
    public string? CollectionScreenshotPath { get; set; }    // proof when CollectionMethod == "Online"
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ── Installation linkage ─────────────────────────────────────────────────
    // When a customer buys a product (e.g. AC, CCTV, TV) and also books its
    // installation at checkout, the order spawns an Installation booking that
    // lands in the Repair section. RelatedOrderId ties this booking back to the
    // order that created it. Null for ordinary standalone repair bookings.
    public int? RelatedOrderId { get; set; }

    // ── Service catalog linkage (optional) ───────────────────────────────────
    // What the customer picked in the app. Plain ids (no foreign keys) so the
    // booking history survives if an admin later deletes a service or category.
    public int? ServiceCategoryId { get; set; }   // main or sub category chosen
    public int? ServiceId { get; set; }           // RepairService chosen (if any)
    public int? ServicePackageId { get; set; }    // ServicePackage chosen (if any)

    // Navigation
    public User User { get; set; } = null!;
    public Order? RelatedOrder { get; set; }
    public Review? Review { get; set; }
}
