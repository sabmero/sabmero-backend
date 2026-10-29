namespace sabmero.Models;

// A repair/installation service the admin offers. Shown to customers in the
// app's Repair section with its charge and image. The booking's ServiceType
// stores the chosen service's Name.
public class RepairService
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Charge { get; set; }              // displayed base price / charge
    public string? ImagePath { get; set; }           // uploaded image (POST /api/uploads/product)
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Optional: which service category (main or sub) this service sits under.
    public int? CategoryId { get; set; }
}
