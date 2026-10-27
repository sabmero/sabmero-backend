using System.ComponentModel.DataAnnotations;

namespace sabmero.DTOs.Service;

// One category in the tree. Main categories have ParentId = null and a list of Children.
public class ServiceCategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
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

    public string? ImagePath { get; set; }

    public int? ParentId { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}
