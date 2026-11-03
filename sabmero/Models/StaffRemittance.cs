namespace sabmero.Models;

// Money a technician / rider hands over to SabMero (cash they collected from
// customers). Cash: handed over in person, amount written in words. Online:
// paid to the admin's QR, screenshot attached. The admin verifies or rejects;
// only VERIFIED remittances reduce what the person still has to hand over.
public class StaffRemittance
{
    public int Id { get; set; }
    public int StaffId { get; set; }                         // FK -> Users (technician or rider)
    public decimal Amount { get; set; }
    public string Method { get; set; } = "Cash";             // "Cash" | "Online"
    public string? AmountInWords { get; set; }
    public string? ScreenshotPath { get; set; }
    public string? Note { get; set; }
    public string Status { get; set; } = "Pending";          // Pending | Verified | Rejected
    public string? RejectReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? VerifiedByUserId { get; set; }
    public DateTime? VerifiedAt { get; set; }

    public User? Staff { get; set; }
}
