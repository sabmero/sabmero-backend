using System.ComponentModel.DataAnnotations;

namespace sabmero.DTOs.Order;

// ── One line the customer wants to buy (sent inside PlaceOrderDto) ──
public class OrderItemInputDto
{
    [Required]
    public int ProductId { get; set; }

    [Range(1, 1000, ErrorMessage = "Quantity must be at least 1")]
    public int Quantity { get; set; }

    public string? SelectedSize { get; set; }
    public string? SelectedColor { get; set; }
}

// ── Optional installation request sent with an order ──
// When the customer buys an electrical item (AC, CCTV, TV, Fan, etc.) they can
// also book installation at checkout. If BookInstallation is true, the order
// creates a linked Installation booking that appears in the Repair section.
public class InstallationRequestDto
{
    [Required(ErrorMessage = "Installation date is required")]
    public DateTime BookingDate { get; set; }

    [Required(ErrorMessage = "Time slot is required")]
    public string TimeSlot { get; set; } = string.Empty;   // e.g. "10:00 AM - 12:00 PM"

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    // If omitted, the order's DeliveryAddress is used for the installation.
    [MaxLength(400)]
    public string? ServiceAddress { get; set; }
}

// ── What Flutter SENDS to place an order (the whole cart at once) ──
public class PlaceOrderDto
{
    [Required(ErrorMessage = "Delivery address is required")]
    [MaxLength(400)]
    public string DeliveryAddress { get; set; } = string.Empty;

    // "COD" | "QR"
    public string PaymentMethod { get; set; } = "COD";

    // Server path of the payment screenshot (from POST /api/Uploads/payment).
    // Sent by the app for QR payments; null for COD.
    public string? PaymentScreenshotPath { get; set; }

    // Optional promo code typed at checkout
    public string? PromoCode { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "Order must contain at least one item")]
    public List<OrderItemInputDto> Items { get; set; } = new();

    // ── Product + Installation linkage ───────────────────────────────────────
    // Set true to also book on-site installation for this order. When true,
    // Installation details must be provided. The booking lands in the Repair
    // section linked to this order (RelatedOrderId).
    public bool BookInstallation { get; set; } = false;
    public InstallationRequestDto? Installation { get; set; }
}

// ── One line in the API response ──
public class OrderItemDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? ProductImage { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }       // UnitPrice * Quantity
    public string? SelectedSize { get; set; }
    public string? SelectedColor { get; set; }
}

// ── The full order the API SENDS BACK ──
public class OrderDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public string? CustomerEmail { get; set; }
    public int? RiderId { get; set; }
    public string? RiderName { get; set; }
    public decimal SubTotal { get; set; }        // sum of items before discount
    public decimal Discount { get; set; }
    public decimal DeliveryFee { get; set; }     // admin-configurable delivery charge
    public decimal TotalAmount { get; set; }     // final amount payable (subtotal - discount + delivery)
    public decimal CommissionAmount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string? PaymentScreenshotPath { get; set; }
    public string Status { get; set; } = string.Empty;
    public string DeliveryAddress { get; set; } = string.Empty;
    public string? PromoCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();

    // The linked installation booking created at checkout, if any.
    public int? InstallationBookingId { get; set; }
}

// ── Admin/rider SENDS this to change an order's status ──
public class UpdateOrderStatusDto
{
    [Required]
    // "Pending" | "Processing" | "Dispatched" | "Delivered" | "Cancelled"
    public string Status { get; set; } = string.Empty;

    // Rider marking a cash-on-delivery order as Delivered: how the money was
    // collected. "Cash" (write the amount in words) or "Online" (screenshot).
    [MaxLength(10)]
    public string? CollectionMethod { get; set; }

    [MaxLength(200)]
    public string? AmountInWords { get; set; }

    [MaxLength(400)]
    public string? CollectionScreenshotPath { get; set; }
}

// ── Admin SENDS this to assign a delivery rider ──
public class AssignRiderDto
{
    [Required]
    public int RiderId { get; set; }
}
