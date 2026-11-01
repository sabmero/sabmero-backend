namespace sabmero.Models;

// A repair/installation service the admin offers. Shown to customers in the
// app's Services section with its price and images. The booking's ServiceType
// stores the chosen service's Name and its ServiceId links back to this row.
public class RepairService
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameNe { get; set; }              // Nepali name (optional)
    public string? Description { get; set; }
    public string? DescriptionNe { get; set; }       // Nepali description (optional)
    public decimal Charge { get; set; }              // selling / offer price (NPR)
    public decimal? OldPrice { get; set; }           // regular price; when higher than Charge a discount is shown
    public string? ImagePath { get; set; }          // main image (POST /api/uploads/product)
    public string? ImagePathsCsv { get; set; }      // gallery: all images, comma-separated
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Optional: which service category (main or sub) this service sits under.
    public int? CategoryId { get; set; }

    public string? DurationText { get; set; }        // e.g. "2-3 hours"
    public string? Warranty { get; set; }            // warranty / service guarantee text
    public bool HomeServiceAvailable { get; set; } = true;
    public string? Requirements { get; set; }        // requirements and notes for the customer
    public bool IsFeatured { get; set; }
    public int SortOrder { get; set; }
}
