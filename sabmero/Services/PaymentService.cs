using Microsoft.EntityFrameworkCore;
using sabmero.Data;
using sabmero.DTOs.Payment;
using sabmero.Models;

namespace sabmero.Services;

// Manual QR payment flow (no third-party gateway).
//
// The admin uploads ONE global QR image. Customers scan it, pay externally,
// and upload a screenshot as proof. Admin (any) or Vendor (own orders) then
// verify the proof. A QR order/booking is blocked from progressing until its
// payment is Verified.
//
// PaymentStatus values:  Pending → Submitted → Verified | Rejected
public class PaymentService : IPaymentService
{
    // Well-known AppSetting key holding the admin's QR image path.
    private const string QrKey = "PaymentQrImagePath";

    private readonly AppDbContext _db;
    private readonly ILogger<PaymentService> _logger;
    private readonly IPushService _push;

    public PaymentService(AppDbContext db, ILogger<PaymentService> logger, IPushService push)
    {
        _db = db;
        _logger = logger;
        _push = push;
    }

    // ── Admin sets the global QR image ───────────────────────────────────────
    public async Task<(bool Success, string Message)> SetQrAsync(string qrImagePath)
    {
        if (string.IsNullOrWhiteSpace(qrImagePath))
            return (false, "QR image path is required.");

        var setting = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == QrKey);
        if (setting == null)
        {
            setting = new AppSetting { Key = QrKey, Value = qrImagePath, UpdatedAt = DateTime.UtcNow };
            _db.AppSettings.Add(setting);
        }
        else
        {
            setting.Value = qrImagePath;
            setting.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Admin updated payment QR image to {Path}", qrImagePath);
        return (true, "Payment QR updated.");
    }

