using System.Collections.Generic;
using System.Reflection.Emit;
using Microsoft.EntityFrameworkCore;
using sabmero.Models;

namespace sabmero.Data;

// This is the "bridge" between your C# code and the PostgreSQL database.
// Every DbSet<X> below becomes a table in PostgreSQL.
// OnModelCreating() defines table rules (primary keys, foreign keys, column sizes).
// SeedData() inserts default rows when the database is first created.

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // ── Each line below = one table in PostgreSQL ──────────────────────────
    public DbSet<User> Users { get; set; }
    public DbSet<OtpCode> OtpCodes { get; set; }
    public DbSet<Vendor> Vendors { get; set; }
    public DbSet<VendorRequest> VendorRequests { get; set; }
    public DbSet<Category> Categories { get; set; }
    public DbSet<Product> Products { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }
    public DbSet<ServiceBooking> ServiceBookings { get; set; }
    public DbSet<PromoCode> PromoCodes { get; set; }
    public DbSet<ReturnRequest> ReturnRequests { get; set; }
    public DbSet<Review> Reviews { get; set; }
    public DbSet<AppSetting> AppSettings { get; set; }
    public DbSet<VendorPayment> VendorPayments { get; set; }
    public DbSet<RepairService> RepairServices { get; set; }
    public DbSet<ServiceCategory> ServiceCategories { get; set; }
    public DbSet<ServicePackage> ServicePackages { get; set; }
