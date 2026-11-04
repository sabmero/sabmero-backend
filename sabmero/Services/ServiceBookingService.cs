using Microsoft.EntityFrameworkCore;
using sabmero.Data;
using sabmero.DTOs.Service;
using sabmero.Models;

namespace sabmero.Services;

// Handles the on-site repair side of the app (Electrical, CCTV, Tech).
// Flow: Customer books → Admin assigns technician → Technician marks
//       Processing → OnTheWay → Completed (with final service charge).
public class ServiceBookingService : IServiceBookingService
{
    private readonly AppDbContext _db;
    private readonly ILogger<ServiceBookingService> _logger;
    private readonly IPushService _push;

    public ServiceBookingService(AppDbContext db, ILogger<ServiceBookingService> logger, IPushService push)
    {
        _db = db;
        _logger = logger;
        _push = push;
    }

    public async Task<(bool Success, string Message, BookingDto? Data)> CreateAsync(int userId, CreateBookingDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.ServiceType))
            return (false, "Service type is required.", null);

        if (dto.BookingDate.Date < DateTime.UtcNow.Date)
            return (false, "Booking date cannot be in the past.", null);

        // Normalize the payment method up front.
        var paymentMethod = dto.PaymentMethod == "QR" ? "QR" : "Cash";

        // Cash → no payment step. QR → the customer pays the admin's QR and
        // submits a screenshot afterwards via POST /api/payments/submit, which
        // the admin then verifies. If a screenshot is already provided at
        // creation we accept it and mark the payment as Submitted right away.
        var hasShot = paymentMethod == "QR" && !string.IsNullOrWhiteSpace(dto.PaymentScreenshotPath);
        var paymentStatus = (paymentMethod == "QR" && hasShot) ? "Submitted" : "Pending";

        // Optional catalog linkage: the picked service/package/category must exist.
        if (dto.ServiceId.HasValue
            && !await _db.RepairServices.AnyAsync(s => s.Id == dto.ServiceId.Value))
            return (false, "The selected service was not found.", null);
        if (dto.ServicePackageId.HasValue
            && !await _db.ServicePackages.AnyAsync(p => p.Id == dto.ServicePackageId.Value))
            return (false, "The selected package was not found.", null);
        if (dto.ServiceCategoryId.HasValue
            && !await _db.ServiceCategories.AnyAsync(c => c.Id == dto.ServiceCategoryId.Value))
            return (false, "The selected category was not found.", null);

        // Work out the category when only a service or package was sent.
        var categoryId = dto.ServiceCategoryId;
        if (!categoryId.HasValue && dto.ServiceId.HasValue)
            categoryId = await _db.RepairServices
                .Where(s => s.Id == dto.ServiceId.Value)
                .Select(s => s.CategoryId)
                .FirstOrDefaultAsync();
        if (!categoryId.HasValue && dto.ServicePackageId.HasValue)
            categoryId = await _db.ServicePackages
                .Where(p => p.Id == dto.ServicePackageId.Value)
                .Select(p => (int?)p.CategoryId)
                .FirstOrDefaultAsync();

        // The amount the customer has to pay = the package / service rate.
        // The technician sees it as "Collect Rs X"; the admin can change it.
        decimal? agreedCharge = null;
        if (dto.ServicePackageId.HasValue)
            agreedCharge = await _db.ServicePackages.AsNoTracking()
                .Where(p => p.Id == dto.ServicePackageId.Value).Select(p => (decimal?)p.Price).FirstOrDefaultAsync();
        else if (dto.ServiceId.HasValue)
            agreedCharge = await _db.RepairServices.AsNoTracking()
                .Where(s => s.Id == dto.ServiceId.Value).Select(s => (decimal?)s.Charge).FirstOrDefaultAsync();
        if (agreedCharge is <= 0) agreedCharge = null;

        var booking = new ServiceBooking
        {
            ServiceCharge = agreedCharge,
            UserId = userId,
            ServiceType = dto.ServiceType,
            BookingDate = DateTime.SpecifyKind(dto.BookingDate, DateTimeKind.Utc),
            TimeSlot = dto.TimeSlot.Trim(),
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            ServiceAddress = dto.ServiceAddress.Trim(),
            DamageImagePath = FirstDamagePath(dto),
            DamageImagePathsCsv = JoinDamagePaths(dto),
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
            Status = "Pending",
            PaymentMethod = paymentMethod,
            PaymentScreenshotPath = paymentMethod == "QR" ? dto.PaymentScreenshotPath : null,
            PaymentStatus = paymentStatus,
            ServiceCategoryId = categoryId,
            ServiceId = dto.ServiceId,
            ServicePackageId = dto.ServicePackageId,
            CreatedAt = DateTime.UtcNow
        };

        _db.ServiceBookings.Add(booking);
        await _db.SaveChangesAsync();
        StatusEventLog.Add(_db, "Booking", booking.Id, "Placed", "Booking received", null, "Customer");
        await _db.SaveChangesAsync();
        _logger.LogInformation("Service booking {Id} created by user {UserId}", booking.Id, userId);

        await NotifyBookingCreatedAsync(booking);

        return (true, "Service booked successfully.", await BuildDtoAsync(booking.Id));
    }

    public async Task<List<BookingDto>> GetMyBookingsAsync(int userId)
    {
        var ids = await _db.ServiceBookings
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => b.Id).ToListAsync();
        return await BuildManyAsync(ids);
    }

    public async Task<List<BookingDto>> GetTechnicianBookingsAsync(int technicianId)
    {
        var ids = await _db.ServiceBookings
            .Where(b => b.TechnicianId == technicianId)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => b.Id).ToListAsync();
        return await BuildManyAsync(ids);
    }

    public async Task<List<BookingDto>> GetAllAsync(string? status)
    {
        var q = _db.ServiceBookings.AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(b => b.Status == status);

        var ids = await q.OrderByDescending(b => b.CreatedAt).Select(b => b.Id).ToListAsync();
        return await BuildManyAsync(ids);
    }

    public async Task<(bool Success, string Message, BookingDto? Data)> GetByIdAsync(int userId, string role, int bookingId)
    {
        var booking = await _db.ServiceBookings.FindAsync(bookingId);
        if (booking == null)
            return (false, "Booking not found.", null);

        // Customer sees own; Technician sees assigned; Admin sees all.
        if (role == "Customer" && booking.UserId != userId)
            return (false, "You can only view your own bookings.", null);
        if (role == "Technician" && booking.TechnicianId != userId)
            return (false, "This booking is not assigned to you.", null);

        var data = await BuildDtoAsync(bookingId);

        // While the booking is still under review (Pending / no technician yet),
        // tell the customer it's being reviewed instead of leaking a raw error
        // or empty technician details. Once approved, the technician's name and
        // phone come back in the data payload.
        var message = (booking.Status == "Pending" || booking.TechnicianId == null)
            ? "Your booking is currently under review. Once your status is approved, a technician will be assigned to you."
            : "Found.";

        return (true, message, data);
    }

    public async Task<(bool Success, string Message)> AssignTechnicianAsync(int bookingId, int technicianId)
    {
        var booking = await _db.ServiceBookings.FindAsync(bookingId);
        if (booking == null)
            return (false, "Booking not found.");

        var tech = await _db.Users.FindAsync(technicianId);
        if (tech == null || tech.Role != "Technician")
            return (false, "That user is not a technician.");

        // A QR booking can't be approved/assigned until its payment is verified.
        if (booking.PaymentMethod == "QR" && booking.PaymentStatus != "Verified")
            return (false, "Payment is not verified yet. Verify the payment screenshot before assigning a technician.");

        booking.TechnicianId = technicianId;
        // Assigning a technician approves the booking and reveals the
        // technician's details to the customer.
        if (booking.Status == "Pending")
            booking.Status = "Approved";

        StatusEventLog.Add(_db, "Booking", booking.Id, "Assigned", $"Assigned to technician {tech.FullName}", tech.FullName, "Technician");
        await _db.SaveChangesAsync();

        try
        {
            // Technician: a new job (only this technician is notified).
            var jobData = new Dictionary<string, string> { ["type"] = "booking_assigned", ["bookingId"] = booking.Id.ToString() };
            var damagePhoto = !string.IsNullOrWhiteSpace(booking.DamageImagePath)
                ? booking.DamageImagePath
                : booking.DamageImagePathsCsv?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(damagePhoto)) jobData["image"] = damagePhoto;

            var issue = string.IsNullOrWhiteSpace(booking.Description) ? "" : $" Issue: {(booking.Description.Length > 80 ? booking.Description[..80] + "…" : booking.Description)}";
            await _push.SendToUserAsync(tech.Id, "New Job Assigned 🔧",
                $"{booking.ServiceType} job #{booking.Id} at {booking.ServiceAddress}.{issue}",
                jobData);

            // Customer: "Assigned to Technician" step.
            await _push.SendToUserAsync(booking.UserId, "Technician Assigned 👷",
                $"{tech.FullName} has been assigned to your {booking.ServiceType} booking #{booking.Id}.",
                new Dictionary<string, string> { ["type"] = "booking", ["bookingId"] = booking.Id.ToString(), ["status"] = "Approved" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send assignment notifications for booking {Id}", booking.Id);
        }

        return (true, "Technician assigned.");
    }

    public async Task<(bool Success, string Message)> UpdateStatusAsync(int userId, string role, int bookingId, UpdateBookingStatusDto dto)
    {
        var allowed = new[] { "Pending", "Approved", "Processing", "OnTheWay", "Completed" };
        if (!allowed.Contains(dto.Status))
            return (false, "Invalid status.");

        var booking = await _db.ServiceBookings.FindAsync(bookingId);
        if (booking == null)
            return (false, "Booking not found.");

        // Technician can only update bookings assigned to them.
        if (role == "Technician" && booking.TechnicianId != userId)
            return (false, "This booking is not assigned to you.");

        // A booking can only be Approved once a technician has been assigned,
        // because approval is what surfaces the technician's details.
        if (dto.Status == "Approved" && booking.TechnicianId == null)
            return (false, "Assign a technician before approving this booking.");

        // A QR booking can't be approved until its payment is verified.
        if (dto.Status == "Approved" && booking.PaymentMethod == "QR" && booking.PaymentStatus != "Verified")
            return (false, "Payment is not verified yet. Verify the payment screenshot before approving this booking.");

        // Completing a job: normalise how the money was collected.
        string? collectionMethod = null;
        if (dto.Status == "Completed" && !string.IsNullOrWhiteSpace(dto.CollectionMethod))
        {
            collectionMethod = dto.CollectionMethod.Trim().Equals("online", StringComparison.OrdinalIgnoreCase) ? "Online" : "Cash";
            if (collectionMethod == "Online" && string.IsNullOrWhiteSpace(dto.CollectionScreenshotPath))
                return (false, "Please attach the online payment screenshot.");
        }

        booking.Status = dto.Status;

        // Timestamps for the technician workflow.
        if (dto.Status == "OnTheWay" && booking.CheckInTime == null)
            booking.CheckInTime = DateTime.UtcNow;

        if (dto.Status == "Completed")
        {
            booking.CompletedTime = DateTime.UtcNow;
            if (dto.ServiceCharge.HasValue)
                booking.ServiceCharge = dto.ServiceCharge.Value;

            if (collectionMethod != null)
            {
                booking.CollectionMethod = collectionMethod;
                booking.CollectionAmountInWords = string.IsNullOrWhiteSpace(dto.AmountInWords) ? null : dto.AmountInWords.Trim();
                booking.CollectionScreenshotPath = collectionMethod == "Online" ? dto.CollectionScreenshotPath : null;
            }
        }

        var actor = await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.FullName).FirstOrDefaultAsync();
        var collectedNote = (dto.Status == "Completed" && collectionMethod != null)
            ? $"Payment collected via {collectionMethod}" : null;
        StatusEventLog.Add(_db, "Booking", booking.Id, dto.Status, collectedNote, actor, role);
        await _db.SaveChangesAsync();

        // Notify the customer about the status change (fire-and-forget safe).
        try
        {
            var (title, msg) = dto.Status switch
            {
                "Approved" => ("Booking Approved ✅", $"Your {booking.ServiceType} booking #{booking.Id} has been approved. A technician has been assigned."),
                "Processing" => ("Booking In Progress 🔧", $"Your {booking.ServiceType} booking #{booking.Id} is being processed."),
                "OnTheWay" => ("Technician On The Way 🛵", $"The technician for booking #{booking.Id} is on the way to your location."),
                "Completed" => ("Service Completed 🎉", $"Your {booking.ServiceType} booking #{booking.Id} is completed. Please leave a review!"),
                _ => ("Booking Update", $"Your booking #{booking.Id} is now '{dto.Status}'.")
            };
            var data = new Dictionary<string, string> { ["type"] = "booking", ["bookingId"] = booking.Id.ToString(), ["status"] = dto.Status };

            // Customer: each step of the tracker.
            await _push.SendToUserAsync(booking.UserId, title, msg, data);

            // Admins: told about technician progress (not the admin who made the change).
            var admins = await _db.Users
                .Where(u => u.Role == "Admin" && u.IsActive && u.Id != userId)
                .Select(u => u.Id).ToListAsync();
            var adminTitle = dto.Status == "Completed" ? "Service Completed ✅" : "Booking Update 🔧";
            var adminBody = dto.Status == "Completed"
                ? $"Booking #{booking.Id} ({booking.ServiceType}) completed. Collected Rs. {booking.ServiceCharge:0.##} via {booking.CollectionMethod ?? booking.PaymentMethod}"
                  + (string.IsNullOrWhiteSpace(booking.CollectionAmountInWords) ? "." : $" ({booking.CollectionAmountInWords}).")
                : $"Booking #{booking.Id} ({booking.ServiceType}) is now '{dto.Status}'.";
            foreach (var adminId in admins)
                await _push.SendToUserAsync(adminId, adminTitle, adminBody,
                    new Dictionary<string, string> { ["type"] = "booking_status", ["bookingId"] = booking.Id.ToString(), ["status"] = dto.Status });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send status notifications for booking {Id}", booking.Id);
        }

        return (true, $"Booking status updated to {dto.Status}.");
    }

    public async Task<(bool Success, string Message)> CancelAsync(int userId, int bookingId)
    {
        var booking = await _db.ServiceBookings.FindAsync(bookingId);
        if (booking == null)
            return (false, "Booking not found.");
        if (booking.UserId != userId)
            return (false, "You can only cancel your own bookings.");
        // A customer can cancel any time BEFORE the technician is on the way.
        // (Previously "Approved" was missing here, so cancelling an approved
        // booking always failed with a 400 — that was the bug.)
        var cancellable = new[] { "Pending", "Approved", "Processing" };
        if (!cancellable.Contains(booking.Status))
            return (false, "This booking can no longer be cancelled because the technician is already on the way or the job is completed.");

        var cancelledId = booking.Id;
        var cancelledType = booking.ServiceType;
        var cancelledTech = booking.TechnicianId;

        // Delete the booking record on cancellation.
        _db.ServiceBookings.Remove(booking);
        await _db.SaveChangesAsync();

        try
        {
            var ids = await _db.Users
                .Where(u => u.IsActive && (u.Role == "Admin" || (cancelledTech != null && u.Id == cancelledTech)))
                .Select(u => u.Id).ToListAsync();
            foreach (var id in ids)
                await _push.SendToUserAsync(id, "Booking Cancelled",
                    $"Service booking #{cancelledId} ({cancelledType}) was cancelled by the customer.",
                    new Dictionary<string, string> { ["type"] = "booking_cancelled" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send cancel notifications for booking {Id}", cancelledId);
        }
        return (true, "Booking cancelled.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    // New booking: tell the customer ("Booking Received") and every admin.
    private async Task NotifyBookingCreatedAsync(ServiceBooking booking)
    {
        try
        {
            var customer = await _db.Users.AsNoTracking()
                .Where(u => u.Id == booking.UserId)
                .Select(u => new { u.Id, u.FullName })
                .FirstOrDefaultAsync();

            await _push.SendToUserAsync(booking.UserId, "Booking Received 📝",
                $"Your {booking.ServiceType} booking #{booking.Id} has been received. We'll assign a technician soon.",
                new Dictionary<string, string> { ["type"] = "booking", ["bookingId"] = booking.Id.ToString(), ["status"] = "Pending" });

            var who = string.IsNullOrWhiteSpace(customer?.FullName) ? "a customer" : customer!.FullName;
            var admins = await _db.Users
                .Where(u => u.Role == "Admin" && u.IsActive && u.Id != booking.UserId)
                .Select(u => u.Id).ToListAsync();
            foreach (var adminId in admins)
                await _push.SendToUserAsync(adminId, "New Service Booking 🔧",
                    $"Booking #{booking.Id} ({booking.ServiceType}) from {who} on {booking.BookingDate:dd MMM} {booking.TimeSlot}. Assign a technician.",
                    new Dictionary<string, string> { ["type"] = "booking_new", ["bookingId"] = booking.Id.ToString() });
        }
        catch (Exception ex)
        {
            // A notification problem must never fail the customer's booking.
            _logger.LogError(ex, "Failed to send booking-created notifications for booking {Id}", booking.Id);
        }
    }

    private async Task<List<BookingDto>> BuildManyAsync(List<int> ids)
    {
        var result = new List<BookingDto>();
        foreach (var id in ids)
        {
            var dto = await BuildDtoAsync(id);
            if (dto != null) result.Add(dto);
        }
        return result;
    }

    private async Task<BookingDto?> BuildDtoAsync(int bookingId)
    {
        var b = await _db.ServiceBookings
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Id == bookingId);
        if (b == null) return null;

        string? techName = null;
        string? techPhone = null;
        // Only reveal the technician's details once the booking is approved
        // (i.e. a technician has actually been assigned to this customer).
        if (b.TechnicianId.HasValue && b.Status != "Pending")
        {
            var tech = await _db.Users.FindAsync(b.TechnicianId.Value);
            techName = tech?.FullName;
            techPhone = tech?.Phone;
        }

        // Resolve the picked category into main / sub (older bookings have none).
        int? mainId = null, subId = null;
        string? mainName = null, subName = null;
        if (b.ServiceCategoryId.HasValue)
        {
            var cat = await _db.ServiceCategories.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == b.ServiceCategoryId.Value);
            if (cat != null)
            {
                if (cat.ParentId.HasValue)
                {
                    var parent = await _db.ServiceCategories.AsNoTracking()
                        .FirstOrDefaultAsync(c => c.Id == cat.ParentId.Value);
                    subId = cat.Id;
                    subName = cat.Name;
                    mainId = parent?.Id;
                    mainName = parent?.Name;
                }
                else
                {
                    mainId = cat.Id;
                    mainName = cat.Name;
                }
            }
        }

        return new BookingDto
        {
            Id = b.Id,
            UserId = b.UserId,
            CustomerName = b.User.FullName,
            CustomerPhone = b.User.Phone,
            TechnicianId = b.TechnicianId,
            TechnicianName = techName,
            TechnicianPhone = techPhone,
            ServiceType = b.ServiceType,
            BookingDate = b.BookingDate,
            TimeSlot = b.TimeSlot,
            Latitude = b.Latitude,
            Longitude = b.Longitude,
            ServiceAddress = b.ServiceAddress,
            DamageImagePath = b.DamageImagePath,
            DamageImagePaths = SplitDamagePaths(b.DamageImagePathsCsv, b.DamageImagePath),
            Description = b.Description,
            Status = b.Status,
            CheckInTime = b.CheckInTime,
            CompletedTime = b.CompletedTime,
            PaymentMethod = b.PaymentMethod,
            PaymentScreenshotPath = b.PaymentScreenshotPath,
            PaymentStatus = b.PaymentStatus,
            ServiceCharge = b.ServiceCharge,
            CollectionMethod = b.CollectionMethod,
            CollectionAmountInWords = b.CollectionAmountInWords,
            CollectionScreenshotPath = b.CollectionScreenshotPath,
            CreatedAt = b.CreatedAt,
            MainCategoryId = mainId,
            MainCategoryName = mainName,
            SubCategoryId = subId,
            SubCategoryName = subName,
            ServiceId = b.ServiceId,
            ServicePackageId = b.ServicePackageId
        };
    }
    // ── Damage-photo helpers ─────────────────────────────────────────────────
    // The app may upload several photos; we keep them all as a comma-separated
    // list and the first one in DamageImagePath for older clients.
    private static List<string> AllDamagePaths(DTOs.Service.CreateBookingDto dto)
    {
        var list = new List<string>();
        if (dto.DamageImagePaths != null)
            list.AddRange(dto.DamageImagePaths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));
        if (!string.IsNullOrWhiteSpace(dto.DamageImagePath) && !list.Contains(dto.DamageImagePath!.Trim()))
            list.Insert(0, dto.DamageImagePath!.Trim());
        return list;
    }

    private static string? FirstDamagePath(DTOs.Service.CreateBookingDto dto)
    {
        var all = AllDamagePaths(dto);
        return all.Count > 0 ? all[0] : null;
    }

    private static string? JoinDamagePaths(DTOs.Service.CreateBookingDto dto)
    {
        var all = AllDamagePaths(dto);
        return all.Count > 0 ? string.Join(",", all) : null;
    }

    private static List<string> SplitDamagePaths(string? csv, string? single)
    {
        if (!string.IsNullOrWhiteSpace(csv))
            return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (!string.IsNullOrWhiteSpace(single))
            return new List<string> { single!.Trim() };
        return new List<string>();
    }
}