    // ── Any user fetches the QR to pay ───────────────────────────────────────
    public async Task<QrInfoDto> GetQrAsync()
    {
        var setting = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == QrKey);
        return new QrInfoDto { QrImagePath = setting?.Value };
    }

    // ── Customer submits a payment screenshot ────────────────────────────────
    public async Task<(bool Success, string Message)> SubmitAsync(int userId, SubmitPaymentDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.ScreenshotPath))
            return (false, "Please upload a payment screenshot first.");

        var type = NormalizeType(dto.Type);
        if (type == null)
            return (false, "Type must be 'Order' or 'Booking'.");

        if (type == "Order")
        {
            var order = await _db.Orders.FindAsync(dto.Id);
            if (order == null) return (false, "Order not found.");
            if (order.UserId != userId) return (false, "You can only pay for your own orders.");
            if (order.PaymentStatus == "Verified" || order.PaymentStatus == "Paid")
                return (false, "This order is already paid.");

            order.PaymentMethod = "QR";
            order.PaymentScreenshotPath = dto.ScreenshotPath;
            order.PaymentStatus = "Submitted";
            await _db.SaveChangesAsync();
            _logger.LogInformation("Payment screenshot submitted for order {Id} by user {UserId}", dto.Id, userId);
            return (true, "Payment screenshot submitted. Awaiting verification.");
        }
        else // Booking
        {
            var booking = await _db.ServiceBookings.FindAsync(dto.Id);
            if (booking == null) return (false, "Booking not found.");
            if (booking.UserId != userId) return (false, "You can only pay for your own bookings.");
            if (booking.PaymentStatus == "Verified")
                return (false, "This booking is already paid.");

            booking.PaymentMethod = "QR";
            booking.PaymentScreenshotPath = dto.ScreenshotPath;
            booking.PaymentStatus = "Submitted";
            await _db.SaveChangesAsync();
            _logger.LogInformation("Payment screenshot submitted for booking {Id} by user {UserId}", dto.Id, userId);
            return (true, "Payment screenshot submitted. Awaiting verification.");
        }
    }

    // ── Admin/Vendor verify a submitted payment ──────────────────────────────
    public async Task<(bool Success, string Message)> VerifyAsync(int userId, string role, VerifyPaymentDto dto)
    {
        var type = NormalizeType(dto.Type);
        if (type == null)
            return (false, "Type must be 'Order' or 'Booking'.");

        if (type == "Order")
        {
            var order = await _db.Orders
                .Include(o => o.OrderItems).ThenInclude(oi => oi.Product)
                .FirstOrDefaultAsync(o => o.Id == dto.Id);
            if (order == null) return (false, "Order not found.");

            // Vendors may only verify orders that contain one of their products.
            if (role == "Vendor" && !await VendorOwnsOrderAsync(userId, order))
                return (false, "This order doesn't contain any of your products.");

            if (order.PaymentStatus != "Submitted")
                return (false, "This payment is not awaiting verification.");

            order.PaymentStatus = dto.Approve ? "Verified" : "Rejected";
            await _db.SaveChangesAsync();
            _logger.LogInformation("Order {Id} payment {Result} by user {UserId}",
                dto.Id, order.PaymentStatus, userId);

            // Tell the customer their payment was reviewed.
            var customer = await _db.Users.FindAsync(order.UserId);
            if (customer != null)
            {
                var (title, body) = dto.Approve
                    ? ("Payment Verified ✅", $"Your payment for order #{order.Id} has been verified. Your order will be processed soon.")
                    : ("Payment Rejected ❌", $"Your payment for order #{order.Id} could not be verified. Please re-submit a valid screenshot.");
                await _push.SendToUserAsync(customer.Id, title, body,
                    new Dictionary<string, string> { ["type"] = "payment", ["orderId"] = order.Id.ToString() });
            }
            return (true, dto.Approve ? "Payment verified." : "Payment rejected.");
        }
        else // Booking
        {
            // Only Admin verifies service-booking payments (vendors don't own bookings).
            if (role == "Vendor")
                return (false, "Vendors can't verify service-booking payments.");

            var booking = await _db.ServiceBookings.FindAsync(dto.Id);
            if (booking == null) return (false, "Booking not found.");
            if (booking.PaymentStatus != "Submitted")
                return (false, "This payment is not awaiting verification.");

            booking.PaymentStatus = dto.Approve ? "Verified" : "Rejected";
            await _db.SaveChangesAsync();
            _logger.LogInformation("Booking {Id} payment {Result} by user {UserId}",
                dto.Id, booking.PaymentStatus, userId);

            var bkCustomer = await _db.Users.FindAsync(booking.UserId);
            if (bkCustomer != null)
            {
                var (title, body) = dto.Approve
                    ? ("Payment Verified ✅", $"Your payment for booking #{booking.Id} has been verified.")
                    : ("Payment Rejected ❌", $"Your payment for booking #{booking.Id} could not be verified. Please re-submit.");
                await _push.SendToUserAsync(bkCustomer.Id, title, body,
                    new Dictionary<string, string> { ["type"] = "payment", ["bookingId"] = booking.Id.ToString() });
            }
            return (true, dto.Approve ? "Payment verified." : "Payment rejected.");
        }
    }

    // ── Admin/Vendor list payments awaiting verification ─────────────────────
    public async Task<List<PendingPaymentDto>> GetPendingAsync(int userId, string role)
    {
        var result = new List<PendingPaymentDto>();

        // Orders with a submitted payment.
        var orderQuery = _db.Orders
            .Include(o => o.User)
            .Include(o => o.OrderItems).ThenInclude(oi => oi.Product)
            .Where(o => o.PaymentStatus == "Submitted");

        // Vendors only see orders containing their products.
        if (role == "Vendor")
        {
            var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.UserId == userId);
            if (vendor != null)
                orderQuery = orderQuery.Where(o => o.OrderItems.Any(oi => oi.Product.VendorId == vendor.Id));
            else
                orderQuery = orderQuery.Where(o => false);
        }

        var orders = await orderQuery.OrderByDescending(o => o.CreatedAt).ToListAsync();
        foreach (var o in orders)
        {
            result.Add(new PendingPaymentDto
            {
                Type = "Order",
                Id = o.Id,
                CustomerName = o.User.FullName,
                CustomerPhone = o.User.Phone,
                Amount = o.TotalAmount,
                PaymentStatus = o.PaymentStatus,
                ScreenshotPath = o.PaymentScreenshotPath,
                CreatedAt = o.CreatedAt
            });
        }

        // Bookings with a submitted payment (Admin only — vendors don't own bookings).
        if (role != "Vendor")
        {
            var bookings = await _db.ServiceBookings
                .Include(b => b.User)
                .Where(b => b.PaymentStatus == "Submitted")
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();

            foreach (var b in bookings)
            {
                result.Add(new PendingPaymentDto
                {
                    Type = "Booking",
                    Id = b.Id,
                    CustomerName = b.User.FullName,
                    CustomerPhone = b.User.Phone,
                    Amount = b.ServiceCharge ?? 0m,
                    PaymentStatus = b.PaymentStatus,
                    ScreenshotPath = b.PaymentScreenshotPath,
                    CreatedAt = b.CreatedAt
                });
            }
        }

        return result;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private static string? NormalizeType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return null;
        var t = type.Trim().ToLowerInvariant();
        if (t == "order") return "Order";
        if (t == "booking") return "Booking";
        return null;
    }

    private async Task<bool> VendorOwnsOrderAsync(int userId, Order order)
    {
        var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.UserId == userId);
        if (vendor == null) return false;
        return order.OrderItems.Any(oi => oi.Product.VendorId == vendor.Id);
    }
}