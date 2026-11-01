using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using sabmero.Data;
using sabmero.DTOs.Common;
using sabmero.DTOs.Product;
using sabmero.Models;

namespace sabmero.Services;

// Handles the product catalogue: public browsing + vendor management.
// Only APPROVED vendors can create products, and they can only touch their own.
public class ProductService : IProductService
{
    private readonly AppDbContext _db;
    private readonly ILogger<ProductService> _logger;

    public ProductService(AppDbContext db, ILogger<ProductService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── PUBLIC BROWSE ────────────────────────────────────────────────────────
    // Supports: text search, category filter, vendor filter, price range,
    // sorting (newest / price_asc / price_desc / rating), and pagination.
    public async Task<PagedResult<ProductDto>> BrowseAsync(
        string? search, int? categoryId, int? vendorId,
        decimal? minPrice, decimal? maxPrice,
        string? sortBy, int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        // Only show active products from approved vendors.
        var query = _db.Products
            .Where(p => p.IsActive && p.Vendor.IsApproved);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(p =>
                p.Name.ToLower().Contains(s) ||
                p.Description.ToLower().Contains(s));
        }

        if (categoryId.HasValue)
            query = query.Where(p => p.CategoryId == categoryId.Value);

        if (vendorId.HasValue)
            query = query.Where(p => p.VendorId == vendorId.Value);

        if (minPrice.HasValue)
            query = query.Where(p => p.Price >= minPrice.Value);

        if (maxPrice.HasValue)
            query = query.Where(p => p.Price <= maxPrice.Value);

        // Sorting
        query = sortBy switch
        {
            "price_asc" => query.OrderBy(p => p.Price),
            "price_desc" => query.OrderByDescending(p => p.Price),
            "rating" => query.OrderByDescending(p => p.Reviews.Average(r => (double?)r.Rating) ?? 0),
            _ => query.OrderByDescending(p => p.CreatedAt)   // "newest" (default)
        };

        var total = await query.CountAsync();

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(MapExpression)
            .ToListAsync();

        return new PagedResult<ProductDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    public async Task<ProductDto?> GetByIdAsync(int id)
        => await _db.Products.Where(p => p.Id == id).Select(MapExpression).FirstOrDefaultAsync();

    public async Task<List<ProductDto>> GetByVendorUserAsync(int userId)
    {
        var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.UserId == userId);
        if (vendor == null) return new List<ProductDto>();

        return await _db.Products
            .Where(p => p.VendorId == vendor.Id)
            .OrderByDescending(p => p.CreatedAt)
            .Select(MapExpression)
            .ToListAsync();
    }

    public async Task<(bool Success, string Message, ProductDto? Data)> CreateAsync(int userId, CreateProductDto dto)
    {
        var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.UserId == userId);
        if (vendor == null)
            return (false, "You don't have a vendor profile. Apply as a vendor first.", null);
        if (!vendor.IsApproved)
            return (false, "Your vendor account is not approved yet.", null);

        bool categoryOk = await _db.Categories.AnyAsync(c => c.Id == dto.CategoryId && c.IsActive);
        if (!categoryOk)
            return (false, "Invalid or inactive category.", null);

        var product = new Product
        {
            VendorId = vendor.Id,
            CategoryId = dto.CategoryId,
            Name = dto.Name.Trim(),
            Description = dto.Description.Trim(),
            Price = dto.Price,
            OldPrice = CleanOldPrice(dto.OldPrice, dto.Price),
            Stock = dto.Stock,
            ImagePath = ResolveCover(dto.ImagePath, dto.ImagePaths),
            ImagePathsCsv = JoinImages(dto.ImagePaths),
            SizeOptions = dto.SizeOptions,
            ColorOptions = dto.ColorOptions,
            Unit = dto.Unit,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Product {Id} created by vendor {VendorId}", product.Id, vendor.Id);

        return (true, "Product created.", await GetByIdAsync(product.Id));
    }

    public async Task<(bool Success, string Message, ProductDto? Data)> UpdateAsync(int userId, string role, int productId, UpdateProductDto dto)
    {
        var product = await _db.Products.FindAsync(productId);
        if (product == null)
            return (false, "Product not found.", null);

        // Ownership check: vendor can only edit their own products (admin can edit any).
        if (role != "Admin")
        {
            var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.UserId == userId);
            if (vendor == null || product.VendorId != vendor.Id)
                return (false, "You can only edit your own products.", null);
        }

        bool categoryOk = await _db.Categories.AnyAsync(c => c.Id == dto.CategoryId);
        if (!categoryOk)
            return (false, "Invalid category.", null);

        product.Name = dto.Name.Trim();
        product.Description = dto.Description.Trim();
        product.Price = dto.Price;
        product.OldPrice = CleanOldPrice(dto.OldPrice, dto.Price);
        product.Stock = dto.Stock;
        product.CategoryId = dto.CategoryId;
        product.ImagePath = ResolveCover(dto.ImagePath, dto.ImagePaths);
        product.ImagePathsCsv = JoinImages(dto.ImagePaths);
        product.SizeOptions = dto.SizeOptions;
        product.ColorOptions = dto.ColorOptions;
        product.Unit = dto.Unit;
        product.IsActive = dto.IsActive;

        await _db.SaveChangesAsync();
        return (true, "Product updated.", await GetByIdAsync(product.Id));
    }

    public async Task<(bool Success, string Message)> DeleteAsync(int userId, string role, int productId)
    {
        var product = await _db.Products.FindAsync(productId);
        if (product == null)
            return (false, "Product not found.");

        if (role != "Admin")
        {
            var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.UserId == userId);
            if (vendor == null || product.VendorId != vendor.Id)
                return (false, "You can only delete your own products.");
        }

        // Soft delete so existing order history keeps working.
        product.IsActive = false;
        await _db.SaveChangesAsync();
        return (true, "Product removed.");
    }

    // EF-translatable projection used by all read methods.
    // Defined as an Expression (not a method) so EF Core can turn it into SQL.
    private static readonly Expression<Func<Product, ProductDto>> MapExpression = p => new ProductDto
    {
        Id = p.Id,
        VendorId = p.VendorId,
        VendorName = p.Vendor.BusinessName,
        CategoryId = p.CategoryId,
        CategoryName = p.Category.Name,
        Name = p.Name,
        Description = p.Description,
        Price = p.Price,
        OldPrice = p.OldPrice,
        Stock = p.Stock,
        ImagePath = p.ImagePath,
        ImagePathsCsv_Internal = p.ImagePathsCsv,
        SizeOptions = p.SizeOptions,
        ColorOptions = p.ColorOptions,
        Unit = p.Unit,
        IsActive = p.IsActive,
        AverageRating = p.Reviews.Average(r => (double?)r.Rating) ?? 0,
        ReviewCount = p.Reviews.Count,
        CreatedAt = p.CreatedAt
    };

    // An old price only makes sense when it is higher than the selling price.
    private static decimal? CleanOldPrice(decimal? oldPrice, decimal price)
        => oldPrice.HasValue && oldPrice.Value > price ? oldPrice : null;

    // ── Multi-image helpers ──────────────────────────────────────────────────
    // Cover = explicit ImagePath if given, else first of ImagePaths.
    private static string? ResolveCover(string? imagePath, List<string>? imagePaths)
        => !string.IsNullOrWhiteSpace(imagePath)
            ? imagePath
            : imagePaths?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

    private static string? JoinImages(List<string>? imagePaths)
    {
        var clean = imagePaths?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        return clean == null || clean.Count == 0 ? null : string.Join("|", clean);
    }
}