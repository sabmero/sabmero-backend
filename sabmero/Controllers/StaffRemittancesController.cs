using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using sabmero.Data;
using sabmero.DTOs.Payment;
using sabmero.Helpers;
using sabmero.Models;
using sabmero.Services;

namespace sabmero.Controllers;

// ── STAFF → SABMERO HAND-OVER OF COLLECTED MONEY ─────────────────────────────
//  TECHNICIAN / RIDER:
//   POST /api/staff-remittances                 → I handed over / sent money
//   GET  /api/staff-remittances/mine            → my hand-overs + status
//  ADMIN:
//   GET  /api/staff-remittances?status=Pending  → all (or by status)
//   POST /api/staff-remittances/{id}/verify     → money received ✔
//   POST /api/staff-remittances/{id}/reject     → not received ✖ (reason)
//   PUT  /api/staff-remittances/staff/{userId}/qr → set the staff member's own payout QR
//  Only VERIFIED hand-overs reduce the cash a person still holds.
[ApiController]
[Route("api/staff-remittances")]
[Authorize]
public class StaffRemittancesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IPushService _push;
    private readonly ILogger<StaffRemittancesController> _logger;

    public StaffRemittancesController(AppDbContext db, IPushService push, ILogger<StaffRemittancesController> logger)
    {
        _db = db;
        _push = push;
        _logger = logger;
    }

    private static StaffRemittanceDto Map(StaffRemittance r) => new()
    {
        Id = r.Id,
        StaffId = r.StaffId,
        StaffName = r.Staff?.FullName ?? string.Empty,
        Role = r.Staff?.Role ?? string.Empty,
        Amount = r.Amount,
        Method = r.Method,
        AmountInWords = r.AmountInWords,
        ScreenshotPath = r.ScreenshotPath,
        Note = r.Note,
        Status = r.Status,
        RejectReason = r.RejectReason,
        CreatedAt = r.CreatedAt,
        VerifiedAt = r.VerifiedAt
    };

    [Authorize(Roles = "Technician,Rider")]
    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] SubmitRemittanceDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var staffId = User.GetUserId();
        if (dto.Amount <= 0)
            return BadRequest(new { success = false, message = "Amount must be greater than 0." });

        var method = string.Equals(dto.Method?.Trim(), "online", StringComparison.OrdinalIgnoreCase) ? "Online" : "Cash";
        if (method == "Online" && string.IsNullOrWhiteSpace(dto.ScreenshotPath))
            return BadRequest(new { success = false, message = "Please attach the payment screenshot." });
        if (method == "Cash" && string.IsNullOrWhiteSpace(dto.AmountInWords))
            return BadRequest(new { success = false, message = "Please write the amount in words." });

        var staff = await _db.Users.FirstOrDefaultAsync(u => u.Id == staffId);
        if (staff == null) return Unauthorized();

        var r = new StaffRemittance
        {
            StaffId = staffId,
            Amount = dto.Amount,
            Method = method,
            AmountInWords = string.IsNullOrWhiteSpace(dto.AmountInWords) ? null : dto.AmountInWords!.Trim(),
            ScreenshotPath = string.IsNullOrWhiteSpace(dto.ScreenshotPath) ? null : dto.ScreenshotPath,
            Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note!.Trim(),
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };
        _db.StaffRemittances.Add(r);
        await _db.SaveChangesAsync();

        try
        {
            var admins = await _db.Users.Where(u => u.Role == "Admin" && u.IsActive).Select(u => u.Id).ToListAsync();
            foreach (var adminId in admins)
            {
                var data = new Dictionary<string, string> { ["type"] = "remittance_submitted", ["remittanceId"] = r.Id.ToString() };
                if (method == "Online" && r.ScreenshotPath != null) data["image"] = r.ScreenshotPath;
                await _push.SendToUserAsync(adminId, "Money hand-over 💵",
                    $"{staff.FullName} ({staff.Role}) sent Rs {r.Amount:0} ({method}). Please verify.", data);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to notify admins about remittance {Id}", r.Id);
        }

        r.Staff = staff;
        return Ok(new { success = true, message = "Sent. The admin will verify it.", data = Map(r) });
    }

    [Authorize(Roles = "Technician,Rider")]
    [HttpGet("mine")]
    public async Task<IActionResult> Mine()
    {
        var staffId = User.GetUserId();
        var rows = await _db.StaffRemittances.AsNoTracking().Include(r => r.Staff)
            .Where(r => r.StaffId == staffId)
            .OrderByDescending(r => r.CreatedAt).Take(50).ToListAsync();
        return Ok(new { success = true, data = rows.Select(Map).ToList() });
    }

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] int? staffId)
    {
        var q = _db.StaffRemittances.AsNoTracking().Include(r => r.Staff).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(r => r.Status == status);
        if (staffId.HasValue) q = q.Where(r => r.StaffId == staffId.Value);
        var rows = await q.OrderByDescending(r => r.CreatedAt).Take(200).ToListAsync();
        return Ok(new { success = true, data = rows.Select(Map).ToList() });
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{id:int}/verify")]
    public async Task<IActionResult> Verify(int id)
    {
        var r = await _db.StaffRemittances.Include(x => x.Staff).FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return NotFound(new { success = false, message = "Not found." });
        if (r.Status != "Pending")
            return BadRequest(new { success = false, message = $"Already {r.Status.ToLower()}." });

        r.Status = "Verified";
        r.VerifiedByUserId = User.GetUserId();
        r.VerifiedAt = DateTime.UtcNow;
        r.RejectReason = null;
        await _db.SaveChangesAsync();

        try
        {
            await _push.SendToUserAsync(r.StaffId, "Hand-over verified ✅",
                $"SabMero received your Rs {r.Amount:0}. Thank you!",
                new Dictionary<string, string> { ["type"] = "remittance_verified", ["remittanceId"] = r.Id.ToString() });
        }
        catch (Exception ex) { _logger.LogError(ex, "Notify failed for remittance {Id}", id); }

        return Ok(new { success = true, message = "Verified.", data = Map(r) });
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{id:int}/reject")]
    public async Task<IActionResult> Reject(int id, [FromBody] RejectRemittanceDto? dto)
    {
        var r = await _db.StaffRemittances.Include(x => x.Staff).FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return NotFound(new { success = false, message = "Not found." });
        if (r.Status != "Pending")
            return BadRequest(new { success = false, message = $"Already {r.Status.ToLower()}." });

        var reason = string.IsNullOrWhiteSpace(dto?.Reason) ? null : dto!.Reason!.Trim();
        if (reason != null && reason.Length > 200) reason = reason[..200];
        r.Status = "Rejected";
        r.VerifiedByUserId = User.GetUserId();
        r.VerifiedAt = DateTime.UtcNow;
        r.RejectReason = reason;
        await _db.SaveChangesAsync();

        try
        {
            await _push.SendToUserAsync(r.StaffId, "Hand-over not accepted ❌",
                $"Your Rs {r.Amount:0} hand-over was not accepted{(reason != null ? ": " + reason : ".")} Please contact SabMero.",
                new Dictionary<string, string> { ["type"] = "remittance_rejected", ["remittanceId"] = r.Id.ToString() });
        }
        catch (Exception ex) { _logger.LogError(ex, "Notify failed for remittance {Id}", id); }

        return Ok(new { success = true, message = "Rejected.", data = Map(r) });
    }

    // Admin sets (or removes) a staff member's own payout QR.
    [Authorize(Roles = "Admin")]
    [HttpPut("staff/{userId:int}/qr")]
    public async Task<IActionResult> SetStaffQr(int userId, [FromBody] SetStaffQrDto dto)
    {
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId && (x.Role == "Technician" || x.Role == "Rider"));
        if (u == null) return NotFound(new { success = false, message = "Staff member not found." });
        u.PaymentQrPath = string.IsNullOrWhiteSpace(dto.QrImagePath) ? null : dto.QrImagePath.Trim();
        await _db.SaveChangesAsync();
        return Ok(new { success = true, message = "QR saved.", data = new { userId = u.Id, qrPath = u.PaymentQrPath } });
    }
}
