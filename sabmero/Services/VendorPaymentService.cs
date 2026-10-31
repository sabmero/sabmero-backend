using Microsoft.EntityFrameworkCore;
using sabmero.Data;
using sabmero.DTOs.Payment;
using sabmero.Models;

namespace sabmero.Services;

// Admin → vendor commission / settlement payments.
//
// The vendor saves a payout QR on their profile. The admin scans it, pays the
// vendor externally, and records the payment here with a proof screenshot. The
// vendor then sees the payment and can mark it received, so both sides have a
// confirmed history that the settlement is done.
public class VendorPaymentService : IVendorPaymentService
{
    private readonly AppDbContext _db;
    private readonly ILogger<VendorPaymentService> _logger;
    private readonly IPushService _push;

    public VendorPaymentService(AppDbContext db, ILogger<VendorPaymentService> logger, IPushService push)
    {
        _db = db;
        _logger = logger;
        _push = push;
    }

    // ── VENDOR: read own QR ──────────────────────────────────────────────────
    public async Task<VendorQrDto?> GetMyQrAsync(int userId)
    {
        var vendor = await _db.Vendors.AsNoTracking().FirstOrDefaultAsync(v => v.UserId == userId);
        if (vendor == null) return null;
        return new VendorQrDto { QrImagePath = vendor.PaymentQrPath };
    }

    // ── VENDOR: set/replace own QR ───────────────────────────────────────────
    public async Task<(bool Success, string Message)> SetMyQrAsync(int userId, string qrImagePath)
    {
        if (string.IsNullOrWhiteSpace(qrImagePath))
            return (false, "QR image path is required.");

        var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.UserId == userId);
        if (vendor == null)
            return (false, "No vendor profile found.");

        vendor.PaymentQrPath = qrImagePath;
        await _db.SaveChangesAsync();
        _logger.LogInformation("Vendor {VendorId} updated payout QR", vendor.Id);
        return (true, "Your payment QR has been saved.");
    }

    // ── VENDOR: my payouts (QR + total + history) ────────────────────────────
    public async Task<MyPayoutInfoDto?> GetMyPayoutsAsync(int userId)
    {
        var vendor = await _db.Vendors.AsNoTracking().FirstOrDefaultAsync(v => v.UserId == userId);
        if (vendor == null) return null;

        var payments = await _db.VendorPayments
            .AsNoTracking()
            .Where(vp => vp.VendorId == vendor.Id)
            .OrderByDescending(vp => vp.CreatedAt)
            .ToListAsync();

        return new MyPayoutInfoDto
        {
            PaymentQrPath = vendor.PaymentQrPath,
            TotalReceived = payments.Sum(p => p.Amount),
            Payments = payments.Select(p => Map(p, vendor)).ToList()
        };
    }

    // ── VENDOR: acknowledge a received payment ───────────────────────────────
    public async Task<(bool Success, string Message)> AcknowledgeAsync(int userId, int paymentId)
    {
        var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.UserId == userId);
        if (vendor == null)
            return (false, "No vendor profile found.");

        var payment = await _db.VendorPayments
            .FirstOrDefaultAsync(vp => vp.Id == paymentId && vp.VendorId == vendor.Id);
        if (payment == null)
            return (false, "Payment not found.");
        if (payment.Status == "Acknowledged")
            return (false, "You have already confirmed this payment.");

        payment.Status = "Acknowledged";
        payment.AcknowledgedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        _logger.LogInformation("Vendor {VendorId} acknowledged payment {PaymentId}", vendor.Id, paymentId);
        return (true, "Payment confirmed as received.");
    }

    // ── ADMIN: vendors to pay (with QR + totals) ─────────────────────────────
    public async Task<List<VendorPayoutSummaryDto>> GetVendorsForPayoutAsync()
    {
        var vendors = await _db.Vendors
            .AsNoTracking()
            .Include(v => v.User)
            .Where(v => v.IsApproved)
            .OrderBy(v => v.BusinessName)
            .ToListAsync();

        var totals = await _db.VendorPayments
            .AsNoTracking()
            .GroupBy(vp => vp.VendorId)
            .Select(g => new { VendorId = g.Key, Total = g.Sum(x => x.Amount), Last = g.Max(x => x.CreatedAt) })
            .ToListAsync();

        return vendors.Select(v =>
        {
            var t = totals.FirstOrDefault(x => x.VendorId == v.Id);
            return new VendorPayoutSummaryDto
            {
                VendorId = v.Id,
                BusinessName = v.BusinessName,
                OwnerName = v.User.FullName,
                Phone = v.User.Phone,
                PaymentQrPath = v.PaymentQrPath,
                CommissionRate = v.CommissionRate,
                TotalPaid = t?.Total ?? 0m,
                LastPaidAt = t?.Last
            };
        }).ToList();
    }

    // ── ADMIN: record a payment to a vendor ──────────────────────────────────
    public async Task<(bool Success, string Message, VendorPaymentDto? Data)> RecordAsync(int adminUserId, RecordVendorPaymentDto dto)
    {
        if (dto.Amount <= 0)
            return (false, "Amount must be greater than 0.", null);
        if (string.IsNullOrWhiteSpace(dto.ScreenshotPath))
            return (false, "Please upload a payment screenshot first.", null);

        var vendor = await _db.Vendors.Include(v => v.User).FirstOrDefaultAsync(v => v.Id == dto.VendorId);
        if (vendor == null)
            return (false, "Vendor not found.", null);

        var payment = new VendorPayment
        {
            VendorId = vendor.Id,
            Amount = dto.Amount,
            Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note!.Trim(),
            ScreenshotPath = dto.ScreenshotPath,
            Status = "Paid",
            PaidByUserId = adminUserId,
            CreatedAt = DateTime.UtcNow
        };
        _db.VendorPayments.Add(payment);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Admin {AdminId} recorded payment {PaymentId} of {Amount} to vendor {VendorId}",
            adminUserId, payment.Id, payment.Amount, vendor.Id);

        // Notify the vendor by push.
        await _push.SendToUserAsync(
            vendor.UserId,
            "Payment Received 💰",
            $"You have been paid Rs {payment.Amount:0} by SabMero. Open the app to confirm receipt.",
            new Dictionary<string, string> { ["type"] = "vendorPayment", ["paymentId"] = payment.Id.ToString() });

        return (true, "Payment recorded and the vendor has been notified.", Map(payment, vendor));
    }

    // ── ADMIN: full history (optionally by vendor) ───────────────────────────
    public async Task<List<VendorPaymentDto>> GetHistoryAsync(int? vendorId)
    {
        var q = _db.VendorPayments.AsNoTracking().Include(vp => vp.Vendor).ThenInclude(v => v.User).AsQueryable();
        if (vendorId.HasValue)
            q = q.Where(vp => vp.VendorId == vendorId.Value);

        var rows = await q.OrderByDescending(vp => vp.CreatedAt).ToListAsync();
        return rows.Select(p => Map(p, p.Vendor)).ToList();
    }

    // ── Helper ───────────────────────────────────────────────────────────────
    private static VendorPaymentDto Map(VendorPayment p, Vendor v)
        => new VendorPaymentDto
        {
            Id = p.Id,
            VendorId = p.VendorId,
            VendorName = v.BusinessName,
            OwnerName = v.User?.FullName ?? string.Empty,
            Phone = v.User?.Phone ?? string.Empty,
            Amount = p.Amount,
            Note = p.Note,
            ScreenshotPath = p.ScreenshotPath,
            Status = p.Status,
            CreatedAt = p.CreatedAt,
            AcknowledgedAt = p.AcknowledgedAt
        };
}