public DbSet<ServicePackageItem> ServicePackageItems { get; set; }
    public DbSet<AppNotification> Notifications { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── USERS ──────────────────────────────────────────────────────────
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Phone).IsUnique();          // no two users with same phone
            e.Property(u => u.FullName).HasMaxLength(100).IsRequired();
            e.Property(u => u.Phone).HasMaxLength(20).IsRequired();
            e.Property(u => u.Email).HasMaxLength(150);
            e.Property(u => u.Role).HasMaxLength(20).HasDefaultValue("Customer");
            e.Property(u => u.KycStatus).HasMaxLength(15).HasDefaultValue("NotSubmitted");
            e.Property(u => u.KycRejectionReason).HasMaxLength(400);
            e.Property(u => u.Address).HasMaxLength(300);
        });

        // ── OTP CODES ──────────────────────────────────────────────────────
        modelBuilder.Entity<OtpCode>(e =>
        {
            e.HasKey(o => o.Id);
            e.Property(o => o.Phone).HasMaxLength(100).IsRequired();
            e.Property(o => o.Code).HasMaxLength(6).IsRequired();
        });

        // ── VENDORS ────────────────────────────────────────────────────────
        modelBuilder.Entity<Vendor>(e =>
        {
            e.HasKey(v => v.Id);

            // One User → One Vendor (one-to-one)
            e.HasOne(v => v.User)
             .WithOne(u => u.Vendor)
             .HasForeignKey<Vendor>(v => v.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.Property(v => v.BusinessName).HasMaxLength(200).IsRequired();
            e.Property(v => v.BusinessAddress).HasMaxLength(300);
            e.Property(v => v.CommissionRate).HasPrecision(5, 2);
        });

        // ── VENDOR REQUESTS ────────────────────────────────────────────────
        modelBuilder.Entity<VendorRequest>(e =>
        {
            e.HasKey(r => r.Id);

            e.HasOne(r => r.User)
             .WithMany()
             .HasForeignKey(r => r.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            // Optional link to the Vendor profile created on approval.
            e.HasOne(r => r.Vendor)
             .WithMany()
             .HasForeignKey(r => r.VendorId)
             .OnDelete(DeleteBehavior.SetNull);

            e.Property(r => r.BusinessName).HasMaxLength(200).IsRequired();
            e.Property(r => r.BusinessAddress).HasMaxLength(300);
            e.Property(r => r.Status).HasMaxLength(15).HasDefaultValue("Pending");
            e.Property(r => r.RejectionReason).HasMaxLength(400);
        });

        // ── CATEGORIES ─────────────────────────────────────────────────────
        modelBuilder.Entity<Category>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).HasMaxLength(100).IsRequired();
        });

        // ── PRODUCTS ───────────────────────────────────────────────────────
        modelBuilder.Entity<Product>(e =>
        {
            e.HasKey(p => p.Id);

            // One Vendor → Many Products
            e.HasOne(p => p.Vendor)
             .WithMany(v => v.Products)
             .HasForeignKey(p => p.VendorId)
             .OnDelete(DeleteBehavior.Cascade);

            // One Category → Many Products
            e.HasOne(p => p.Category)
             .WithMany(c => c.Products)
             .HasForeignKey(p => p.CategoryId)
             .OnDelete(DeleteBehavior.Restrict);   // can't delete category if products exist

            e.Property(p => p.Name).HasMaxLength(200).IsRequired();
            e.Property(p => p.Price).HasPrecision(18, 2);
            e.Property(p => p.Unit).HasMaxLength(10);
        });

        // ── ORDERS ─────────────────────────────────────────────────────────
        modelBuilder.Entity<Order>(e =>
        {
            e.HasKey(o => o.Id);

            // One User → Many Orders
            e.HasOne(o => o.User)
             .WithMany(u => u.Orders)
             .HasForeignKey(o => o.UserId)
             .OnDelete(DeleteBehavior.Restrict);

            e.Property(o => o.TotalAmount).HasPrecision(18, 2);
            e.Property(o => o.CommissionAmount).HasPrecision(18, 2);
            e.Property(o => o.DeliveryFee).HasPrecision(18, 2);
            e.Property(o => o.Discount).HasPrecision(18, 2);
            e.Property(o => o.PaymentMethod).HasMaxLength(10).HasDefaultValue("COD");
            e.Property(o => o.PaymentStatus).HasMaxLength(15).HasDefaultValue("Pending");
            e.Property(o => o.PaymentScreenshotPath).HasMaxLength(400);
            e.Property(o => o.Status).HasMaxLength(20).HasDefaultValue("Pending");
            e.Property(o => o.DeliveryAddress).HasMaxLength(400).IsRequired();
            e.Property(o => o.PromoCode).HasMaxLength(50);
        });

        // ── ORDER ITEMS ────────────────────────────────────────────────────
        modelBuilder.Entity<OrderItem>(e =>
        {
            e.HasKey(oi => oi.Id);

            // One Order → Many OrderItems
            e.HasOne(oi => oi.Order)
             .WithMany(o => o.OrderItems)
             .HasForeignKey(oi => oi.OrderId)
             .OnDelete(DeleteBehavior.Cascade);

            // One Product → Many OrderItems
            e.HasOne(oi => oi.Product)
             .WithMany(p => p.OrderItems)
             .HasForeignKey(oi => oi.ProductId)
             .OnDelete(DeleteBehavior.Restrict);

            e.Property(oi => oi.UnitPrice).HasPrecision(18, 2);
            e.Property(oi => oi.SelectedSize).HasMaxLength(20);
            e.Property(oi => oi.SelectedColor).HasMaxLength(30);
        });

        // ── SERVICE BOOKINGS ───────────────────────────────────────────────
        modelBuilder.Entity<ServiceBooking>(e =>
        {
            e.HasKey(sb => sb.Id);

            e.HasOne(sb => sb.User)
             .WithMany(u => u.ServiceBookings)
             .HasForeignKey(sb => sb.UserId)
             .OnDelete(DeleteBehavior.Restrict);

            e.Property(sb => sb.ServiceType).HasMaxLength(50).IsRequired();
            e.Property(sb => sb.TimeSlot).HasMaxLength(50);
            e.Property(sb => sb.ServiceAddress).HasMaxLength(400).IsRequired();
            e.Property(sb => sb.Status).HasMaxLength(20).HasDefaultValue("Pending");
            e.Property(sb => sb.PaymentMethod).HasMaxLength(10).HasDefaultValue("Cash");
            e.Property(sb => sb.PaymentScreenshotPath).HasMaxLength(400);
            e.Property(sb => sb.PaymentStatus).HasMaxLength(15).HasDefaultValue("Pending");
            e.Property(sb => sb.ServiceCharge).HasPrecision(18, 2);
            e.Property(sb => sb.Description).HasMaxLength(1000);

            // Optional link to the order that spawned an installation booking.
            e.HasOne(sb => sb.RelatedOrder)
             .WithMany()
             .HasForeignKey(sb => sb.RelatedOrderId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // ── APP SETTINGS (key-value store, e.g. the admin payment QR) ──────
        modelBuilder.Entity<AppSetting>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => s.Key).IsUnique();
            e.Property(s => s.Key).HasMaxLength(100).IsRequired();
            e.Property(s => s.Value).HasMaxLength(600);
        });

        // ── REPAIR SERVICES (admin-managed service catalog) ─────────────────
        modelBuilder.Entity<RepairService>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Name).HasMaxLength(100).IsRequired();
            e.Property(r => r.Description).HasMaxLength(500);
            e.Property(r => r.Charge).HasPrecision(18, 2);
            e.Property(r => r.ImagePath).HasMaxLength(400);
        });

        // ── VENDOR PAYMENTS (admin → vendor commission / settlement) ────────
        modelBuilder.Entity<VendorPayment>(e =>
        {
            e.HasKey(vp => vp.Id);

            // One Vendor → Many VendorPayments
            e.HasOne(vp => vp.Vendor)
             .WithMany()
             .HasForeignKey(vp => vp.VendorId)
             .OnDelete(DeleteBehavior.Cascade);

            e.Property(vp => vp.Amount).HasPrecision(18, 2);
            e.Property(vp => vp.Note).HasMaxLength(300);
            e.Property(vp => vp.ScreenshotPath).HasMaxLength(400);
            e.Property(vp => vp.Status).HasMaxLength(15).HasDefaultValue("Paid");
        });

        // ── VENDOR payment QR column size ──────────────────────────────────
        modelBuilder.Entity<Vendor>(e =>
        {
            e.Property(v => v.PaymentQrPath).HasMaxLength(400);
        });

        // ── PROMO CODES ────────────────────────────────────────────────────
        modelBuilder.Entity<PromoCode>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.Code).IsUnique();           // codes must be unique
            e.Property(p => p.Code).HasMaxLength(50).IsRequired();
            e.Property(p => p.DiscountPercent).HasPrecision(5, 2);
        });

        // ── RETURN REQUESTS ────────────────────────────────────────────────
        modelBuilder.Entity<ReturnRequest>(e =>
        {
            e.HasKey(r => r.Id);

            // One Order ↔ One ReturnRequest (one-to-one)
            e.HasOne(r => r.Order)
             .WithOne(o => o.ReturnRequest)
             .HasForeignKey<ReturnRequest>(r => r.OrderId)
             .OnDelete(DeleteBehavior.Cascade);

            e.Property(r => r.Reason).HasMaxLength(500).IsRequired();
            e.Property(r => r.Status).HasMaxLength(15).HasDefaultValue("Pending");
            e.Property(r => r.AdminNote).HasMaxLength(300);
        });

        // ── REVIEWS ────────────────────────────────────────────────────────
        modelBuilder.Entity<Review>(e =>
        {
            e.HasKey(r => r.Id);

            e.HasOne(r => r.User)
             .WithMany(u => u.Reviews)
             .HasForeignKey(r => r.UserId)
             .OnDelete(DeleteBehavior.Restrict);

            // Review → Product (optional)
            e.HasOne(r => r.Product)
             .WithMany(p => p.Reviews)
             .HasForeignKey(r => r.ProductId)
             .OnDelete(DeleteBehavior.Cascade)
             .IsRequired(false);

            // Review → ServiceBooking (optional, one-to-one)
            e.HasOne(r => r.ServiceBooking)
             .WithOne(sb => sb.Review)
             .HasForeignKey<Review>(r => r.ServiceBookingId)
             .OnDelete(DeleteBehavior.Cascade)
             .IsRequired(false);

            e.Property(r => r.Comment).HasMaxLength(500);
        });

        // ── SEED DATA (default rows inserted on first migration) ───────────
        // ── NOTIFICATIONS (Alerts tab history) ─────────────────────────────
        modelBuilder.Entity<AppNotification>(e =>
        {
            e.ToTable("Notifications");
            e.HasKey(n => n.Id);
            e.Property(n => n.Title).HasMaxLength(150).IsRequired();
            e.Property(n => n.Body).HasMaxLength(500).IsRequired();
            e.Property(n => n.Type).HasMaxLength(40).IsRequired();
            e.Property(n => n.ReferenceId).HasMaxLength(50);
            e.HasIndex(n => new { n.UserId, n.CreatedAt });

            // One User → Many Notifications
            e.HasOne(n => n.User)
             .WithMany()
             .HasForeignKey(n => n.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        SeedData(modelBuilder);
    }

    private static void SeedData(ModelBuilder modelBuilder)
    {
        // Default Admin user — phone: 9800000000, password: Admin@123
        modelBuilder.Entity<User>().HasData(new User
        {
            Id = 1,
            FullName = "sabmero Admin",
            Phone = "9800000000",
            Email = "admin@sabmero.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
            Address = "Kathmandu, Nepal",
            Role = "Admin",
            IsKycVerified = true,
            KycStatus = "Approved",
            IsActive = true,
            CreatedAt = new DateTime(2026, 6, 4, 0, 0, 0, DateTimeKind.Utc)
        });

        // Default categories from the agreement
        modelBuilder.Entity<Category>().HasData(
            new Category { Id = 1, Name = "Electronics", IsActive = true, CreatedAt = new DateTime(2026, 6, 4, 0, 0, 0, DateTimeKind.Utc) },
            new Category { Id = 2, Name = "Clothing", IsActive = true, CreatedAt = new DateTime(2026, 6, 4, 0, 0, 0, DateTimeKind.Utc) },
            new Category { Id = 3, Name = "Footwear", IsActive = true, CreatedAt = new DateTime(2026, 6, 4, 0, 0, 0, DateTimeKind.Utc) },
            new Category { Id = 4, Name = "Grocery", IsActive = true, CreatedAt = new DateTime(2026, 6, 4, 0, 0, 0, DateTimeKind.Utc) }
        );

        SeedMockData(modelBuilder);
    }

    // ── MOCK DATA FOR TESTING ──────────────────────────────────────────────
    // Inserted on first migration alongside the defaults above.
    // Every seeded account uses the SAME password:  Password@123
    //   Customers   : 9811111111 (Ramesh) , 9822222222 (Sita)
    //   Technicians : 9833333333 (Bibek)  , 9844444444 (Kiran)
    //   Vendor      : 9855555555 (Hari — owns "Hari Electronics Store")
    // The bcrypt hash below is fixed so migrations stay deterministic.
    private static void SeedMockData(ModelBuilder modelBuilder)
    {
        const string mockHash = "$2b$11$J7WSywJFE37tTzHqVU321eYep8c4Muw0ewXAgQelgLn/Q.y3iUiZm"; // "Password@123"
        var seedDate = new DateTime(2026, 6, 4, 0, 0, 0, DateTimeKind.Utc);

        // ── Mock users (Id 1 is the Admin seeded above) ──────────────────────
        modelBuilder.Entity<User>().HasData(
            new User { Id = 2, FullName = "Ramesh Thapa", Phone = "9811111111", Email = "ramesh@example.com", PasswordHash = mockHash, Address = "Baneshwor, Kathmandu", Role = "Customer", IsKycVerified = true, KycStatus = "Approved", IsActive = true, CreatedAt = seedDate },
            new User { Id = 3, FullName = "Sita Gurung", Phone = "9822222222", Email = "sita@example.com", PasswordHash = mockHash, Address = "Lakeside, Pokhara", Role = "Customer", IsKycVerified = true, KycStatus = "Approved", IsActive = true, CreatedAt = seedDate },
            new User { Id = 4, FullName = "Bibek Shrestha", Phone = "9833333333", Email = "bibek.tech@example.com", PasswordHash = mockHash, Address = "Kalanki, Kathmandu", Role = "Technician", IsKycVerified = true, KycStatus = "Approved", IsActive = true, CreatedAt = seedDate },
            new User { Id = 5, FullName = "Kiran Magar", Phone = "9844444444", Email = "kiran.tech@example.com", PasswordHash = mockHash, Address = "Lalitpur, Kathmandu", Role = "Technician", IsKycVerified = true, KycStatus = "Approved", IsActive = true, CreatedAt = seedDate },
            new User { Id = 6, FullName = "Hari Bahadur", Phone = "9855555555", Email = "hari.vendor@example.com", PasswordHash = mockHash, Address = "New Road, Kathmandu", Role = "Vendor", IsKycVerified = true, KycStatus = "Approved", IsActive = true, CreatedAt = seedDate }
        );

        // ── Mock vendor (owned by Hari, Id 6) ────────────────────────────────
        modelBuilder.Entity<Vendor>().HasData(
            new Vendor { Id = 1, UserId = 6, BusinessName = "Hari Electronics Store", BusinessAddress = "New Road, Kathmandu", IsApproved = true, CommissionRate = 10.0m, CreatedAt = seedDate }
        );

        // ── Mock products ────────────────────────────────────────────────────
        modelBuilder.Entity<Product>().HasData(
            new Product { Id = 1, VendorId = 1, CategoryId = 1, Name = "Wireless Earbuds", Description = "Bluetooth 5.3 noise-cancelling earbuds.", Price = 2499m, Stock = 50, IsActive = true, CreatedAt = seedDate },
            new Product { Id = 2, VendorId = 1, CategoryId = 1, Name = "Smart LED Bulb", Description = "Wi-Fi controlled 9W colour-changing bulb.", Price = 899m, Stock = 120, IsActive = true, CreatedAt = seedDate },
            new Product { Id = 3, VendorId = 1, CategoryId = 2, Name = "Cotton T-Shirt", Description = "Unisex round-neck cotton t-shirt.", Price = 699m, Stock = 200, SizeOptions = "[\"S\",\"M\",\"L\",\"XL\"]", ColorOptions = "[\"Black\",\"White\",\"Navy\"]", IsActive = true, CreatedAt = seedDate },
            new Product { Id = 4, VendorId = 1, CategoryId = 4, Name = "Basmati Rice", Description = "Premium aged basmati rice.", Price = 180m, Stock = 300, Unit = "Kg", IsActive = true, CreatedAt = seedDate }
        );

        // ── Mock service bookings ────────────────────────────────────────────
        // 1) Cash booking, still under review (Pending, no technician).
        // 2) QR booking with payment screenshot, still under review.
        // 3) Approved booking — technician (Bibek) assigned, details visible.
        // 4) Completed booking with a final service charge.
        modelBuilder.Entity<ServiceBooking>().HasData(
            new ServiceBooking
            {
                Id = 1,
                UserId = 2,
                TechnicianId = null,
                ServiceType = "Electrical",
                BookingDate = new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc),
                TimeSlot = "10:00 AM - 12:00 PM",
                Latitude = 27.7172,
                Longitude = 85.3240,
                ServiceAddress = "Baneshwor, Kathmandu",
                Status = "Pending",
                PaymentMethod = "Cash",
                CreatedAt = seedDate
            },
            new ServiceBooking
            {
                Id = 2,
                UserId = 3,
                TechnicianId = null,
                ServiceType = "CCTV",
                BookingDate = new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc),
                TimeSlot = "02:00 PM - 04:00 PM",
                Latitude = 28.2096,
                Longitude = 83.9856,
                ServiceAddress = "Lakeside, Pokhara",
                Status = "Pending",
                PaymentMethod = "QR",
                PaymentScreenshotPath = "/uploads/payment/mock-qr-payment-1.jpg",
                PaymentStatus = "Submitted",
                CreatedAt = seedDate
            },
            new ServiceBooking
            {
                Id = 3,
                UserId = 2,
                TechnicianId = 4,
                ServiceType = "Tech",
                BookingDate = new DateTime(2026, 6, 19, 0, 0, 0, DateTimeKind.Utc),
                TimeSlot = "09:00 AM - 11:00 AM",
                Latitude = 27.7172,
                Longitude = 85.3240,
                ServiceAddress = "Baneshwor, Kathmandu",
                Status = "Approved",
                PaymentMethod = "Cash",
                CreatedAt = seedDate
            },
            new ServiceBooking
            {
                Id = 4,
                UserId = 3,
                TechnicianId = 5,
                ServiceType = "Electrical",
                BookingDate = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc),
                TimeSlot = "01:00 PM - 03:00 PM",
                Latitude = 28.2096,
                Longitude = 83.9856,
                ServiceAddress = "Lakeside, Pokhara",
                Status = "Completed",
                CheckInTime = new DateTime(2026, 6, 15, 7, 15, 0, DateTimeKind.Utc),
                CompletedTime = new DateTime(2026, 6, 15, 9, 30, 0, DateTimeKind.Utc),
                PaymentMethod = "QR",
                PaymentScreenshotPath = "/uploads/payment/mock-qr-payment-2.jpg",
                PaymentStatus = "Verified",
                ServiceCharge = 1500m,
                CreatedAt = seedDate
            }
        );

        // ── Admin payment QR (mock) ──────────────────────────────────────────
        modelBuilder.Entity<AppSetting>().HasData(
            new AppSetting
            {
                Id = 1,
                Key = "PaymentQrImagePath",
                Value = "/uploads/payment/admin-qr.png",
                UpdatedAt = seedDate
            }
        );
    }
}
