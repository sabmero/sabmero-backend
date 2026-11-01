namespace sabmero.Models;

public class ServiceCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameNe { get; set; }              // Nepali name (optional)
    public string? Description { get; set; }
    public string? DescriptionNe { get; set; }       // Nepali description (optional)
    public string? ImagePath { get; set; }
    public int? ParentId { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
