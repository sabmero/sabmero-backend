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
        return await q.OrderBy(r => r.Name).Select(r => Map(r)).ToListAsync();
    }

    public async Task<(bool Success, string Message, RepairServiceDto? Data)> CreateAsync(CreateRepairServiceDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return (false, "Service name is required.", null);

        if (dto.CategoryId.HasValue
            && !await _db.ServiceCategories.AnyAsync(c => c.Id == dto.CategoryId.Value))
            return (false, "Category not found.", null);

        var svc = new RepairService
        {
            Name = dto.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
            Charge = dto.Charge < 0 ? 0 : dto.Charge,
            ImagePath = dto.ImagePath,
            CategoryId = dto.CategoryId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
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

        svc.Name = dto.Name.Trim();
        svc.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        svc.Charge = dto.Charge < 0 ? 0 : dto.Charge;
        svc.ImagePath = dto.ImagePath;
        svc.IsActive = dto.IsActive;

        // only change the category when one is sent, so older clients keep it
        if (dto.CategoryId.HasValue)
            svc.CategoryId = dto.CategoryId;

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

    private static RepairServiceDto Map(RepairService r) => new RepairServiceDto
    {
        Id = r.Id,
        Name = r.Name,
        Description = r.Description,
        Charge = r.Charge,
        ImagePath = r.ImagePath,
        IsActive = r.IsActive,
        CategoryId = r.CategoryId
    };
}
