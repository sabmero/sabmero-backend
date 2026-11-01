using Microsoft.EntityFrameworkCore;
using sabmero.Data;
using sabmero.DTOs.Service;
using sabmero.Models;

namespace sabmero.Services;

public class RepairServiceCatalog : IRepairServiceCatalog
{
    private readonly AppDbContext _db;
    private readonly ILogger<RepairServiceCatalog> _logger;

    public RepairServiceCatalog(AppDbContext db, ILogger<RepairServiceCatalog> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<RepairServiceDto>> GetAllAsync(bool includeInactive)
    {
        var q = _db.RepairServices.AsNoTracking().AsQueryable();
        if (!includeInactive) q = q.Where(r => r.IsActive);

        // Featured first, then the admin's display order, then by name.
        var rows = await q
            .OrderByDescending(r => r.IsFeatured)
            .ThenBy(r => r.SortOrder)
            .ThenBy(r => r.Name)
            .ToListAsync();
        return rows.Select(Map).ToList();
    }

    public async Task<(bool Success, string Message, RepairServiceDto? Data)> CreateAsync(CreateRepairServiceDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return (false, "Service name is required.", null);

        if (dto.CategoryId.HasValue
            && !await _db.ServiceCategories.AnyAsync(c => c.Id == dto.CategoryId.Value))
            return (false, "Category not found.", null);

        var charge = dto.Charge < 0 ? 0 : dto.Charge;
        var priceError = CheckPrices(charge, dto.OldPrice);
        if (priceError != null)
            return (false, priceError, null);

        var svc = new RepairService
        {
            Name = dto.Name.Trim(),
            NameNe = Clean(dto.NameNe),
            Description = Clean(dto.Description),
            DescriptionNe = Clean(dto.DescriptionNe),
            Charge = charge,
            OldPrice = dto.OldPrice.HasValue && dto.OldPrice.Value > 0 ? dto.OldPrice : null,
            ImagePath = dto.ImagePath,
            CategoryId = dto.CategoryId,
            DurationText = Clean(dto.DurationText),
            Warranty = Clean(dto.Warranty),
            HomeServiceAvailable = dto.HomeServiceAvailable,
            Requirements = Clean(dto.Requirements),
            IsFeatured = dto.IsFeatured,
            SortOrder = dto.SortOrder,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        ApplyGallery(svc, dto.ImagePaths, dto.ImagePath);

        _db.RepairServices.Add(svc);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Repair service {Id} '{Name}' created", svc.Id, svc.Name);
        return (true, "Service created.", Map(svc));
    }

    public async Task<(bool Success, string Message)> UpdateAsync(int id, UpdateRepairServiceDto dto)
    {
        var svc = await _db.RepairServices.FindAsync(id);
        if (svc == null) return (false, "Service not found.");

        if (dto.CategoryId.HasValue
            && !await _db.ServiceCategories.AnyAsync(c => c.Id == dto.CategoryId.Value))
            return (false, "Category not found.");

        var charge = dto.Charge < 0 ? 0 : dto.Charge;
        var newOld = dto.OldPrice.HasValue ? (dto.OldPrice.Value > 0 ? dto.OldPrice : null) : svc.OldPrice;
        var priceError = CheckPrices(charge, newOld);
        if (priceError != null)
            return (false, priceError);

        svc.Name = dto.Name.Trim();
        svc.Description = Clean(dto.Description);
        svc.Charge = charge;
        svc.OldPrice = newOld;
        svc.ImagePath = dto.ImagePath;
        svc.IsActive = dto.IsActive;

        // only change the category when one is sent, so older clients keep it
        if (dto.CategoryId.HasValue)
            svc.CategoryId = dto.CategoryId;

        // Phase 2B fields: changed only when sent.
        if (dto.NameNe != null) svc.NameNe = Clean(dto.NameNe);
        if (dto.DescriptionNe != null) svc.DescriptionNe = Clean(dto.DescriptionNe);
        if (dto.DurationText != null) svc.DurationText = Clean(dto.DurationText);
        if (dto.Warranty != null) svc.Warranty = Clean(dto.Warranty);
        if (dto.Requirements != null) svc.Requirements = Clean(dto.Requirements);
        if (dto.HomeServiceAvailable.HasValue) svc.HomeServiceAvailable = dto.HomeServiceAvailable.Value;
        if (dto.IsFeatured.HasValue) svc.IsFeatured = dto.IsFeatured.Value;
        if (dto.SortOrder.HasValue) svc.SortOrder = dto.SortOrder.Value;
        if (dto.ImagePaths != null) ApplyGallery(svc, dto.ImagePaths, dto.ImagePath);

        await _db.SaveChangesAsync();
        return (true, "Service updated.");
    }

    public async Task<(bool Success, string Message)> DeleteAsync(int id)
    {
        var svc = await _db.RepairServices.FindAsync(id);
        if (svc == null) return (false, "Service not found.");
        _db.RepairServices.Remove(svc);
        await _db.SaveChangesAsync();
        return (true, "Service deleted.");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // The regular price must be higher than the selling price, otherwise there
    // is no discount to show.
    private static string? CheckPrices(decimal charge, decimal? oldPrice)
    {
        if (oldPrice.HasValue && oldPrice.Value > 0 && charge > 0 && oldPrice.Value <= charge)
            return "Regular price must be higher than the offer price.";
        return null;
    }

    // Stores the gallery as a comma-separated list and keeps the first image in
    // ImagePath so older clients still see a main image.
    private static void ApplyGallery(RepairService svc, List<string>? paths, string? mainImage)
    {
        if (paths == null) return;

        var list = paths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct()
            .ToList();

        svc.ImagePathsCsv = list.Count == 0 ? null : string.Join(",", list);
        if (string.IsNullOrWhiteSpace(mainImage))
            svc.ImagePath = list.Count == 0 ? null : list[0];
    }

    private static List<string> SplitGallery(string? csv, string? mainImage)
    {
        var list = new List<string>();
        if (!string.IsNullOrWhiteSpace(csv))
            list.AddRange(csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (!string.IsNullOrWhiteSpace(mainImage) && !list.Contains(mainImage))
            list.Insert(0, mainImage);
        return list;
    }

    private static int Discount(decimal charge, decimal? oldPrice)
    {
        if (!oldPrice.HasValue || oldPrice.Value <= 0 || oldPrice.Value <= charge) return 0;
        return (int)Math.Round((double)((oldPrice.Value - charge) / oldPrice.Value * 100m));
    }

    private static RepairServiceDto Map(RepairService r) => new RepairServiceDto
    {
        Id = r.Id,
        Name = r.Name,
        NameNe = r.NameNe,
        Description = r.Description,
        DescriptionNe = r.DescriptionNe,
        Charge = r.Charge,
        OldPrice = r.OldPrice,
        DiscountPercent = Discount(r.Charge, r.OldPrice),
        ImagePath = r.ImagePath,
        ImagePaths = SplitGallery(r.ImagePathsCsv, r.ImagePath),
        IsActive = r.IsActive,
        CategoryId = r.CategoryId,
        DurationText = r.DurationText,
        Warranty = r.Warranty,
        HomeServiceAvailable = r.HomeServiceAvailable,
        Requirements = r.Requirements,
        IsFeatured = r.IsFeatured,
        SortOrder = r.SortOrder
    };
}
