using System.ComponentModel.DataAnnotations;

namespace sabmero.DTOs.Service;

// One category in the tree. Main categories have ParentId = null and a list of Children.
public class ServiceCategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameNe { get; set; }
    public string? Description { get; set; }
    public string? DescriptionNe { get; set; }
    public string? ImagePath { get; set; }
    public int? ParentId { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public List<ServiceCategoryDto> Children { get; set; } = new();
}

public class CreateServiceCategoryDto
{
    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(150)]
    public string? NameNe { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    [StringLength(500)]
    public string? DescriptionNe { get; set; }

    public string? ImagePath { get; set; }

    // null = main category, otherwise the Id of a main category
    public int? ParentId { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

public class UpdateServiceCategoryDto
{
    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    // The three fields below are only changed when they are sent (so older
    // admin versions that do not know them keep the stored values).
    // Send an empty string to clear one.
    [StringLength(150)]
    public string? NameNe { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    [StringLength(500)]
    public string? DescriptionNe { get; set; }

    public string? ImagePath { get; set; }

    public int? ParentId { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}
