namespace sabmero.Models;

// One line in a booking's / order's activity timeline: "what happened, when,
// and who did it" (e.g. Technician Ram started the job at 2:10 PM).
public class StatusEvent
{
    public int Id { get; set; }
    public string Kind { get; set; } = string.Empty;   // "Booking" | "Order"
    public int RefId { get; set; }                     // booking id / order id
    public string Status { get; set; } = string.Empty; // Placed, Assigned, OnTheWay, Completed, Dispatched, Delivered...
    public string? Note { get; set; }
    public string? ActorName { get; set; }
    public string? ActorRole { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
