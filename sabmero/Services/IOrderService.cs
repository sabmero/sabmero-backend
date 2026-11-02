using sabmero.DTOs.Order;

namespace sabmero.Services;

// Contract for order operations.
public interface IOrderService
{
    // Customer places an order (the whole cart at once).
    Task<(bool Success, string Message, OrderDto? Data)> PlaceOrderAsync(int userId, PlaceOrderDto dto);

    // Customer: list my orders.
    Task<List<OrderDto>> GetMyOrdersAsync(int userId);

    // Get a single order (with ownership/role check done in service).
    Task<(bool Success, string Message, OrderDto? Data)> GetByIdAsync(int userId, string role, int orderId);

    // Admin: list every order.
    Task<List<OrderDto>> GetAllAsync(string? status);

    // Vendor: list orders that contain my products.
    Task<List<OrderDto>> GetVendorOrdersAsync(int userId);

    // Admin/Rider: change an order's status.
    Task<(bool Success, string Message)> UpdateStatusAsync(int orderId, string status, int actorUserId, string actorRole, UpdateOrderStatusDto? extra = null);

    // Admin: assign a rider to an order.
    Task<(bool Success, string Message)> AssignRiderAsync(int orderId, int riderId);

    // Rider: list orders assigned to me.
    Task<List<OrderDto>> GetRiderOrdersAsync(int riderId);

    // Customer: cancel my own order (only while still Pending/Processing).
    Task<(bool Success, string Message)> CancelAsync(int userId, int orderId);
}