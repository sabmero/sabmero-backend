using Microsoft.EntityFrameworkCore;
using sabmero.Data;
using sabmero.DTOs.Payment;
using sabmero.Models;

namespace sabmero.Services;

// Admin → technician / rider payments, plus the "what did they collect" report.
// The admin pays outside the app and records it here; ONLY that person is
// notified, and can confirm receipt.
public class TechnicianPaymentService : ITechnicianPaymentService
{
    private static readonly string[] StaffRoles = { "Technician", "Rider" };

    private readonly AppDbContext _db;
    private readonly ILogger<TechnicianPaymentService> _logger;
    private readonly IPushService _push;

    public TechnicianPaymentService(AppDbContext db, ILogger<TechnicianPaymentService> logger, IPushService push)
    {
        _db = db;
        _logger = logger;
        _push = push;
    }

    // ── TECHNICIAN / RIDER: my payments ──────────────────────────────────────
    public async Task<MyTechnicianPayoutDto> GetMyPayoutsAsync(int technicianId)
    {
        var rows = await _db.TechnicianPayments
            .AsNoTracking()
            .Include(p => p.Technician)
            .Where(p => p.TechnicianId == technicianId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        return new MyTechnicianPayoutDto
        {
            TotalReceived = rows.Sum(p => p.Amount),
            Payments = rows.Select(Map).ToList()
        };
    }

    // ── TECHNICIAN / RIDER: confirm receipt ──────────────────────────────────
    public async Task<(bool Success, string Message)> AcknowledgeAsync(int technicianId, int paymentId)
    {
        var payment = await _db.TechnicianPayments
            .FirstOrDefaultAsync(p => p.Id == paymentId && p.TechnicianId == technicianId);
        if (payment == null)
            return (false, "Payment not found.");
        if (payment.Status == "Acknowledged")
            return (false, "You have already confirmed this payment.");

        payment.Status = "Acknowledged";
        payment.AcknowledgedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        try
        {
            var name = await _db.Users.Where(u => u.Id == technicianId).Select(u => u.FullName).FirstOrDefaultAsync();
            var admins = await _db.Users.Where(u => u.Role == "Admin" && u.IsActive).Select(u => u.Id).ToListAsync();
            foreach (var adminId in admins)
                await _push.SendToUserAsync(adminId, "Payment Confirmed ✅",
                    $"{name ?? "Staff"} confirmed receiving Rs {payment.Amount:0}.",
                    new Dictionary<string, string> { ["type"] = "technicianPaymentAck", ["paymentId"] = payment.Id.ToString() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to notify admins about payment {Id}", paymentId);
        }

        return (true, "Payment confirmed as received.");
    }

    // ── ADMIN: technicians + riders to pay ───────────────────────────────────
    public async Task<List<TechnicianPayoutSummaryDto>> GetTechniciansForPayoutAsync()
    {
        var staff = await _db.Users.AsNoTracking()
            .Where(u => StaffRoles.Contains(u.Role) && u.IsActive)
            .OrderBy(u => u.Role).ThenBy(u => u.FullName)
            .ToListAsync();

        var jobs = await _db.ServiceBookings.AsNoTracking()
            .Where(b => b.TechnicianId != null && b.Status == "Completed")
            .GroupBy(b => b.TechnicianId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count(), Charge = g.Sum(x => x.ServiceCharge ?? 0m) })
            .ToListAsync();

        var deliveries = await _db.Orders.AsNoTracking()
            .Where(o => o.RiderId != null && o.Status == "Delivered")
            .GroupBy(o => o.RiderId!.Value)
            .Select(g => new
            {
                Id = g.Key,
                Count = g.Count(),
                Collected = g.Sum(x => x.PaymentMethod == "COD" ? x.TotalAmount : 0m),
                Fees = g.Sum(x => x.DeliveryFee)
            })
            .ToListAsync();

        var paid = await _db.TechnicianPayments.AsNoTracking()
            .GroupBy(p => p.TechnicianId)
            .Select(g => new { Id = g.Key, Total = g.Sum(x => x.Amount), Last = g.Max(x => x.CreatedAt) })
            .ToListAsync();

        return staff.Select(u =>
        {
            var pd = paid.FirstOrDefault(x => x.Id == u.Id);
            var dto = new TechnicianPayoutSummaryDto
            {
                TechnicianId = u.Id,
                Role = u.Role,
                Name = u.FullName,
                Phone = u.Phone ?? string.Empty,
                TotalPaid = pd?.Total ?? 0m,
                LastPaidAt = pd?.Last
            };
            if (u.Role == "Rider")
            {
                var d = deliveries.FirstOrDefault(x => x.Id == u.Id);
                dto.CompletedJobs = d?.Count ?? 0;
                dto.TotalServiceCharge = d?.Collected ?? 0m;
                dto.DeliveryFees = d?.Fees ?? 0m;
            }
            else
            {
                var j = jobs.FirstOrDefault(x => x.Id == u.Id);
                dto.CompletedJobs = j?.Count ?? 0;
                dto.TotalServiceCharge = j?.Charge ?? 0m;
            }
            return dto;
        }).ToList();
    }

    // ── ADMIN: record a payment ──────────────────────────────────────────────
    public async Task<(bool Success, string Message, TechnicianPaymentDto? Data)> RecordAsync(int adminUserId, RecordTechnicianPaymentDto dto)
    {
        if (dto.Amount <= 0)
            return (false, "Amount must be greater than 0.", null);

        var tech = await _db.Users.FirstOrDefaultAsync(u => u.Id == dto.TechnicianId);
        if (tech == null || !StaffRoles.Contains(tech.Role))
            return (false, "That user is not a technician or rider.", null);

        var method = string.Equals(dto.Method?.Trim(), "online", StringComparison.OrdinalIgnoreCase) ? "Online" : "Cash";
        if (method == "Online" && string.IsNullOrWhiteSpace(dto.ScreenshotPath))
            return (false, "Please attach the online payment screenshot.", null);

        var payment = new TechnicianPayment
        {
            TechnicianId = tech.Id,
            Amount = dto.Amount,
            Method = method,
            AmountInWords = string.IsNullOrWhiteSpace(dto.AmountInWords) ? null : dto.AmountInWords!.Trim(),
            Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note!.Trim(),
            ScreenshotPath = string.IsNullOrWhiteSpace(dto.ScreenshotPath) ? null : dto.ScreenshotPath,
            Status = "Paid",
            PaidByUserId = adminUserId,
            CreatedAt = DateTime.UtcNow
        };
        _db.TechnicianPayments.Add(payment);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Admin {AdminId} recorded payment {PaymentId} of {Amount} to {Role} {TechId}",
            adminUserId, payment.Id, payment.Amount, tech.Role, tech.Id);

        // Notify ONLY this person.
        var data = new Dictionary<string, string> { ["type"] = "technicianPayment", ["paymentId"] = payment.Id.ToString() };
        if (method == "Online") data["image"] = payment.ScreenshotPath!;
        await _push.SendToUserAsync(tech.Id, "Payment Received 💰",
            $"SabMero paid you Rs {payment.Amount:0} ({payment.Method}).{(payment.Note != null ? " " + payment.Note : "")}",
            data);

        payment.Technician = tech;
        return (true, "Payment recorded and the person has been notified.", Map(payment));
    }

    // ── ADMIN: history ───────────────────────────────────────────────────────
    public async Task<List<TechnicianPaymentDto>> GetHistoryAsync(int? technicianId)
    {
        var q = _db.TechnicianPayments.AsNoTracking().Include(p => p.Technician).AsQueryable();
        if (technicianId.HasValue)
            q = q.Where(p => p.TechnicianId == technicianId.Value);

        var rows = await q.OrderByDescending(p => p.CreatedAt).ToListAsync();
        return rows.Select(Map).ToList();
    }

    // ── ADMIN: what technicians + riders collected ───────────────────────────
    // date = "yyyy-MM-dd" in the admin's local time (tzOffsetMinutes east of UTC,
    // 345 for Nepal); omit it to see everything.
    public async Task<CollectionsReportDto> GetCollectionsAsync(string? date, int? technicianId, int tzOffsetMinutes)
    {
        DateTime? start = null, end = null;
        if (!string.IsNullOrWhiteSpace(date) && DateTime.TryParse(date, out var d))
        {
            start = DateTime.SpecifyKind(d.Date.AddMinutes(-tzOffsetMinutes), DateTimeKind.Utc);
            end = start.Value.AddDays(1);
        }

        // Technician jobs (completed bookings)
        var bq = _db.ServiceBookings.AsNoTracking().Include(b => b.User)
            .Where(b => b.TechnicianId != null && b.Status == "Completed");
        if (technicianId.HasValue) bq = bq.Where(b => b.TechnicianId == technicianId.Value);
        if (start.HasValue) bq = bq.Where(b => b.CompletedTime >= start && b.CompletedTime < end);
        var bookings = await bq.OrderByDescending(b => b.CompletedTime).ToListAsync();

        // Rider deliveries (delivered orders)
        var oq = _db.Orders.AsNoTracking().Include(o => o.User)
            .Where(o => o.RiderId != null && o.Status == "Delivered");
        if (technicianId.HasValue) oq = oq.Where(o => o.RiderId == technicianId.Value);
        if (start.HasValue) oq = oq.Where(o => o.DeliveredAt >= start && o.DeliveredAt < end);
        var orders = await oq.OrderByDescending(o => o.DeliveredAt).ToListAsync();

        // Payments the admin made in the same period
        var pq = _db.TechnicianPayments.AsNoTracking().AsQueryable();
        if (technicianId.HasValue) pq = pq.Where(p => p.TechnicianId == technicianId.Value);
        if (start.HasValue) pq = pq.Where(p => p.CreatedAt >= start && p.CreatedAt < end);
        var paid = await pq.GroupBy(p => p.TechnicianId)
            .Select(g => new { Id = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync();

        var ids = bookings.Select(b => b.TechnicianId!.Value)
            .Concat(orders.Select(o => o.RiderId!.Value))
            .Concat(paid.Select(x => x.Id))
            .Distinct().ToList();
        var people = await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new { u.FullName, u.Role });

        string NameOf(int id) => people.TryGetValue(id, out var p) ? p.FullName : string.Empty;
        string RoleOf(int id) => people.TryGetValue(id, out var p) ? p.Role : "Technician";

        var rows = new List<CollectionRowDto>();
        rows.AddRange(bookings.Select(b => new CollectionRowDto
        {
            Kind = "Booking",
            BookingId = b.Id,
            TechnicianId = b.TechnicianId!.Value,
            TechnicianName = NameOf(b.TechnicianId!.Value),
            Role = "Technician",
            ServiceType = b.ServiceType,
            CustomerName = b.User?.FullName ?? string.Empty,
            Amount = b.ServiceCharge ?? 0m,
            Method = b.CollectionMethod ?? "Cash",
            AmountInWords = b.CollectionAmountInWords,
            ScreenshotPath = b.CollectionScreenshotPath,
            CompletedAt = b.CompletedTime
        }));
        rows.AddRange(orders.Select(o =>
        {
            var cod = o.PaymentMethod == "COD";
            return new CollectionRowDto
            {
                Kind = "Order",
                BookingId = o.Id,
                TechnicianId = o.RiderId!.Value,
                TechnicianName = NameOf(o.RiderId!.Value),
                Role = "Rider",
                ServiceType = "Delivery",
                CustomerName = o.User?.FullName ?? string.Empty,
                Amount = cod ? o.TotalAmount : 0m,
                Method = cod ? (o.CollectionMethod ?? "Cash") : "Prepaid",
                AmountInWords = o.CollectionAmountInWords,
                ScreenshotPath = o.CollectionScreenshotPath,
                CompletedAt = o.DeliveredAt
            };
        }));
        rows = rows.OrderByDescending(r => r.CompletedAt).ToList();

        var fees = orders.GroupBy(o => o.RiderId!.Value).ToDictionary(g => g.Key, g => g.Sum(x => x.DeliveryFee));

        var summary = rows.GroupBy(r => r.TechnicianId).Select(g => new CollectionSummaryDto
        {
            TechnicianId = g.Key,
            TechnicianName = NameOf(g.Key),
            Role = RoleOf(g.Key),
            Jobs = g.Count(),
            DeliveryFees = fees.TryGetValue(g.Key, out var f) ? f : 0m,
            Cash = g.Where(r => r.Method == "Cash").Sum(r => r.Amount),
            Online = g.Where(r => r.Method == "Online").Sum(r => r.Amount),
            Total = g.Sum(r => r.Amount),
            PaidToTechnician = paid.FirstOrDefault(x => x.Id == g.Key)?.Total ?? 0m
        }).ToList();

        // Paid in this period but no job in it.
        foreach (var p in paid.Where(x => summary.All(s => s.TechnicianId != x.Id)))
            summary.Add(new CollectionSummaryDto
            {
                TechnicianId = p.Id,
                TechnicianName = NameOf(p.Id),
                Role = RoleOf(p.Id),
                PaidToTechnician = p.Total
            });

        return new CollectionsReportDto
        {
            TotalCash = summary.Sum(s => s.Cash),
            TotalOnline = summary.Sum(s => s.Online),
            TotalPaidToTechnicians = summary.Sum(s => s.PaidToTechnician),
            Summary = summary.OrderBy(s => s.Role).ThenBy(s => s.TechnicianName).ToList(),
            Rows = rows
        };
    }

    private static TechnicianPaymentDto Map(TechnicianPayment p) => new()
    {
        Id = p.Id,
        TechnicianId = p.TechnicianId,
        TechnicianName = p.Technician?.FullName ?? string.Empty,
        Role = p.Technician?.Role ?? "Technician",
        Phone = p.Technician?.Phone ?? string.Empty,
        Amount = p.Amount,
        Note = p.Note,
        Method = p.Method,
        AmountInWords = p.AmountInWords,
        ScreenshotPath = p.ScreenshotPath,
        Status = p.Status,
        CreatedAt = p.CreatedAt,
        AcknowledgedAt = p.AcknowledgedAt
    };
}
