using Microsoft.EntityFrameworkCore;
using sabmero.Data;
using sabmero.DTOs.Order;
using sabmero.Models;

namespace sabmero.Services;

// The heart of the shop. PlaceOrderAsync does a lot:
//  1. Validates every product (exists, active, enough stock)
//  2. Locks in the price at order time
//  3. Applies a promo code if valid
//  4. Calculates platform commission from each vendor's rate
//  5. Deducts stock and saves everything in one transaction
//  6. Optionally creates a linked Installation booking (Repair section)
public class OrderService : IOrderService
{
    private readonly AppDbContext _db;
    private readonly ILogger<OrderService> _logger;
    private readonly IPushService _push;

    public OrderService(AppDbContext db, ILogger<OrderService> logger, IPushService push)
    {
        _db = db;
        _logger = logger;
        _push = push;
    }

    public async Task<(bool Success, string Message, OrderDto? Data)> PlaceOrderAsync(int userId, PlaceOrderDto dto)
    {
        if (dto.Items == null || dto.Items.Count == 0)
            return (false, "Your cart is empty.", null);

        // If installation is requested, validate its details up front.
        if (dto.BookInstallation)
        {
            if (dto.Installation == null)
                return (false, "Installation details are required when booking installation.", null);
            if (string.IsNullOrWhiteSpace(dto.Installation.TimeSlot))
                return (false, "Installation time slot is required.", null);
            if (dto.Installation.BookingDate.Date < DateTime.UtcNow.Date)
                return (false, "Installation date cannot be in the past.", null);
        }

        // Load all referenced products in one query
        var productIds = dto.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _db.Products
            .Include(p => p.Vendor)
            .Where(p => productIds.Contains(p.Id))
            .ToListAsync();

        // Use a DB transaction so stock + order (+ installation) are saved atomically.
        await using var tx = await _db.Database.BeginTransactionAsync();

        decimal subTotal = 0m;
        decimal commissionTotal = 0m;
        var orderItems = new List<OrderItem>();

        foreach (var item in dto.Items)
        {
            var product = products.FirstOrDefault(p => p.Id == item.ProductId);
            if (product == null)
                return (false, $"Product #{item.ProductId} no longer exists.", null);
            if (!product.IsActive || product.Vendor == null || !product.Vendor.IsApproved)
                return (false, $"'{product.Name}' is not available.", null);
            if (item.Quantity < 1)
                return (false, $"Invalid quantity for '{product.Name}'.", null);
            if (product.Stock < item.Quantity)
                return (false, $"Not enough stock for '{product.Name}'. Only {product.Stock} left.", null);

            var lineTotal = product.Price * item.Quantity;
            subTotal += lineTotal;

            // Commission the platform earns from this vendor for this line.
            commissionTotal += lineTotal * (product.Vendor.CommissionRate / 100m);

            // Deduct stock
            product.Stock -= item.Quantity;

            orderItems.Add(new OrderItem
            {
                ProductId = product.Id,
                Quantity = item.Quantity,
                UnitPrice = product.Price,
                SelectedSize = item.SelectedSize,
                SelectedColor = item.SelectedColor
            });
        }

        // ── Promo code ──
        decimal discount = 0m;
        string? appliedPromo = null;
        if (!string.IsNullOrWhiteSpace(dto.PromoCode))
        {
            var code = dto.PromoCode.Trim().ToUpper();
            var promo = await _db.PromoCodes
                .FirstOrDefaultAsync(p => p.Code.ToUpper() == code && p.IsActive);

            if (promo == null)
                return (false, "Invalid promo code.", null);
            if (promo.ExpiresAt < DateTime.UtcNow)
                return (false, "This promo code has expired.", null);

            discount = Math.Round(subTotal * (promo.DiscountPercent / 100m), 2);
            appliedPromo = promo.Code;
        }

        var total = subTotal - discount;
        if (total < 0) total = 0;

        // ── Delivery fee (admin-configurable, applied to the payable total) ──
        var deliveryFee = await GetEffectiveDeliveryFeeAsync(subTotal);
        total += deliveryFee;

        // QR orders that arrive WITH a screenshot go straight to "Submitted"
        // so they appear in the vendor/admin "awaiting verification" list.
        // (Previously the screenshot from the app was silently dropped here
        // and the status stayed "Pending" — that was the bug.)
        var isQr = dto.PaymentMethod == "QR";
        var hasScreenshot = isQr && !string.IsNullOrWhiteSpace(dto.PaymentScreenshotPath);

        var order = new Order
        {
            UserId = userId,
            TotalAmount = total,
            CommissionAmount = Math.Round(commissionTotal, 2),
            DeliveryFee = deliveryFee,
            PaymentMethod = isQr ? "QR" : "COD",
            PaymentScreenshotPath = hasScreenshot ? dto.PaymentScreenshotPath : null,
            PaymentStatus = hasScreenshot ? "Submitted" : "Pending",
            Status = "Pending",
            DeliveryAddress = dto.DeliveryAddress.Trim(),
            PromoCode = appliedPromo,
            Discount = discount,
            CreatedAt = DateTime.UtcNow,
            OrderItems = orderItems
        };

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        // ── Optional installation booking (lands in the Repair section) ──
        // Vendors sell the product; the on-site installation is our technical
        // work, so it is created as a ServiceBooking linked to this order.
        int? installationBookingId = null;
        if (dto.BookInstallation && dto.Installation != null)
        {
            var inst = dto.Installation;
            var booking = new ServiceBooking
            {
                UserId = userId,
                ServiceType = "Installation",
                BookingDate = DateTime.SpecifyKind(inst.BookingDate, DateTimeKind.Utc),
                TimeSlot = inst.TimeSlot.Trim(),
                Latitude = inst.Latitude,
                Longitude = inst.Longitude,
                ServiceAddress = string.IsNullOrWhiteSpace(inst.ServiceAddress)
                    ? order.DeliveryAddress
                    : inst.ServiceAddress!.Trim(),
                Status = "Pending",
                PaymentMethod = "Cash",     // installation charge settled on-site by default
                PaymentStatus = "Pending",
                RelatedOrderId = order.Id,
                CreatedAt = DateTime.UtcNow
            };
            _db.ServiceBookings.Add(booking);
            await _db.SaveChangesAsync();
            installationBookingId = booking.Id;
            _logger.LogInformation("Installation booking {BId} created for order {OId}", booking.Id, order.Id);
        }

        await tx.CommitAsync();

        _logger.LogInformation("Order {Id} placed by user {UserId}, total {Total}", order.Id, userId, total);

        // Push: customer ("Order Placed"), each vendor whose product is in the
        // order, and every admin ("New Order"). Never breaks the order flow.
        await NotifyOrderPlacedAsync(order);

        var dtoResult = await BuildOrderDtoAsync(order.Id);
        return (true, dto.BookInstallation
            ? "Order placed and installation booked successfully."
            : "Order placed successfully.", dtoResult);
    }

