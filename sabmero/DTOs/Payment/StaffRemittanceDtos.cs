using System.ComponentModel.DataAnnotations;

namespace sabmero.DTOs.Payment;

// Technician / rider hands over money to SabMero.
public class SubmitRemittanceDto
{
    [Range(1, 100000000)]
    public decimal Amount { get; set; }
    public string Method { get; set; } = "Cash";      // "Cash" | "Online"
    public string? AmountInWords { get; set; }        // required for Cash
    public string? ScreenshotPath { get; set; }       // required for Online (POST /api/uploads/payment first)
    public string? Note { get; set; }
}

public class RejectRemittanceDto
{
    public string? Reason { get; set; }
}

public class SetStaffQrDto
{
    public string? QrImagePath { get; set; }          // null/empty removes it
}

public class StaffRemittanceDto
{
    public int Id { get; set; }
    public int StaffId { get; set; }
    public string StaffName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Method { get; set; } = "Cash";
    public string? AmountInWords { get; set; }
    public string? ScreenshotPath { get; set; }
    public string? Note { get; set; }
    public string Status { get; set; } = "Pending";   // Pending | Verified | Rejected
    public string? RejectReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? VerifiedAt { get; set; }
}
