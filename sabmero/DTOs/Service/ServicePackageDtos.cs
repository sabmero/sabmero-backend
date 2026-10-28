using System.ComponentModel.DataAnnotations;

namespace sabmero.DTOs.Service;

public class ServicePackageItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int SortOrder { get; set; }
}

public class ServicePackageDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public int? VendorId { get; set; }
    public string? ImagePath { get; set; }
    public decimal Price { get; set; }
    public decimal? OldPrice { get; set; }

    // Worked out from Price and OldPrice, 0 when there is no discount.
    public int DiscountPercent { get; set; }

    public string Status { get; set; } = string.Empty;
    public string? RejectionReason { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<ServicePackageItemDto> Items { get; set; } = new();
}

public class ServicePackageItemInputDto
{
    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Range(1, 1000)]
    public int Quantity { get; set; } = 1;

    public int SortOrder { get; set; }
}

// Used for both create and update (the item list is replaced as a whole on update).
public class SaveServicePackageDto
{
    [Required, StringLength(150)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Range(1, int.MaxValue)]
    public int CategoryId { get; set; }

    public string? ImagePath { get; set; }

    public decimal Price { get; set; }

    public decimal? OldPrice { get; set; }

    // Pending, Active, Rejected or Inactive. Packages made by an admin are normally Active.
    public string Status { get; set; } = "Active";

    public int SortOrder { get; set; }

    public List<ServicePackageItemInputDto> Items { get; set; } = new();
}
