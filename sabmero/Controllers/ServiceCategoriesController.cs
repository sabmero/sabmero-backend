using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using sabmero.DTOs.Service;
using sabmero.Helpers;
using sabmero.Services;

namespace sabmero.Controllers;

// -- SERVICE CATEGORIES (main + sub) -----------------------------------------
//   GET    /api/ServiceCategories?includeInactive=  -> main categories with sub-categories
//   POST   /api/ServiceCategories                   -> create a category   (Admin)
//   PUT    /api/ServiceCategories/{id}              -> update a category   (Admin)
//   DELETE /api/ServiceCategories/{id}              -> delete a category   (Admin)
// -----------------------------------------------------------------------------

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ServiceCategoriesController : ControllerBase
{
    private readonly IServiceCategoryService _categories;

    public ServiceCategoriesController(IServiceCategoryService categories)
    {
        _categories = categories;
    }

    // Any logged-in user sees active categories. Only an admin may include inactive ones.
        [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false)
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false)
    {
        var isAdmin = User.GetRole() == "Admin";
        var data = await _categories.GetTreeAsync(includeInactive && isAdmin);
        return Ok(new { success = true, data });
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateServiceCategoryDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var (success, message, data) = await _categories.CreateAsync(dto);
        return success
            ? Ok(new { success = true, message, data })
            : BadRequest(new { success = false, message });
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateServiceCategoryDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var (success, message) = await _categories.UpdateAsync(id, dto);
        return success
            ? Ok(new { success = true, message })
            : BadRequest(new { success = false, message });
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var (success, message) = await _categories.DeleteAsync(id);
        return success
            ? Ok(new { success = true, message })
            : BadRequest(new { success = false, message });
    }
}
