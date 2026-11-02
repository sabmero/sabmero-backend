using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using sabmero.DTOs.Order;
using sabmero.Helpers;
using sabmero.Services;

namespace sabmero.Controllers;

// ── ORDER ENDPOINTS ───────────────────────────────────────────────────────────
//  POST   /api/orders                 → place an order              (Customer)
//  GET    /api/orders/mine            → my orders                   (Customer)
//  GET    /api/orders/{id}            → one order                   (owner/Admin/Rider)
//  POST   /api/orders/{id}/cancel     → cancel my order             (Customer)
//  GET    /api/orders/vendor          → orders with my products     (Vendor)
//  GET    /api/orders/rider           → orders assigned to me       (Rider)
//  PUT    /api/orders/{id}/status     → change status               (Admin/Rider)
//
// (Admin listing of all orders lives under /api/admin/orders.)
// ─────────────────────────────────────────────────────────────────────────────

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _service;

    public OrdersController(IOrderService service)
    {
        _service = service;
    }

    [HttpPost]
    public async Task<IActionResult> Place([FromBody] PlaceOrderDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var (success, message, data) = await _service.PlaceOrderAsync(User.GetUserId(), dto);
        return success
            ? Ok(new { success = true, message, data })
            : BadRequest(new { success = false, message });
    }

    [HttpGet("mine")]
    public async Task<IActionResult> Mine()
    {
        var data = await _service.GetMyOrdersAsync(User.GetUserId());
        return Ok(new { success = true, data });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var (success, message, data) = await _service.GetByIdAsync(User.GetUserId(), User.GetRole(), id);
        return success
            ? Ok(new { success = true, data })
            : NotFound(new { success = false, message });
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id)
    {
        var (success, message) = await _service.CancelAsync(User.GetUserId(), id);
        return success
            ? Ok(new { success = true, message })
            : BadRequest(new { success = false, message });
    }

    [Authorize(Roles = "Vendor,Admin")]
    [HttpGet("vendor")]
    public async Task<IActionResult> VendorOrders()
    {
        var data = await _service.GetVendorOrdersAsync(User.GetUserId());
        return Ok(new { success = true, data });
    }

    [Authorize(Roles = "Rider")]
    [HttpGet("rider")]
    public async Task<IActionResult> RiderOrders()
    {
        var data = await _service.GetRiderOrdersAsync(User.GetUserId());
        return Ok(new { success = true, data });
    }

    // Admin: any status. Vendor: Processing/Dispatched/Cancelled on orders
    // containing their products (this is how a vendor CONFIRMS an order —
    // setting Processing notifies the customer "Order Confirmed").
    // Rider: Dispatched/Delivered.
    [Authorize(Roles = "Admin,Rider,Vendor")]
    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateOrderStatusDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var (success, message) = await _service.UpdateStatusAsync(id, dto.Status, User.GetUserId(), User.GetRole(), dto);
        return success
            ? Ok(new { success = true, message })
            : BadRequest(new { success = false, message });
    }
}