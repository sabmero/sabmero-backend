using Microsoft.EntityFrameworkCore;
using sabmero.Data;
using sabmero.DTOs.Service;
using sabmero.Models;

namespace sabmero.Services;

public interface IServicePackageService
{
    // Customers only ever get Active packages (in active categories). Admins can filter by status.
    Task<List<ServicePackageDto>> ListAsync(int? categoryId, string? status, bool isAdmin);

    Task<ServicePackageDto?> GetAsync(int id, bool isAdmin);

    Task<(bool Success, string Message, ServicePackageDto? Data)> CreateAsync(SaveServicePackageDto dto);

    Task<(bool Success, string Message)> UpdateAsync(int id, SaveServicePackageDto dto);

    Task<(bool Success, string Message)> DeleteAsync(int id);
}

public class ServicePackageService : IServicePackageService
{
    private static readonly string[] AllowedStatuses = { "Pending", "Active", "Rejected", "Inactive" };

    private readonly AppDbContext _db;

    public ServicePackageService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<ServicePackageDto>> ListAsync(int? categoryId, string? status, bool isAdmin)
    {
        var query = _db.ServicePackages
            .AsNoTracking()
            .Include(p => p.Items)
            .AsQueryable();

        if (categoryId.HasValue)
            query = query.Where(p => p.CategoryId == categoryId.Value);

        if (!isAdmin)
        {
            query = query.Where(p => p.Status == "Active"
                && _db.ServiceCategories.Any(c => c.Id == p.CategoryId && c.IsActive));
        }
        else if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(p => p.Status == status);
        }

        var list = await query
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Name)
            .ToListAsync();

        var names = await CategoryNamesAsync(list.Select(p => p.CategoryId).Distinct().ToList());
        return list.Select(p => Map(p, names)).ToList();
    }

    public async Task<ServicePackageDto?> GetAsync(int id, bool isAdmin)
    {
        var package = await _db.ServicePackages
            .AsNoTracking()
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (package == null)
            return null;

        if (!isAdmin)
        {
            var categoryActive = await _db.ServiceCategories
                .AnyAsync(c => c.Id == package.CategoryId && c.IsActive);
            if (package.Status != "Active" || !categoryActive)
                return null;
        }

        var names = await CategoryNamesAsync(new List<int> { package.CategoryId });
        return Map(package, names);
    }

    public async Task<(bool Success, string Message, ServicePackageDto? Data)> CreateAsync(SaveServicePackageDto dto)
    {
        var error = await ValidateAsync(dto);
        if (error != null)
            return (false, error, null);

        var package = new ServicePackage
        {
            Name = dto.Name.Trim(),
            Description = dto.Description,
            CategoryId = dto.CategoryId,
            ImagePath = dto.ImagePath,
            Price = dto.Price,
            OldPrice = dto.OldPrice,
            Status = dto.Status,
            SortOrder = dto.SortOrder,
            Items = BuildItems(dto.Items)
        };

        _db.ServicePackages.Add(package);
        await _db.SaveChangesAsync();

        var names = await CategoryNamesAsync(new List<int> { package.CategoryId });
        return (true, "Package created.", Map(package, names));
    }

    public async Task<(bool Success, string Message)> UpdateAsync(int id, SaveServicePackageDto dto)
    {
        var package = await _db.ServicePackages
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (package == null)
            return (false, "Package not found.");

        var error = await ValidateAsync(dto);
        if (error != null)
            return (false, error);

        package.Name = dto.Name.Trim();
        package.Description = dto.Description;
        package.CategoryId = dto.CategoryId;
        package.ImagePath = dto.ImagePath;
        package.Price = dto.Price;
        package.OldPrice = dto.OldPrice;
        package.Status = dto.Status;
        package.SortOrder = dto.SortOrder;

        if (dto.Status != "Rejected")
            package.RejectionReason = null;

        // replace the included items as a whole
        package.Items.Clear();
        foreach (var item in BuildItems(dto.Items))
            package.Items.Add(item);

        await _db.SaveChangesAsync();
        return (true, "Package updated.");
    }

    public async Task<(bool Success, string Message)> DeleteAsync(int id)
    {
        var package = await _db.ServicePackages
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (package == null)
            return (false, "Package not found.");

        // TODO: block deleting once bookings can reference a package (deactivate it instead).

        _db.ServicePackages.Remove(package);
        await _db.SaveChangesAsync();
        return (true, "Package deleted.");
    }

    private async Task<string?> ValidateAsync(SaveServicePackageDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return "Name is required.";

        if (!AllowedStatuses.Contains(dto.Status))
            return "Status must be Pending, Active, Rejected or Inactive.";

        if (dto.Price < 0)
            return "Price cannot be negative.";

        if (dto.OldPrice.HasValue && dto.OldPrice.Value <= dto.Price)
            return "Old price must be higher than the price.";

        var categoryExists = await _db.ServiceCategories.AnyAsync(c => c.Id == dto.CategoryId);
        if (!categoryExists)
            return "Category not found.";

        return null;
    }

    private async Task<Dictionary<int, string>> CategoryNamesAsync(List<int> ids)
    {
        return await _db.ServiceCategories
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name);
    }

    private static List<ServicePackageItem> BuildItems(List<ServicePackageItemInputDto>? items)
    {
        var result = new List<ServicePackageItem>();
        if (items == null)
            return result;

        var order = 0;
        foreach (var i in items)
        {
            if (string.IsNullOrWhiteSpace(i.Name))
                continue;

            result.Add(new ServicePackageItem
            {
                Name = i.Name.Trim(),
                Quantity = i.Quantity < 1 ? 1 : i.Quantity,
                SortOrder = i.SortOrder != 0 ? i.SortOrder : order
            });
            order++;
        }

        return result;
    }

    private static ServicePackageDto Map(ServicePackage p, Dictionary<int, string> categoryNames)
    {
        var discount = 0;
        if (p.OldPrice.HasValue && p.OldPrice.Value > p.Price && p.OldPrice.Value > 0)
            discount = (int)Math.Round((p.OldPrice.Value - p.Price) / p.OldPrice.Value * 100m);

        return new ServicePackageDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            CategoryId = p.CategoryId,
            CategoryName = categoryNames.TryGetValue(p.CategoryId, out var categoryName) ? categoryName : null,
            VendorId = p.VendorId,
            ImagePath = p.ImagePath,
            Price = p.Price,
            OldPrice = p.OldPrice,
            DiscountPercent = discount,
            Status = p.Status,
            RejectionReason = p.RejectionReason,
            SortOrder = p.SortOrder,
            CreatedAt = p.CreatedAt,
            Items = p.Items
                .OrderBy(i => i.SortOrder)
                .ThenBy(i => i.Id)
                .Select(i => new ServicePackageItemDto
                {
                    Id = i.Id,
                    Name = i.Name,
                    Quantity = i.Quantity,
                    SortOrder = i.SortOrder
                })
                .ToList()
        };
    }
}
