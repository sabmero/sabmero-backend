namespace sabmero.Models;

// One row = one notification shown in the app's Alerts tab (the "Notifications" table).
// A row is saved every time a push is sent to a user, so the history is still
// there even if the phone was offline or the push was switched off.
public class AppNotification
{
    public int Id { get; set; }
    public int UserId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    // Same value as the push "type": order_new, order_status, booking, promo,
    // vendor_request, payment ... The app maps it to an icon.
    public string Type { get; set; } = "system";

    // Id of the related order / booking / request (or the promo code).
    public string? ReferenceId { get; set; }

    public bool IsRead { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}
