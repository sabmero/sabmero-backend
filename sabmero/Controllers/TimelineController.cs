using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using sabmero.Data;
using sabmero.Helpers;

namespace sabmero.Controllers;

// GET /api/timeline/booking/{id}   and   GET /api/timeline/order/{id}
// What happened and when, plus the technician / rider's name and phone.
// Allowed: the customer who owns it, the assigned technician / rider, admins.
[ApiController]
[Route("api/timeline")]
[Authorize]
public class TimelineController : ControllerBase
{
    private readonly AppDbContext _db;
    public TimelineController(AppDbContext db) { _db = db; }

    [HttpGet("{kind}/{id:int}")]
    public async Task<IActionResult> Get(string kind, int id)
    {
        var uid = User.GetUserId();
        var role = User.GetRole();
        kind = (kind ?? "").ToLowerInvariant();

        int ownerId; int? staffId; string staffRole; DateTime created; DateTime? doneAt; string doneStatus;
        if (kind == "booking")
        {
            var b = await _db.ServiceBookings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (b == null) return NotFound(new { success = false, message = "Booking not found." });
            ownerId = b.UserId; staffId = b.TechnicianId; staffRole = "Technician";
            created = b.CreatedAt; doneAt = b.CompletedTime; doneStatus = "Completed";
        }
        else if (kind == "order")
        {
            var o = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (o == null) return NotFound(new { success = false, message = "Order not found." });
            ownerId = o.UserId; staffId = o.RiderId; staffRole = "Rider";
            created = o.CreatedAt; doneAt = o.DeliveredAt; doneStatus = "Delivered";
        }
        else return BadRequest(new { success = false, message = "Unknown kind." });

        var allowed = role == "Admin" || ownerId == uid || (staffId != null && staffId == uid);
        if (!allowed) return Forbid();

        var rows = await _db.StatusEvents.AsNoTracking()
            .Where(e => e.Kind == (kind == "booking" ? "Booking" : "Order") && e.RefId == id)
            .OrderBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .Select(e => new { e.Status, e.Note, e.ActorName, e.ActorRole, e.CreatedAt })
            .ToListAsync();

        // Older records have no history yet: show what we can from timestamps.
        var events = rows.ToList();
        if (events.Count == 0)
        {
            events.Add(new { Status = "Placed", Note = (string?)null, ActorName = (string?)null, ActorRole = (string?)null, CreatedAt = created });
            if (doneAt != null)
                events.Add(new { Status = doneStatus, Note = (string?)null, ActorName = (string?)null, ActorRole = (string?)null, CreatedAt = doneAt.Value });
        }

        object? staff = null;
        if (staffId != null)
        {
            var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == staffId);
            if (u != null) staff = new { name = u.FullName, phone = u.Phone, role = staffRole };
        }

        return Ok(new { success = true, data = new { events, staff } });
    }
}
