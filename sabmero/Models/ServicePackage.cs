namespace sabmero.Models;

// A bookable bundle of a service, e.g. "4 camera CCTV package".
// Created by an admin (VendorId = null) or later by a vendor (needs admin approval).
public class ServicePackage
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int CategoryId { get; set; }
    public int? VendorId { get; set; }
    public string? ImagePath { get; set; }
    public decimal Price { get; set; }
    public decimal? OldPrice { get; set; }

    // Pending, Active, Rejected or Inactive. Customers only see Active.
    public string Status { get; set; } = "Active";

    public string? RejectionReason { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<ServicePackageItem> Items { get; set; } = new();
}

// One line of what the package includes, e.g. "CCTV Camera" x 4.
public class ServicePackageItem
{
    public int Id { get; set; }
    public int ServicePackageId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public int SortOrder { get; set; }
}
