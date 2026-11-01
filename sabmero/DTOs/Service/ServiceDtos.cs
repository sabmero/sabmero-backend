using System.ComponentModel.DataAnnotations;

namespace sabmero.DTOs.Service;

// ── Customer SENDS this to book an on-site repair ──
public class CreateBookingDto
{
    [Required(ErrorMessage = "Service type is required")]
    // "Electrical" | "CCTV" | "Tech"
    public string ServiceType { get; set; } = string.Empty;

    [Required(ErrorMessage = "Booking date is required")]
    public DateTime BookingDate { get; set; }

    [Required(ErrorMessage = "Time slot is required")]
    public string TimeSlot { get; set; } = string.Empty;   // e.g. "10:00 AM - 12:00 PM"

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    [Required(ErrorMessage = "Service address is required")]
    [MaxLength(400)]
    public string ServiceAddress { get; set; } = string.Empty;

    public string? DamageImagePath { get; set; }   // optional photo of the problem

    // All damage photos the customer uploaded (optional). The first is kept as
    // DamageImagePath for backward compatibility; all are stored and returned.
    public List<string>? DamageImagePaths { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }   // the customer's problem description

    // "Cash" | "QR"
    public string PaymentMethod { get; set; } = "Cash";

    // Required ONLY when PaymentMethod == "QR": the customer's payment proof.
    // Upload via POST /api/uploads/payment first, then send the returned path here.
    public string? PaymentScreenshotPath { get; set; }

    // Optional: which catalog entry the customer picked. Older app versions
    // simply do not send these.
    public int? ServiceCategoryId { get; set; }
    public int? ServiceId { get; set; }
    public int? ServicePackageId { get; set; }
}

// ── What the API SENDS BACK for a booking ──
public class BookingDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public int? TechnicianId { get; set; }
    public string? TechnicianName { get; set; }
    public string? TechnicianPhone { get; set; }    // populated once status is Approved (technician assigned)
    public string ServiceType { get; set; } = string.Empty;
    public DateTime BookingDate { get; set; }
    public string TimeSlot { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string ServiceAddress { get; set; } = string.Empty;
    public string? DamageImagePath { get; set; }
    public List<string> DamageImagePaths { get; set; } = new();
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? CheckInTime { get; set; }
    public DateTime? CompletedTime { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string? PaymentScreenshotPath { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal? ServiceCharge { get; set; }
    public DateTime CreatedAt { get; set; }

    // Catalog linkage (null for older bookings)
    public int? MainCategoryId { get; set; }
    public string? MainCategoryName { get; set; }
    public int? SubCategoryId { get; set; }
    public string? SubCategoryName { get; set; }
    public int? ServiceId { get; set; }
    public int? ServicePackageId { get; set; }
}

// ── Admin SENDS this to assign a technician ──
public class AssignTechnicianDto
{
    [Required]
    public int TechnicianId { get; set; }
}

// ── Technician/Admin SENDS this to update a booking's status ──
public class UpdateBookingStatusDto
{
    [Required]
    // "Pending" | "Approved" | "Processing" | "OnTheWay" | "Completed"
    public string Status { get; set; } = string.Empty;

    // Set when the technician completes the job and enters the final charge.
    public decimal? ServiceCharge { get; set; }
}