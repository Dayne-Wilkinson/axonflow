using System.Text.Json;
using System.Text.Json.Nodes;
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

    /// <summary>Same payload shape as <c>axonflow item show --json</c> (blockingPredecessors, blockedBy, optional recentNotes).</summary>
    public static Dictionary<string, object?> BuildItemShowEnvelope(Store store, SqliteConnection c, string projectId, Store.WorkItemRow row, bool includeNotes, int notesLimit)
    {
        var preds = store.GetPredecessorIds(c, row.Id).Select(pidPred =>
        {
            var p = store.GetItemById(c, pidPred);
            return p is null ? null : new { id = p.Id, @ref = $"{store.GetProjectRefPrefix(c, projectId)}-{p.RefNumber}", p.Title, itemStatus = p.Status };
        }).Where(x => x is not null).ToList();
        string? blockedBy = null;
        if (!string.IsNullOrEmpty(row.BlockedById))
        {
            var b = store.GetItemById(c, row.BlockedById);
            if (b is not null)
                blockedBy = JsonSerializer.Serialize(new { id = b.Id, @ref = $"{store.GetProjectRefPrefix(c, projectId)}-{b.RefNumber}", b.Title, b.Type });
        }
        var blockingPreds = preds.Where(p => p!.itemStatus is not ("done" or "cancelled")).ToList();
        var o = new Dictionary<string, object?> { ["item"] = ItemDto(store, c, row) };
        o["blockingPredecessors"] = blockingPreds;
        if (blockedBy is not null) o["blockedBy"] = JsonNode.Parse(blockedBy);
        if (includeNotes) o["recentNotes"] = store.ListNotes(c, row.Id, notesLimit).Select(NoteDto).ToList();
        return o;
    }
}
