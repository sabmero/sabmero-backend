using System.ComponentModel.DataAnnotations;

namespace sabmero.DTOs.Product;

// ── What a Vendor SENDS to create a product ──
public class CreateProductDto
{
    [Required(ErrorMessage = "Product name is required")]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Description { get; set; } = string.Empty;

    [Required]
    [Range(0.01, 10000000, ErrorMessage = "Price must be greater than 0")]
    public decimal Price { get; set; }

    [Range(0.01, 10000000, ErrorMessage = "Old price must be greater than 0")]
    public decimal? OldPrice { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Stock cannot be negative")]
    public int Stock { get; set; }

    [Required(ErrorMessage = "Category is required")]
    public int CategoryId { get; set; }

    public string? ImagePath { get; set; }

    // Multiple images: first one becomes the cover (ImagePath). Upload each
    // file via POST /api/Uploads/product first, then send the returned paths.
    public List<string>? ImagePaths { get; set; }

    // For clothing/footwear — JSON arrays as strings e.g. ["S","M","L"]
    public string? SizeOptions { get; set; }
    public string? ColorOptions { get; set; }

    // For grocery — "Kg" | "Ltr" | "Pkt"
    public string? Unit { get; set; }
}

// ── What a Vendor SENDS to update a product ──
public class UpdateProductDto
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Description { get; set; } = string.Empty;

    [Range(0.01, 10000000)]
    public decimal Price { get; set; }

    [Range(0.01, 10000000)]
    public decimal? OldPrice { get; set; }

    [Range(0, int.MaxValue)]
    public int Stock { get; set; }

    public int CategoryId { get; set; }
    public string? ImagePath { get; set; }
    public List<string>? ImagePaths { get; set; }
    public string? SizeOptions { get; set; }
    public string? ColorOptions { get; set; }
    public string? Unit { get; set; }
    public bool IsActive { get; set; } = true;
}

// ── What the API SENDS BACK for any product ──
public class ProductDto
{
    public int Id { get; set; }
    public int VendorId { get; set; }
    public string VendorName { get; set; } = string.Empty;   // business name
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal? OldPrice { get; set; }

    // Worked out from Price and OldPrice. 0 when there is no discount.
    public int DiscountPercent =>
        OldPrice.HasValue && OldPrice.Value > Price
            ? (int)Math.Round((OldPrice.Value - Price) / OldPrice.Value * 100m)
            : 0;

    public decimal SavingsAmount =>
        OldPrice.HasValue && OldPrice.Value > Price ? OldPrice.Value - Price : 0m;

    public int Stock { get; set; }
    public string? ImagePath { get; set; }

    // Raw |-separated extra image paths from the DB. Filled by the EF
    // projection; not serialized to clients (they get ImagePaths instead).
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ImagePathsCsv_Internal { get; set; }

    // All images with the cover first, duplicates removed. Always non-null;
    // falls back to [ImagePath] for products created before multi-image.
    public List<string> ImagePaths
    {
        get
        {
            var list = new List<string>();
            if (!string.IsNullOrWhiteSpace(ImagePath)) list.Add(ImagePath!);
            if (!string.IsNullOrWhiteSpace(ImagePathsCsv_Internal))
                foreach (var pth in ImagePathsCsv_Internal!.Split('|', StringSplitOptions.RemoveEmptyEntries))
                    if (!list.Contains(pth)) list.Add(pth);
            return list;
        }
    }
    public string? SizeOptions { get; set; }
    public string? ColorOptions { get; set; }
    public string? Unit { get; set; }
    public bool IsActive { get; set; }
    public double AverageRating { get; set; }   // average of all reviews
    public int ReviewCount { get; set; }
    public DateTime CreatedAt { get; set; }
}
