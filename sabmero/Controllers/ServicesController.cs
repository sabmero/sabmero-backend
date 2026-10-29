using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using sabmero.DTOs.Service;
using sabmero.Helpers;
using sabmero.Services;

namespace sabmero.Controllers;

// ── REPAIR SERVICE CATALOG ────────────────────────────────────────────────────
//  GET    /api/services?includeInactive=   → list services (customers: active only)
//  POST   /api/services                     → create a service   (Admin)
//  PUT    /api/services/{id}                → update a service    (Admin)
//  DELETE /api/services/{id}                → delete a service    (Admin)
// ─────────────────────────────────────────────────────────────────────────────

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ServicesController : ControllerBase
{
    private readonly IRepairServiceCatalog _catalog;

    public ServicesController(IRepairServiceCatalog catalog)
    {
        _catalog = catalog;
    }

    // Anyone can see active services. Only an admin may include inactive.
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false)
    {
        var isAdmin = User.GetRole() == "Admin";
        var data = await _catalog.GetAllAsync(includeInactive && isAdmin);
        return Ok(new { success = true, data });
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRepairServiceDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var (success, message, data) = await _catalog.CreateAsync(dto);
        return success
            ? Ok(new { success = true, message, data })
            : BadRequest(new { success = false, message });
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateRepairServiceDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var (success, message) = await _catalog.UpdateAsync(id, dto);
        return success
            ? Ok(new { success = true, message })
            : BadRequest(new { success = false, message });
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var (success, message) = await _catalog.DeleteAsync(id);
        return success
            ? Ok(new { success = true, message })
            : BadRequest(new { success = false, message });
    }
}
