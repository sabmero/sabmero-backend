using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using sabmero.DTOs.Payment;
using sabmero.Helpers;
using sabmero.Services;

namespace sabmero.Controllers;

// ── ADMIN → TECHNICIAN PAYMENTS ──────────────────────────────────────────────
//  TECHNICIAN or RIDER (Roles = Technician,Rider):
//   GET  /api/technician-payments/mine                  → my total + history
//   POST /api/technician-payments/{id}/acknowledge      → confirm I received it
//  ADMIN (Roles = Admin):
//   GET  /api/technician-payments/technicians           → technicians to pay (totals)
//   POST /api/technician-payments                       → record a payment
//   GET  /api/technician-payments/history?technicianId= → history (all / one)
//   GET  /api/technician-payments/collections?date=     → what technicians collected
[ApiController]
[Route("api/technician-payments")]
[Authorize]
public class TechnicianPaymentsController : ControllerBase
{
    private readonly ITechnicianPaymentService _service;

    public TechnicianPaymentsController(ITechnicianPaymentService service)
    {
        _service = service;
    }

    [Authorize(Roles = "Technician,Rider")]
    [HttpGet("mine")]
    public async Task<IActionResult> Mine()
        => Ok(new { success = true, data = await _service.GetMyPayoutsAsync(User.GetUserId()) });

    [Authorize(Roles = "Technician,Rider")]
    [HttpPost("{id:int}/acknowledge")]
    public async Task<IActionResult> Acknowledge(int id)
    {
        var (success, message) = await _service.AcknowledgeAsync(User.GetUserId(), id);
        return success
            ? Ok(new { success = true, message })
            : BadRequest(new { success = false, message });
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("technicians")]
    public async Task<IActionResult> Technicians()
        => Ok(new { success = true, data = await _service.GetTechniciansForPayoutAsync() });

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> Record([FromBody] RecordTechnicianPaymentDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var (success, message, data) = await _service.RecordAsync(User.GetUserId(), dto);
        return success
            ? Ok(new { success = true, message, data })
            : BadRequest(new { success = false, message });
    }

    // What technicians collected (cash / online) — per technician and per job.
    // ?date=2026-11-02&technicianId=47&tzOffsetMinutes=345
    [Authorize(Roles = "Admin")]
    [HttpGet("collections")]
    public async Task<IActionResult> Collections([FromQuery] string? date, [FromQuery] int? technicianId, [FromQuery] int tzOffsetMinutes = 345)
        => Ok(new { success = true, data = await _service.GetCollectionsAsync(date, technicianId, tzOffsetMinutes) });

    [Authorize(Roles = "Admin")]
    [HttpGet("history")]
    public async Task<IActionResult> History([FromQuery] int? technicianId)
        => Ok(new { success = true, data = await _service.GetHistoryAsync(technicianId) });
}
