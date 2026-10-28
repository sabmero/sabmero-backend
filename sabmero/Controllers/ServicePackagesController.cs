using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using sabmero.DTOs.Service;
using sabmero.Helpers;
using sabmero.Services;

namespace sabmero.Controllers;

// -- SERVICE PACKAGES ---------------------------------------------------------
//   GET    /api/ServicePackages?categoryId=&status=  -> list (customers: Active only)
//   GET    /api/ServicePackages/{id}                 -> one package with its items
//   POST   /api/ServicePackages                      -> create   (Admin)
//   PUT    /api/ServicePackages/{id}                 -> update   (Admin)
//   DELETE /api/ServicePackages/{id}                 -> delete   (Admin)
// -----------------------------------------------------------------------------

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ServicePackagesController : ControllerBase
{
    private readonly IServicePackageService _packages;

    public ServicePackagesController(IServicePackageService packages)
    {
        _packages = packages;
    }

    // Any logged-in user sees Active packages. Only an admin can see other statuses.
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? categoryId, [FromQuery] string? status)
    {
        var isAdmin = User.GetRole() == "Admin";
        var data = await _packages.ListAsync(categoryId, status, isAdmin);
        return Ok(new { success = true, data });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var isAdmin = User.GetRole() == "Admin";
        var data = await _packages.GetAsync(id, isAdmin);
        return data == null
            ? NotFound(new { success = false, message = "Package not found." })
            : Ok(new { success = true, data });
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveServicePackageDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var (success, message, data) = await _packages.CreateAsync(dto);
        return success
            ? Ok(new { success = true, message, data })
            : BadRequest(new { success = false, message });
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SaveServicePackageDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var (success, message) = await _packages.UpdateAsync(id, dto);
        return success
            ? Ok(new { success = true, message })
            : BadRequest(new { success = false, message });
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var (success, message) = await _packages.DeleteAsync(id);
        return success
            ? Ok(new { success = true, message })
            : BadRequest(new { success = false, message });
    }
}
