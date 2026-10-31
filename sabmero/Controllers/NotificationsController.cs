using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using sabmero.Data;
using sabmero.Helpers;

namespace sabmero.Controllers;

// ── NOTIFICATION HISTORY (Alerts tab) ─────────────────────────────────────────
//  GET   /api/notifications              → my latest 100 notifications (newest first)
//  POST  /api/notifications/{id}/read    → mark one as read
//  POST  /api/notifications/read-all     → mark all of mine as read
// Rows are created automatically whenever a push is sent (see FcmPushService).
// ─────────────────────────────────────────────────────────────────────────────

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public NotificationsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Mine()
    {
        var userId = User.GetUserId();
        var data = await _db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(100)
            .Select(n => new
            {
                n.Id,
                n.Title,
                n.Body,
                n.Type,
                n.ReferenceId,
                n.IsRead,
                n.CreatedAt
            })
            .ToListAsync();

        return Ok(new { success = true, data });
    }

    // Number of unread notifications — for the bell badge in the web panels.
    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount()
    {
        var userId = User.GetUserId();
        var count = await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);
        return Ok(new { success = true, data = new { count } });
    }

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        var userId = User.GetUserId();
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (n == null)
            return NotFound(new { success = false, message = "Notification not found." });

        if (!n.IsRead)
        {
            n.IsRead = true;
            await _db.SaveChangesAsync();
        }
        return Ok(new { success = true, message = "Marked as read." });
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var userId = User.GetUserId();
        var unread = await _db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync();

        foreach (var n in unread) n.IsRead = true;
        await _db.SaveChangesAsync();

        return Ok(new { success = true, message = "All notifications marked as read." });
    }
}
