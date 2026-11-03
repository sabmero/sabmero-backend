using System.ComponentModel.DataAnnotations;

namespace sabmero.DTOs.Payment;

// ── ADMIN records a payment to a technician ──
// The screenshot is optional (cash payments have none). Upload it first via
// POST /api/uploads/payment and send the returned path.
public class RecordTechnicianPaymentDto
{
    [Required]
    public int TechnicianId { get; set; }

    [Required]
    [Range(0.01, 100000000, ErrorMessage = "Amount must be greater than 0.")]
    public decimal Amount { get; set; }

    [MaxLength(300)]
    public string? Note { get; set; }

    // "Cash" (write the amount in words) or "Online" (screenshot required).
    [MaxLength(10)]
    public string Method { get; set; } = "Cash";

    [MaxLength(200)]
    public string? AmountInWords { get; set; }

    [MaxLength(400)]
    public string? ScreenshotPath { get; set; }
}

// ── One technician payment row (shown to admin and to the technician) ──
public class TechnicianPaymentDto
{
    public int Id { get; set; }
    public int TechnicianId { get; set; }                    // the technician's or rider's user id
    public string TechnicianName { get; set; } = string.Empty;
    public string Role { get; set; } = "Technician";         // "Technician" | "Rider"
    public string Phone { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public string Method { get; set; } = "Cash";
    public string? AmountInWords { get; set; }
    public string? ScreenshotPath { get; set; }
    public string Status { get; set; } = "Paid";             // "Paid" | "Acknowledged"
    public DateTime CreatedAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
}

// ── A technician row in the admin "pay your technicians" list ──
public class TechnicianPayoutSummaryDto
{
    public int TechnicianId { get; set; }
    public string Role { get; set; } = "Technician";         // "Technician" | "Rider"
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public int CompletedJobs { get; set; }                   // jobs done / deliveries made
    public decimal TotalServiceCharge { get; set; }          // technician: charges on completed jobs; rider: COD collected
    public decimal DeliveryFees { get; set; }                // rider: delivery fees on delivered orders
    public decimal TotalPaid { get; set; }                   // sum already paid to the technician
    public DateTime? LastPaidAt { get; set; }
}

// ── What the technician sees on their payments page ──
public class MyTechnicianPayoutDto
{
    public decimal TotalReceived { get; set; }
    public List<TechnicianPaymentDto> Payments { get; set; } = new();

    // ── "My summary" for the technician / rider app ──
    public string Role { get; set; } = "Technician";
    public int Jobs { get; set; }               // completed jobs / delivered orders
    public int JobsToday { get; set; }
    public int JobsThisMonth { get; set; }
    public decimal CollectedCash { get; set; }  // money collected from customers
    public decimal CollectedOnline { get; set; }
    public decimal CollectedTotal { get; set; }
    public decimal CollectedToday { get; set; }
    public decimal DeliveryFees { get; set; }   // riders: delivery charges of delivered orders
    public int PendingConfirmations { get; set; } // payments received but not yet confirmed
    public List<CollectionRowDto> RecentJobs { get; set; } = new();
}

// ── Collections report: money technicians collected from customers ──
public class CollectionRowDto
{
    public string Kind { get; set; } = "Booking";            // "Booking" (technician) | "Order" (rider)
    public int BookingId { get; set; }                       // booking or order id
    public int TechnicianId { get; set; }
    public string TechnicianName { get; set; } = string.Empty;
    public string Role { get; set; } = "Technician";
    public string ServiceType { get; set; } = string.Empty;  // service type, or "Delivery"
    public string CustomerName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Method { get; set; } = "Cash";             // "Cash" | "Online" | "Prepaid" (rider, already paid)
    public string? AmountInWords { get; set; }
    public string? ScreenshotPath { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class CollectionSummaryDto
{
    public int TechnicianId { get; set; }
    public string TechnicianName { get; set; } = string.Empty;
    public string Role { get; set; } = "Technician";
    public int Jobs { get; set; }
    public decimal DeliveryFees { get; set; }                // rider: delivery fees earned in the period
    public decimal Cash { get; set; }
    public decimal Online { get; set; }
    public decimal Total { get; set; }
    public decimal PaidToTechnician { get; set; }            // admin payments in the same period
}

public class CollectionsReportDto
{
    public decimal TotalCash { get; set; }
    public decimal TotalOnline { get; set; }
    public decimal TotalPaidToTechnicians { get; set; }
    public List<CollectionSummaryDto> Summary { get; set; } = new();
    public List<CollectionRowDto> Rows { get; set; } = new();
}
