using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AxonFlow;

public static class ItemJson
{
    public static object ItemDto(Store store, SqliteConnection c, Store.WorkItemRow w, bool includeDiscoveredFrom = true)
    {
        var pref = store.GetProjectRefPrefix(c, w.ProjectId);
        object? discovered = null;
        if (includeDiscoveredFrom && w.DiscoveredFromId is not null)
        {
            var d = store.GetItemById(c, w.DiscoveredFromId);
            if (d is not null)
            {
                var dp = store.GetProjectRefPrefix(c, d.ProjectId);
                discovered = new { id = d.Id, @ref = $"{dp}-{d.RefNumber}", title = d.Title };
            }
        }
        string[]? pathHints = null;
        if (!string.IsNullOrEmpty(w.PathHints))
        {
            try { pathHints = JsonSerializer.Deserialize<string[]>(w.PathHints); } catch { pathHints = null; }
        }
        return new
        {
            id = w.Id,
            @ref = $"{pref}-{w.RefNumber}",
            type = w.Type,
            stream = w.Stream,
            status = w.Status,
            title = w.Title,
            body = w.Body,
            priority = w.Priority,
            parentId = w.ParentId,
            blockedByWorkItemId = w.BlockedById,
            blockedReason = w.BlockedReason,
            pathHints,
            snoozedUntil = w.SnoozedUntil,
            discoveredFrom = discovered,
            clientKey = w.ClientKey,
            externalRef = w.ExternalRef,
            assignedTo = w.AssignedTo,
            sortOrder = w.SortOrder,
            completedAt = w.CompletedAt,
            createdAt = w.CreatedAt,
            updatedAt = w.UpdatedAt
        };
    }

    public static object NoteDto(Store.NoteRow n) =>
        new { id = n.Id, at = n.At, actor = n.Actor, body = n.Body };
}
