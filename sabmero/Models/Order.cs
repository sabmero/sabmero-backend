using sabmero.Models;

namespace sabmero.Models;

// One order placed by a Customer.
// Status flow: Pending → Processing → Dispatched → Delivered | Cancelled
public class Order
{
    public int Id { get; set; }
    public int UserId { get; set; }         // FK → Users (the customer)
    public int? RiderId { get; set; }       // FK → Users (the delivery rider, assigned later)
    public decimal TotalAmount { get; set; }
    public decimal CommissionAmount { get; set; }
    public decimal DeliveryFee { get; set; } = 0;     // admin-configurable delivery charge applied at checkout
    public string PaymentMethod { get; set; } = "COD";      // "COD" | "QR"
    public string PaymentStatus { get; set; } = "Pending";  // "Pending" | "Submitted" | "Verified" | "Rejected" | "Paid"
    public string? PaymentScreenshotPath { get; set; }      // QR payment proof uploaded by the customer
    public string Status { get; set; } = "Pending";         // "Pending" | "Processing" | "Dispatched" | "Delivered" | "Cancelled"
    public string DeliveryAddress { get; set; } = string.Empty;
    public string? PromoCode { get; set; }
    public decimal Discount { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeliveredAt { get; set; }              // when the rider marked it delivered

    // Cash-on-delivery money the rider collected: "Cash" (amount in words) or
    // "Online" (screenshot of the customer's payment).
    public string? CollectionMethod { get; set; }
    public string? CollectionAmountInWords { get; set; }
    public string? CollectionScreenshotPath { get; set; }

    // Navigation
    public User User { get; set; } = null!;
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    public ReturnRequest? ReturnRequest { get; set; }
}