using System.ComponentModel.DataAnnotations;

namespace sabmero.DTOs.Service;

// What the API returns for a catalog service.
public class RepairServiceDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Charge { get; set; }
    public string? ImagePath { get; set; }
    public bool IsActive { get; set; }
    public int? CategoryId { get; set; }
}

// Admin SENDS this to create a service. Upload the image first via
// POST /api/uploads/product, then send the returned path as ImagePath.
public class CreateRepairServiceDto
{
    [Required(ErrorMessage = "Service name is required")]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [Range(0, 100000000, ErrorMessage = "Charge cannot be negative.")]
    public decimal Charge { get; set; }

    [MaxLength(400)]
    public string? ImagePath { get; set; }

    // Optional: a service category (see /api/ServiceCategories).
    public int? CategoryId { get; set; }
}

// Admin SENDS this to update a service.
public class UpdateRepairServiceDto
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [Range(0, 100000000)]
    public decimal Charge { get; set; }

    [MaxLength(400)]
    public string? ImagePath { get; set; }

    public bool IsActive { get; set; } = true;

    // Optional. Leave it out to keep the current category.
    public int? CategoryId { get; set; }
}
