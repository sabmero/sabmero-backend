using sabmero.DTOs.Payment;

namespace sabmero.Services;

// Admin → technician payments.
public interface ITechnicianPaymentService
{
    // ── Technician ──
    Task<MyTechnicianPayoutDto> GetMyPayoutsAsync(int technicianId);
    Task<(bool Success, string Message)> AcknowledgeAsync(int technicianId, int paymentId);

    // ── Admin ──
    Task<List<TechnicianPayoutSummaryDto>> GetTechniciansForPayoutAsync();
    Task<(bool Success, string Message, TechnicianPaymentDto? Data)> RecordAsync(int adminUserId, RecordTechnicianPaymentDto dto);
    Task<List<TechnicianPaymentDto>> GetHistoryAsync(int? technicianId);
    Task<CollectionsReportDto> GetCollectionsAsync(string? date, int? technicianId, int tzOffsetMinutes);
    Task<CommissionSettingsDto> GetCommissionAsync();
    Task SetCommissionAsync(CommissionSettingsDto dto);
}
