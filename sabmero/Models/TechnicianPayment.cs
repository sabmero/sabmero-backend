namespace sabmero.Models;

// A payment the ADMIN makes to a TECHNICIAN (e.g. share of collected service
// charges). The admin pays outside the app (cash / bank / QR) and records it
// here with an amount, an optional note and an optional proof screenshot. The
// technician is notified and can mark it received.
//
// Status values:  "Paid" (admin recorded it) → "Acknowledged" (technician confirmed)
public class TechnicianPayment
{
    public int Id { get; set; }

    public int TechnicianId { get; set; }                    // FK → Users (Role = Technician)
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public string Method { get; set; } = "Cash";             // "Cash" | "Online"
    public string? AmountInWords { get; set; }               // cash amount written in words
    public string? ScreenshotPath { get; set; }

    public string Status { get; set; } = "Paid";             // "Paid" | "Acknowledged"

    public int PaidByUserId { get; set; }                    // the admin who recorded it
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AcknowledgedAt { get; set; }

    // Navigation
    public User Technician { get; set; } = null!;
}
