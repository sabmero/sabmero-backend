using sabmero.Data;
using sabmero.Models;

namespace sabmero.Services;

// Tiny helper: queue a timeline line on the DbContext. The caller's next
// SaveChangesAsync() persists it together with the status change itself.
public static class StatusEventLog
{
    public static void Add(AppDbContext db, string kind, int refId, string status,
        string? note = null, string? actorName = null, string? actorRole = null)
    {
        db.StatusEvents.Add(new StatusEvent
        {
            Kind = kind,
            RefId = refId,
            Status = status,
            Note = string.IsNullOrWhiteSpace(note) ? null : (note.Length > 300 ? note[..300] : note),
            ActorName = actorName,
            ActorRole = actorRole,
            CreatedAt = DateTime.UtcNow
        });
    }
}
