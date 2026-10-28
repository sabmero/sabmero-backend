using sabmero.Models;

namespace sabmero.Models;

// Products listed by vendors.
// SizeOptions & ColorOptions: stored as JSON strings e.g. ["S","M","L"]
// Unit: used for Grocery — "Kg" | "Ltr" | "Pkt"
public class Product
{
    public int Id { get; set; }
    public int VendorId { get; set; }       // FK → Vendors
    public int CategoryId { get; set; }     // FK → Categories
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal? OldPrice { get; set; }
    public int Stock { get; set; } = 0;
    public string? ImagePath { get; set; }          // cover image (kept for backward compat)

    // Additional gallery images, stored as a |-separated list of paths.
    // Exposed to clients as List<string> ImagePaths (cover first).
    public string? ImagePathsCsv { get; set; }
    public string? SizeOptions { get; set; }    // e.g. ["S","M","L","XL"]
    public string? ColorOptions { get; set; }   // e.g. ["Red","Blue","Black"]
    public string? Unit { get; set; }           // "Kg" | "Ltr" | "Pkt"
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Vendor Vendor { get; set; } = null!;
    public Category Category { get; set; } = null!;
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
}
