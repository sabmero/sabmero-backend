using Microsoft.EntityFrameworkCore;
using sabmero.Data;
using sabmero.DTOs.Promo;
using sabmero.Models;

namespace sabmero.Services;

// Admin manages discount codes; customers validate them at checkout.
public class PromoService : IPromoService
{
    private readonly AppDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PromoService> _logger;

    public PromoService(AppDbContext db, IServiceScopeFactory scopeFactory, ILogger<PromoService> logger)
    {
        _db = db;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<List<PromoDto>> GetAllAsync()
        => await _db.PromoCodes
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new PromoDto
            {
                Id = p.Id,
                Code = p.Code,
                DiscountPercent = p.DiscountPercent,
                ExpiresAt = p.ExpiresAt,
                IsActive = p.IsActive,
                IsExpired = p.ExpiresAt < DateTime.UtcNow
            })
            .ToListAsync();

    public async Task<(bool Success, string Message, PromoDto? Data)> CreateAsync(CreatePromoDto dto)
    {
        var code = dto.Code.Trim().ToUpper();
        bool exists = await _db.PromoCodes.AnyAsync(p => p.Code.ToUpper() == code);
        if (exists)
            return (false, "This promo code already exists.", null);

        var promo = new PromoCode
        {
            Code = code,
            DiscountPercent = dto.DiscountPercent,
            ExpiresAt = DateTime.SpecifyKind(dto.ExpiresAt, DateTimeKind.Utc),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.PromoCodes.Add(promo);
        await _db.SaveChangesAsync();

        // Tell customers about the new offer ("Promotional Alerts" switch).
        // Runs in the background so the admin's request returns immediately.
        var (promoCode, percent, expires) = (promo.Code, promo.DiscountPercent, promo.ExpiresAt);
        _ = Task.Run(() => BroadcastPromoAsync(promoCode, percent, expires));

        return (true, "Promo code created.", Map(promo));
    }

    // Push the new promo to every active customer who kept "Promotional Alerts"
    // and "Push Notifications" switched on. Uses its own DI scope because the
    // request's DbContext is gone by the time this runs.
    private async Task BroadcastPromoAsync(string code, decimal discountPercent, DateTime expiresAt)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var push = scope.ServiceProvider.GetRequiredService<IPushService>();

            var userIds = await db.Users
                .AsNoTracking()
                .Where(u => u.Role == "Customer" && u.IsActive && u.PromoAlertsEnabled)
                .Select(u => u.Id)
                .ToListAsync();

            var title = "New Offer 🎉";
            var body = $"Use code {code} to get {discountPercent:0.##}% off. Valid till {expiresAt:dd MMM yyyy}.";
            var data = new Dictionary<string, string> { ["type"] = "promo", ["code"] = code };

            foreach (var id in userIds)
                await push.SendToUserAsync(id, title, body, data);

            _logger.LogInformation("Promo {Code} broadcast to {Count} customers", code, userIds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Promo broadcast failed for {Code}", code);
        }
    }

    public async Task<(bool Success, string Message, PromoDto? Data)> UpdateAsync(int id, UpdatePromoDto dto)
    {
        var promo = await _db.PromoCodes.FindAsync(id);
        if (promo == null)
            return (false, "Promo code not found.", null);

        promo.DiscountPercent = dto.DiscountPercent;
        promo.ExpiresAt = DateTime.SpecifyKind(dto.ExpiresAt, DateTimeKind.Utc);
        promo.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();
        return (true, "Promo code updated.", Map(promo));
    }

    public async Task<(bool Success, string Message)> DeleteAsync(int id)
    {
        var promo = await _db.PromoCodes.FindAsync(id);
        if (promo == null)
            return (false, "Promo code not found.");

        _db.PromoCodes.Remove(promo);
        await _db.SaveChangesAsync();
        return (true, "Promo code deleted.");
    }

    public async Task<(bool Success, string Message, PromoValidationResult? Data)> ValidateAsync(ValidatePromoDto dto)
    {
        var code = dto.Code.Trim().ToUpper();
        var promo = await _db.PromoCodes.FirstOrDefaultAsync(p => p.Code.ToUpper() == code);

        if (promo == null || !promo.IsActive)
            return (false, "Invalid promo code.", null);
        if (promo.ExpiresAt < DateTime.UtcNow)
            return (false, "This promo code has expired.", null);

        var discountAmount = Math.Round(dto.CartSubTotal * (promo.DiscountPercent / 100m), 2);
        var newTotal = dto.CartSubTotal - discountAmount;
        if (newTotal < 0) newTotal = 0;

        return (true, "Promo code applied.", new PromoValidationResult
        {
            Valid = true,
            Code = promo.Code,
            DiscountPercent = promo.DiscountPercent,
            DiscountAmount = discountAmount,
            NewTotal = newTotal
        });
    }

    private static PromoDto Map(PromoCode p) => new()
    {
        Id = p.Id,
        Code = p.Code,
        DiscountPercent = p.DiscountPercent,
        ExpiresAt = p.ExpiresAt,
        IsActive = p.IsActive,
        IsExpired = p.ExpiresAt < DateTime.UtcNow
    };
}