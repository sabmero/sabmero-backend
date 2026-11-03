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

    // ── Commission settings (AppSettings keys) ───────────────────────────────
    private const string RiderPctKey = "RiderCommissionPercent";
    private const string TechPctKey = "TechnicianCommissionPercent";

    public async Task<CommissionSettingsDto> GetCommissionAsync()
    {
        var vals = await _db.AppSettings.AsNoTracking()
            .Where(s => s.Key == RiderPctKey || s.Key == TechPctKey).ToListAsync();
        decimal Get(string k, decimal def) =>
            decimal.TryParse(vals.FirstOrDefault(s => s.Key == k)?.Value,
                System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : def;
        return new CommissionSettingsDto { RiderPercent = Get(RiderPctKey, 80m), TechnicianPercent = Get(TechPctKey, 70m) };
    }

    public async Task SetCommissionAsync(CommissionSettingsDto dto)
    {
        async Task Put(string key, decimal v)
        {
            var row = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
            var text = Math.Clamp(v, 0m, 100m).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (row == null) _db.AppSettings.Add(new AppSetting { Key = key, Value = text, UpdatedAt = DateTime.UtcNow });
            else { row.Value = text; row.UpdatedAt = DateTime.UtcNow; }
        }
        await Put(RiderPctKey, dto.RiderPercent);
        await Put(TechPctKey, dto.TechnicianPercent);
        await _db.SaveChangesAsync();
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

        // Jobs / deliveries + money collected, from the same data the admin report uses.
        const int nepalOffset = 345;
        var report = await GetCollectionsAsync(null, technicianId, nepalOffset);
        var nowLocal = DateTime.UtcNow.AddMinutes(nepalOffset);
        var todayStartUtc = DateTime.SpecifyKind(nowLocal.Date.AddMinutes(-nepalOffset), DateTimeKind.Utc);
        var monthStartUtc = DateTime.SpecifyKind(new DateTime(nowLocal.Year, nowLocal.Month, 1).AddMinutes(-nepalOffset), DateTimeKind.Utc);
        var jobs = report.Rows;
        var summary = report.Summary.FirstOrDefault(s => s.TechnicianId == technicianId);
        var role = await _db.Users.AsNoTracking().Where(u => u.Id == technicianId).Select(u => u.Role).FirstOrDefaultAsync() ?? "Technician";

        var myRemits = await _db.StaffRemittances.AsNoTracking().Include(r => r.Staff)
            .Where(r => r.StaffId == technicianId)
            .OrderByDescending(r => r.CreatedAt).Take(30).ToListAsync();
        var verified = await _db.StaffRemittances.AsNoTracking()
            .Where(r => r.StaffId == technicianId && r.Status == "Verified").SumAsync(r => (decimal?)r.Amount) ?? 0m;
        var pendingAmt = await _db.StaffRemittances.AsNoTracking()
            .Where(r => r.StaffId == technicianId && r.Status == "Pending").SumAsync(r => (decimal?)r.Amount) ?? 0m;
        var myQr = await _db.Users.AsNoTracking().Where(u => u.Id == technicianId).Select(u => u.PaymentQrPath).FirstOrDefaultAsync();
        var cashTotal = jobs.Where(j => j.Method == "Cash").Sum(j => j.Amount);
        var comm = await GetCommissionAsync();
        var pct = role == "Rider" ? comm.RiderPercent : comm.TechnicianPercent;
        var basis = role == "Rider"
            ? (summary?.DeliveryFees ?? 0m)
            : jobs.Where(j => j.Kind == "Booking").Sum(j => j.Amount);
        var earned = Math.Round(basis * pct / 100m, 0);

        return new MyTechnicianPayoutDto
        {
            CommissionPercent = pct,
            Earned = earned,
            Payable = Math.Max(0m, earned - rows.Sum(p => p.Amount)),
            CashHeld = Math.Max(0m, cashTotal - verified),
            Remitted = verified,
            RemittedPending = pendingAmt,
            MyQrPath = myQr,
            Remittances = myRemits.Select(r => new StaffRemittanceDto
            {
                Id = r.Id, StaffId = r.StaffId, StaffName = r.Staff?.FullName ?? string.Empty, Role = r.Staff?.Role ?? string.Empty,
                Amount = r.Amount, Method = r.Method, AmountInWords = r.AmountInWords, ScreenshotPath = r.ScreenshotPath,
                Note = r.Note, Status = r.Status, RejectReason = r.RejectReason, CreatedAt = r.CreatedAt, VerifiedAt = r.VerifiedAt
            }).ToList(),
            TotalReceived = rows.Sum(p => p.Amount),
            Payments = rows.Select(Map).ToList(),
            Role = role,
            Jobs = jobs.Count,
            JobsToday = jobs.Count(j => j.CompletedAt >= todayStartUtc),
            JobsThisMonth = jobs.Count(j => j.CompletedAt >= monthStartUtc),
            CollectedCash = jobs.Where(j => j.Method == "Cash").Sum(j => j.Amount),
            CollectedOnline = jobs.Where(j => j.Method == "Online").Sum(j => j.Amount),
            CollectedTotal = jobs.Sum(j => j.Amount),
            CollectedToday = jobs.Where(j => j.CompletedAt >= todayStartUtc).Sum(j => j.Amount),
            DeliveryFees = summary?.DeliveryFees ?? 0m,
            PendingConfirmations = rows.Count(p => p.Status == "Paid"),
            RecentJobs = jobs.Take(30).ToList()
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

        var comm = await GetCommissionAsync();
        var cashRows = (await GetCollectionsAsync(null, null, 345)).Rows
            .Where(r => r.Method == "Cash")
            .GroupBy(r => r.TechnicianId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var remits = await _db.StaffRemittances.AsNoTracking()
            .GroupBy(r => new { r.StaffId, r.Status })
            .Select(g => new { g.Key.StaffId, g.Key.Status, Total = g.Sum(x => x.Amount), Count = g.Count() })
            .ToListAsync();

        return staff.Select(u =>
        {
            var pd = paid.FirstOrDefault(x => x.Id == u.Id);
            var cash = cashRows.TryGetValue(u.Id, out var cv) ? cv : 0m;
            var ver = remits.Where(x => x.StaffId == u.Id && x.Status == "Verified").Sum(x => x.Total);
            var pend = remits.Where(x => x.StaffId == u.Id && x.Status == "Pending").Sum(x => x.Count);
            var dto = new TechnicianPayoutSummaryDto
            {
                TechnicianId = u.Id,
                Role = u.Role,
                Name = u.FullName,
                Phone = u.Phone ?? string.Empty,
                TotalPaid = pd?.Total ?? 0m,
                LastPaidAt = pd?.Last,
                PaymentQrPath = u.PaymentQrPath,
                CashCollected = cash,
                Remitted = ver,
                CashHeld = Math.Max(0m, cash - ver),
                PendingRemittances = pend
            };
            dto.CommissionPercent = u.Role == "Rider" ? comm.RiderPercent : comm.TechnicianPercent;
            dto.Earned = Math.Round((u.Role == "Rider" ? (deliveries.FirstOrDefault(x => x.Id == u.Id)?.Fees ?? 0m)
                                                       : (jobs.FirstOrDefault(x => x.Id == u.Id)?.Charge ?? 0m)) * dto.CommissionPercent / 100m, 0);
            dto.Payable = Math.Max(0m, dto.Earned - dto.TotalPaid);
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
