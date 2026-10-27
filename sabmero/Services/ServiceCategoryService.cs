using Microsoft.EntityFrameworkCore;
using sabmero.Data;
using sabmero.DTOs.Service;
using sabmero.Models;

namespace sabmero.Services;

public class ServiceCategoryService : IServiceCategoryService
{
    private readonly AppDbContext _db;

    public ServiceCategoryService(AppDbContext db)
    {
        _db = db;
    }

    // Main categories with their sub-categories. Customers only get active ones.
    public async Task<List<ServiceCategoryDto>> GetTreeAsync(bool includeInactive)
    {
        var query = _db.ServiceCategories.AsNoTracking();
        if (!includeInactive)
            query = query.Where(c => c.IsActive);

        var all = await query
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();

        var dtos = all.Select(c => Map(c)).ToList();
        var byId = dtos.ToDictionary(d => d.Id);

        var roots = new List<ServiceCategoryDto>();
        foreach (var dto in dtos)
        {
            if (!dto.ParentId.HasValue)
                roots.Add(dto);
            else if (byId.TryGetValue(dto.ParentId.Value, out var parent))
                parent.Children.Add(dto);
            // a sub-category whose main category is hidden (inactive) stays hidden too
        }

        return roots;
    }

    public async Task<(bool Success, string Message, ServiceCategoryDto? Data)> CreateAsync(CreateServiceCategoryDto dto)
    {
        var name = dto.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return (false, "Name is required.", null);

        if (dto.ParentId.HasValue)
        {
            var parent = await _db.ServiceCategories.FirstOrDefaultAsync(c => c.Id == dto.ParentId.Value);
            if (parent == null)
                return (false, "Main category not found.", null);
            if (parent.ParentId.HasValue)
                return (false, "Only two levels are allowed (main category and sub-category).", null);
        }

        var lower = name.ToLower();
        var exists = await _db.ServiceCategories
            .AnyAsync(c => c.ParentId == dto.ParentId && c.Name.ToLower() == lower);
        if (exists)
            return (false, "A category with this name already exists here.", null);

        var category = new ServiceCategory
        {
            Name = name,
            ImagePath = dto.ImagePath,
            ParentId = dto.ParentId,
            SortOrder = dto.SortOrder,
            IsActive = dto.IsActive
        };

        _db.ServiceCategories.Add(category);
        await _db.SaveChangesAsync();
        return (true, "Category created.", Map(category));
    }

    public async Task<(bool Success, string Message)> UpdateAsync(int id, UpdateServiceCategoryDto dto)
    {
        var category = await _db.ServiceCategories.FirstOrDefaultAsync(c => c.Id == id);
        if (category == null)
            return (false, "Category not found.");

        var name = dto.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return (false, "Name is required.");

        if (dto.ParentId.HasValue)
        {
            if (dto.ParentId.Value == id)
                return (false, "A category cannot be its own parent.");

            var parent = await _db.ServiceCategories.FirstOrDefaultAsync(c => c.Id == dto.ParentId.Value);
            if (parent == null)
                return (false, "Main category not found.");
            if (parent.ParentId.HasValue)
                return (false, "Only two levels are allowed (main category and sub-category).");

            var hasChildren = await _db.ServiceCategories.AnyAsync(c => c.ParentId == id);
            if (hasChildren)
                return (false, "This category has sub-categories, so it cannot become a sub-category itself.");
        }

        var lower = name.ToLower();
        var duplicate = await _db.ServiceCategories
            .AnyAsync(c => c.Id != id && c.ParentId == dto.ParentId && c.Name.ToLower() == lower);
        if (duplicate)
            return (false, "A category with this name already exists here.");

        category.Name = name;
        category.ImagePath = dto.ImagePath;
        category.ParentId = dto.ParentId;
        category.SortOrder = dto.SortOrder;
        category.IsActive = dto.IsActive;

        await _db.SaveChangesAsync();
        return (true, "Category updated.");
    }

    public async Task<(bool Success, string Message)> DeleteAsync(int id)
    {
        var category = await _db.ServiceCategories.FirstOrDefaultAsync(c => c.Id == id);
        if (category == null)
            return (false, "Category not found.");

        var hasChildren = await _db.ServiceCategories.AnyAsync(c => c.ParentId == id);
        if (hasChildren)
            return (false, "Delete or move its sub-categories first, or deactivate it instead.");

        // TODO: also block deleting when repair services use this category
        // (after RepairService.CategoryId is added).

        _db.ServiceCategories.Remove(category);
        await _db.SaveChangesAsync();
        return (true, "Category deleted.");
    }

    private static ServiceCategoryDto Map(ServiceCategory c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        ImagePath = c.ImagePath,
        ParentId = c.ParentId,
        SortOrder = c.SortOrder,
        IsActive = c.IsActive
    };
}