    public async Task<List<OrderDto>> GetMyOrdersAsync(int userId)
    {
        var ids = await _db.Orders
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => o.Id)
            .ToListAsync();

        return await BuildManyAsync(ids);
    }

    public async Task<(bool Success, string Message, OrderDto? Data)> GetByIdAsync(int userId, string role, int orderId)
    {
        var order = await _db.Orders.FindAsync(orderId);
        if (order == null)
            return (false, "Order not found.", null);

        // Customers can only view their own orders. Admin/Rider can view any.
        if (role == "Customer" && order.UserId != userId)
            return (false, "You can only view your own orders.", null);

        return (true, "Found.", await BuildOrderDtoAsync(orderId));
    }

    public async Task<List<OrderDto>> GetAllAsync(string? status)
    {
        var q = _db.Orders.AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(o => o.Status == status);

        var ids = await q.OrderByDescending(o => o.CreatedAt).Select(o => o.Id).ToListAsync();
        return await BuildManyAsync(ids);
    }

    public async Task<List<OrderDto>> GetVendorOrdersAsync(int userId)
    {
        var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.UserId == userId);
        if (vendor == null) return new List<OrderDto>();

        // Orders that contain at least one product from this vendor.
        var ids = await _db.Orders
            .Where(o => o.OrderItems.Any(oi => oi.Product.VendorId == vendor.Id))
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => o.Id)
            .ToListAsync();

        return await BuildManyAsync(ids);
    }

    public async Task<(bool Success, string Message)> UpdateStatusAsync(int orderId, string status, int actorUserId, string actorRole)
    {
        var allowed = new[] { "Pending", "Processing", "Dispatched", "Delivered", "Cancelled" };
        if (!allowed.Contains(status))
            return (false, "Invalid status.");

        var order = await _db.Orders.Include(o => o.OrderItems).ThenInclude(oi => oi.Product)
            .FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null)
            return (false, "Order not found.");

        // ── Per-role permission rules ────────────────────────────────────────
        // Admin  : any status.
        // Vendor : only orders containing their products, and only the statuses
        //          that make sense for a seller — Confirm (Processing),
        //          hand to delivery (Dispatched), or Cancel.
        // Rider  : only delivery-side statuses.
        if (actorRole == "Vendor")
        {
            var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.UserId == actorUserId);
            var ownsOrder = vendor != null &&
                order.OrderItems.Any(oi => oi.Product != null && oi.Product.VendorId == vendor.Id);
            if (!ownsOrder)
                return (false, "This order doesn't contain any of your products.");

            var vendorAllowed = new[] { "Processing", "Dispatched", "Cancelled" };
            if (!vendorAllowed.Contains(status))
                return (false, "Vendors can set the status to Processing, Dispatched, or Cancelled only.");
        }
        else if (actorRole == "Rider")
        {
            var riderAllowed = new[] { "Dispatched", "Delivered" };
            if (!riderAllowed.Contains(status))
                return (false, "Riders can set the status to Dispatched or Delivered only.");
        }

        // A QR order can't move forward until the admin/vendor has verified the
        // payment screenshot. Cancelling is still allowed.
        if (order.PaymentMethod == "QR"
            && order.PaymentStatus != "Verified" && order.PaymentStatus != "Paid"
            && status != "Cancelled" && status != "Pending")
        {
            return (false, "Payment is not verified yet. This order can't be processed until the payment screenshot is approved.");
        }

        // If cancelling, return the stock back to products.
        if (status == "Cancelled" && order.Status != "Cancelled")
        {
            foreach (var item in order.OrderItems)
            {
                var product = await _db.Products.FindAsync(item.ProductId);
                if (product != null) product.Stock += item.Quantity;
            }
        }

        order.Status = status;
        if (status == "Delivered")
            order.PaymentStatus = "Paid";   // COD collected on delivery

        await _db.SaveChangesAsync();

        // Notify the customer about the order status change.
        var customer = await _db.Users.FindAsync(order.UserId);
        if (customer != null)
        {
            var (title, msg) = status switch
            {
                "Processing" => ("Order Accepted ✅", $"Your order #{order.Id} has been accepted and is being processed."),
                "Dispatched" => ("Order Dispatched 🚚", $"Your order #{order.Id} is on the way!"),
                "Delivered" => ("Order Delivered 📦", $"Your order #{order.Id} has been delivered. Enjoy!"),
                "Cancelled" => ("Order Cancelled", $"Your order #{order.Id} has been cancelled."),
                _ => ("Order Update", $"Your order #{order.Id} is now '{status}'.")
            };
            await _push.SendToUserAsync(customer.Id, title, msg,
                new Dictionary<string, string> { ["type"] = "order", ["orderId"] = order.Id.ToString(), ["status"] = status });
        }

        // Also tell the admin(s) and the vendor(s) of this order (except the
        // person who just made the change) — e.g. vendor accepts → admin is told.
        await NotifyStaffStatusChangeAsync(order.Id, status, actorUserId, actorRole);

        return (true, $"Order status updated to {status}.");
    }

    public async Task<(bool Success, string Message)> AssignRiderAsync(int orderId, int riderId)
    {
        var order = await _db.Orders.FindAsync(orderId);
        if (order == null)
            return (false, "Order not found.");

        var rider = await _db.Users.FindAsync(riderId);
        if (rider == null || rider.Role != "Rider")
            return (false, "That user is not a rider.");

        // Don't dispatch a QR order whose payment hasn't been verified yet.
        if (order.PaymentMethod == "QR"
            && order.PaymentStatus != "Verified" && order.PaymentStatus != "Paid")
        {
            return (false, "Payment is not verified yet. Verify the payment before assigning a rider.");
        }

        order.RiderId = riderId;
        if (order.Status == "Pending" || order.Status == "Processing")
            order.Status = "Dispatched";

        await _db.SaveChangesAsync();

        // Notify the rider they've been assigned a new delivery.
        await _push.SendToUserAsync(rider.Id, "New Delivery Assigned 🛵",
            $"You've been assigned order #{order.Id} for delivery to {order.DeliveryAddress}.",
            new Dictionary<string, string> { ["type"] = "order_assigned", ["orderId"] = order.Id.ToString() });

        return (true, "Rider assigned.");
    }

    public async Task<List<OrderDto>> GetRiderOrdersAsync(int riderId)
    {
        var ids = await _db.Orders
            .Where(o => o.RiderId == riderId)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => o.Id)
            .ToListAsync();

        return await BuildManyAsync(ids);
    }

    public async Task<(bool Success, string Message)> CancelAsync(int userId, int orderId)
    {
        var order = await _db.Orders.Include(o => o.OrderItems).FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null)
            return (false, "Order not found.");
        if (order.UserId != userId)
            return (false, "You can only cancel your own orders.");
        if (order.Status != "Pending" && order.Status != "Processing")
            return (false, "This order can no longer be cancelled.");

        foreach (var item in order.OrderItems)
        {
            var product = await _db.Products.FindAsync(item.ProductId);
            if (product != null) product.Stock += item.Quantity;
        }

        order.Status = "Cancelled";
        await _db.SaveChangesAsync();

        // Let the vendor(s) and admin(s) know the customer cancelled.
        await NotifyStaffStatusChangeAsync(order.Id, "Cancelled", userId, "Customer");

        return (true, "Order cancelled.");
    }

    // ── Push notification helpers ────────────────────────────────────────────
    // Users to alert about an order: all active admins + the owner (User) of
    // every vendor whose product is in the order. Optionally skips one user
    // (the person who triggered the action). Only users with an FCM token.
    private async Task<List<User>> GetOrderStaffAsync(int orderId, int? excludeUserId = null)
    {
        var vendorUserIds = await _db.Orders
            .Where(o => o.Id == orderId)
            .SelectMany(o => o.OrderItems)
            .Select(oi => oi.Product.Vendor.UserId)
            .Distinct()
            .ToListAsync();

        return await _db.Users
            .Where(u => u.IsActive
                        && (u.Role == "Admin" || vendorUserIds.Contains(u.Id))
                        && (excludeUserId == null || u.Id != excludeUserId))
            .ToListAsync();
    }

    private async Task NotifyOrderPlacedAsync(Order order)
    {
        try
        {
            var data = new Dictionary<string, string>
            {
                ["type"] = "order_new",
                ["orderId"] = order.Id.ToString()
            };

            var customer = await _db.Users.FindAsync(order.UserId);
            var sentUsers = new HashSet<int>();

            // Customer
            if (customer != null)
            {
                sentUsers.Add(customer.Id);
                await _push.SendToUserAsync(customer.Id, "Order Placed 🛍️",
                    $"Your order #{order.Id} has been placed. We'll notify you once it's accepted.", data);
            }

            // Vendors + admins
            var itemCount = order.OrderItems.Sum(i => i.Quantity);
            var who = customer?.FullName is { Length: > 0 } n ? n : "a customer";
            foreach (var u in await GetOrderStaffAsync(order.Id, order.UserId))
            {
                if (!sentUsers.Add(u.Id)) continue;

                var (title, body) = u.Role == "Admin"
                    ? ("New Order 🛒", $"Order #{order.Id} placed by {who} — Rs. {order.TotalAmount:0.##}.")
                    : ("New Order Received 🛒", $"Order #{order.Id} from {who} ({itemCount} item(s)). Please accept it.");
                await _push.SendToUserAsync(u.Id, title, body, data);
            }
        }
        catch (Exception ex)
        {
            // A notification problem must never fail the customer's order.
            _logger.LogError(ex, "Failed to send order-placed notifications for order {Id}", order.Id);
        }
    }

    private async Task NotifyStaffStatusChangeAsync(int orderId, string status, int actorUserId, string actorRole)
    {
        try
        {
            var data = new Dictionary<string, string>
            {
                ["type"] = "order_status",
                ["orderId"] = orderId.ToString(),
                ["status"] = status
            };

            var (title, body) = status switch
            {
                "Processing" => ("Order Accepted ✅", $"Order #{orderId} has been accepted ({actorRole})."),
                "Dispatched" => ("Order Dispatched 🚚", $"Order #{orderId} has been dispatched."),
                "Delivered" => ("Order Delivered 📦", $"Order #{orderId} has been delivered."),
                "Cancelled" => ("Order Cancelled", $"Order #{orderId} was cancelled ({actorRole})."),
                _ => ("Order Update", $"Order #{orderId} is now '{status}'.")
            };

            var sentUsers = new HashSet<int>();
            foreach (var u in await GetOrderStaffAsync(orderId, actorUserId))
            {
                if (!sentUsers.Add(u.Id)) continue;
                await _push.SendToUserAsync(u.Id, title, body, data);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send staff notifications for order {Id}", orderId);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private async Task<List<OrderDto>> BuildManyAsync(List<int> ids)
    {
        var result = new List<OrderDto>();
        foreach (var id in ids)
        {
            var dto = await BuildOrderDtoAsync(id);
            if (dto != null) result.Add(dto);
        }
        return result;
    }

    private async Task<OrderDto?> BuildOrderDtoAsync(int orderId)
    {
        var order = await _db.Orders
            .Include(o => o.User)
            .Include(o => o.OrderItems).ThenInclude(oi => oi.Product)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null) return null;

        string? riderName = null;
        if (order.RiderId.HasValue)
            riderName = (await _db.Users.FindAsync(order.RiderId.Value))?.FullName;

        // Linked installation booking, if any.
        var installationBookingId = await _db.ServiceBookings
            .Where(b => b.RelatedOrderId == order.Id)
            .Select(b => (int?)b.Id)
            .FirstOrDefaultAsync();

        var items = order.OrderItems.Select(oi => new OrderItemDto
        {
            Id = oi.Id,
            ProductId = oi.ProductId,
            ProductName = oi.Product != null ? oi.Product.Name : "(removed product)",
            ProductImage = oi.Product != null ? oi.Product.ImagePath : null,
            Quantity = oi.Quantity,
            UnitPrice = oi.UnitPrice,
            LineTotal = oi.UnitPrice * oi.Quantity,
            SelectedSize = oi.SelectedSize,
            SelectedColor = oi.SelectedColor
        }).ToList();

        var subTotal = items.Sum(i => i.LineTotal);

        return new OrderDto
        {
            Id = order.Id,
            UserId = order.UserId,
            CustomerName = order.User != null ? order.User.FullName : string.Empty,
            CustomerPhone = order.User != null ? order.User.Phone : null,
            CustomerEmail = order.User != null ? order.User.Email : null,
            RiderId = order.RiderId,
            RiderName = riderName,
            SubTotal = subTotal,
            Discount = order.Discount,
            DeliveryFee = order.DeliveryFee,
            TotalAmount = order.TotalAmount,
            CommissionAmount = order.CommissionAmount,
            PaymentMethod = order.PaymentMethod,
            PaymentStatus = order.PaymentStatus,
            PaymentScreenshotPath = order.PaymentScreenshotPath,
            Status = order.Status,
            DeliveryAddress = order.DeliveryAddress,
            PromoCode = order.PromoCode,
            CreatedAt = order.CreatedAt,
            Items = items,
            InstallationBookingId = installationBookingId
        };
    }
    // ── Delivery fee helpers ─────────────────────────────────────────────────
    // Reads the admin-configured flat fee and optional free-above threshold from
    // AppSettings. Free when the subtotal reaches the threshold (threshold > 0).
    private async Task<decimal> GetEffectiveDeliveryFeeAsync(decimal subTotal)
    {
        var fee = ParseMoney((await _db.AppSettings.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Key == "DeliveryFee"))?.Value);
        var freeAbove = ParseMoney((await _db.AppSettings.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Key == "FreeDeliveryAbove"))?.Value);
        if (freeAbove > 0 && subTotal >= freeAbove) return 0m;
        return fee < 0 ? 0m : fee;
    }

    private static decimal ParseMoney(string? v)
        => decimal.TryParse(v, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0m;
}
