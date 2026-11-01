using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using sabmero.Data;

namespace sabmero.Controllers;

// -- SERVICE DASHBOARD COUNTS (Admin) -----------------------------------------
//   GET /api/ServiceStats -> category / service / package / booking counts and
//                            the five most recent service bookings
// -----------------------------------------------------------------------------

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class ServiceStatsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ServiceStatsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var recent = await _db.ServiceBookings
            .AsNoTracking()
            .OrderByDescending(b => b.CreatedAt)
            .Take(5)
            .Select(b => new
            {
                id = b.Id,
                serviceType = b.ServiceType,
                status = b.Status,
                createdAt = b.CreatedAt,
                customerName = b.User.FullName
            })
            .ToListAsync();

        var data = new
        {
            mainCategories = await _db.ServiceCategories.CountAsync(c => c.ParentId == null),
            subCategories = await _db.ServiceCategories.CountAsync(c => c.ParentId != null),
            services = await _db.RepairServices.CountAsync(),
            activeServices = await _db.RepairServices.CountAsync(s => s.IsActive),
            inactiveServices = await _db.RepairServices.CountAsync(s => !s.IsActive),
            featuredServices = await _db.RepairServices.CountAsync(s => s.IsFeatured),
            packages = await _db.ServicePackages.CountAsync(),
            bookings = await _db.ServiceBookings.CountAsync(),
            recentBookings = recent
        };

        return Ok(new { success = true, data });
    }
}
