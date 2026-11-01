using System.ComponentModel.DataAnnotations;

namespace sabmero.DTOs.Service;

// What the API returns for a catalog service.
public class RepairServiceDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameNe { get; set; }
    public string? Description { get; set; }
    public string? DescriptionNe { get; set; }
    public decimal Charge { get; set; }              // selling / offer price
    public decimal? OldPrice { get; set; }           // regular price (null = no discount)
    public int DiscountPercent { get; set; }         // calculated from OldPrice and Charge
    public string? ImagePath { get; set; }
    public List<string> ImagePaths { get; set; } = new();   // full gallery (first = main image)
    public bool IsActive { get; set; }
    public int? CategoryId { get; set; }
    public string? DurationText { get; set; }
    public string? Warranty { get; set; }
    public bool HomeServiceAvailable { get; set; }
    public string? Requirements { get; set; }
    public bool IsFeatured { get; set; }
    public int SortOrder { get; set; }
}

// Admin SENDS this to create a service. Upload each image first via
// POST /api/uploads/product, then send the returned paths.
public class CreateRepairServiceDto
{
    [Required(ErrorMessage = "Service name is required")]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? NameNe { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(500)]
    public string? DescriptionNe { get; set; }

    [Range(0, 100000000, ErrorMessage = "Charge cannot be negative.")]
    public decimal Charge { get; set; }

    // Regular price. When it is higher than Charge the app shows a discount.
    [Range(0, 100000000)]
    public decimal? OldPrice { get; set; }

    [MaxLength(400)]
    public string? ImagePath { get; set; }

    // Gallery images (optional). The first becomes the main image when
    // ImagePath is not sent.
    public List<string>? ImagePaths { get; set; }

    // Optional: a service category (see /api/ServiceCategories).
    public int? CategoryId { get; set; }

    [MaxLength(60)]
    public string? DurationText { get; set; }

    [MaxLength(200)]
    public string? Warranty { get; set; }

    public bool HomeServiceAvailable { get; set; } = true;

    [MaxLength(1000)]
    public string? Requirements { get; set; }

    public bool IsFeatured { get; set; }

    public int SortOrder { get; set; }
}

// Admin SENDS this to update a service. Every field added in Phase 2B is
// optional: it is only changed when it is sent, so older admin versions keep
// working. Send an empty string to clear a text field.
public class UpdateRepairServiceDto
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? NameNe { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(500)]
    public string? DescriptionNe { get; set; }

    [Range(0, 100000000)]
    public decimal Charge { get; set; }

    // Send 0 to remove the regular price (and the discount).
    [Range(0, 100000000)]
    public decimal? OldPrice { get; set; }

    [MaxLength(400)]
    public string? ImagePath { get; set; }

    public List<string>? ImagePaths { get; set; }

    public bool IsActive { get; set; } = true;

    // Optional. Leave it out to keep the current category.
    public int? CategoryId { get; set; }

    [MaxLength(60)]
    public string? DurationText { get; set; }

    [MaxLength(200)]
    public string? Warranty { get; set; }

    public bool? HomeServiceAvailable { get; set; }

    [MaxLength(1000)]
    public string? Requirements { get; set; }

    public bool? IsFeatured { get; set; }

    public int? SortOrder { get; set; }
}
