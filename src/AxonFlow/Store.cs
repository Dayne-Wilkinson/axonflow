using Microsoft.Data.Sqlite;

namespace AxonFlow;

public sealed class Store(string connectionString)
{
    public SqliteConnection Open()
    {
        var c = new SqliteConnection(connectionString);
        c.Open();
        using var pragma = c.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;";
        pragma.ExecuteNonQuery();
        return c;
    }

    public static string Now() => DateTimeOffset.UtcNow.ToString("O");

    public string? GetProjectId(SqliteConnection c, string slug)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id FROM projects WHERE slug = @s LIMIT 1;";
        cmd.Parameters.AddWithValue("@s", slug);
        return cmd.ExecuteScalar() as string;
    }

    public int CountProjects(SqliteConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM projects;";
        return Convert.ToInt32(cmd.ExecuteScalar()!);
    }

    public void InsertDefaultProject(SqliteConnection c)
    {
        var now = Now();
        var id = Guid.NewGuid().ToString("N");
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO projects(id, name, slug, ref_prefix, created_at, updated_at)
            VALUES(@id, 'Default', 'default', 'AF', @t, @t);
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@t", now);
        cmd.ExecuteNonQuery();
    }

    public void InsertProject(SqliteConnection c, string name, string slug, string refPrefix)
    {
        var now = Now();
        var id = Guid.NewGuid().ToString("N");
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO projects(id, name, slug, ref_prefix, created_at, updated_at)
            VALUES(@id, @n, @slug, @rp, @t, @t);
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@n", name);
        cmd.Parameters.AddWithValue("@slug", slug);
        cmd.Parameters.AddWithValue("@rp", string.IsNullOrWhiteSpace(refPrefix) ? "AF" : refPrefix.Trim());
        cmd.Parameters.AddWithValue("@t", now);
        cmd.ExecuteNonQuery();
    }

    public List<(string Id, string Name, string Slug, string RefPrefix)> ListProjects(SqliteConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, name, slug, ref_prefix FROM projects ORDER BY slug;";
        using var r = cmd.ExecuteReader();
        var list = new List<(string, string, string, string)>();
        while (r.Read())
            list.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        return list;
    }

    public int GetNextRefNumber(SqliteConnection c, string projectId, SqliteTransaction? tx = null)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COALESCE(MAX(ref_number), 0) + 1 FROM work_items WHERE project_id = @p;";
        cmd.Parameters.AddWithValue("@p", projectId);
        return Convert.ToInt32(cmd.ExecuteScalar()!);
    }

    public string GetProjectRefPrefix(SqliteConnection c, string projectId)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT ref_prefix FROM projects WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", projectId);
        return (string)cmd.ExecuteScalar()!;
    }

    public WorkItemRow? GetItemById(SqliteConnection c, string id)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM work_items WHERE id = @id LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadItem(r) : null;
    }

    public WorkItemRow? GetItemByRef(SqliteConnection c, string projectId, string refPrefix, int refNumber)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT w.* FROM work_items w
            JOIN projects p ON p.id = w.project_id
            WHERE w.project_id = @pid AND w.ref_number = @rn AND UPPER(p.ref_prefix) = UPPER(@rp)
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("@pid", projectId);
        cmd.Parameters.AddWithValue("@rn", refNumber);
        cmd.Parameters.AddWithValue("@rp", refPrefix);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadItem(r) : null;
    }

    public WorkItemRow? GetItemByClientKey(SqliteConnection c, string projectId, string clientKey)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM work_items WHERE project_id = @p AND client_key = @ck LIMIT 1;";
        cmd.Parameters.AddWithValue("@p", projectId);
        cmd.Parameters.AddWithValue("@ck", clientKey);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadItem(r) : null;
    }

    public static bool TryParseRef(string token, out string prefix, out int num)
    {
        prefix = "";
        num = 0;
        var t = token.Trim();
        var idx = t.LastIndexOf('-');
        if (idx <= 0 || idx == t.Length - 1) return false;
        prefix = t[..idx];
        return int.TryParse(t[(idx + 1)..], out num);
    }

    public WorkItemRow? ResolveItem(SqliteConnection c, string projectId, string idOrRef)
    {
        var pfx = GetProjectRefPrefix(c, projectId);
        if (idOrRef.Length == 32 && IsLikelyHexId(idOrRef))
        {
            var byId = GetItemById(c, idOrRef);
            if (byId is not null) return byId;
        }
        if (TryParseRef(idOrRef, out var rp, out var n) && rp.Equals(pfx, StringComparison.OrdinalIgnoreCase))
            return GetItemByRef(c, projectId, rp, n);
        return GetItemById(c, idOrRef);
    }

    private static bool IsLikelyHexId(string s) => s.Length == 32 && s.All(c => char.IsAsciiHexDigit(c));

    public List<string> GetPredecessorIds(SqliteConnection c, string itemId)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT predecessor_id FROM work_item_dependencies
            WHERE successor_id = @id AND kind = 'finish_start';
            """;
        cmd.Parameters.AddWithValue("@id", itemId);
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    public bool HasPathSuccessorToPredecessor(SqliteConnection c, string successorId, string predecessorId, SqliteTransaction? tx = null)
    {
        if (successorId == predecessorId) return true;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        stack.Push(successorId);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (!visited.Add(cur)) continue;
            foreach (var pred in GetPredecessorIdsTx(c, cur, tx))
            {
                if (pred == predecessorId) return true;
                stack.Push(pred);
            }
        }
        return false;
    }

    private static List<string> GetPredecessorIdsTx(SqliteConnection c, string itemId, SqliteTransaction? tx)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT predecessor_id FROM work_item_dependencies WHERE successor_id = @id AND kind = 'finish_start';";
        cmd.Parameters.AddWithValue("@id", itemId);
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    public bool PredecessorsSatisfied(SqliteConnection c, string itemId)
    {
        foreach (var pred in GetPredecessorIds(c, itemId))
        {
            var p = GetItemById(c, pred);
            if (p is null) continue;
            if (p.Status is not ("done" or "cancelled")) return false;
        }
        return true;
    }

    public List<WorkItemRow> ListChildren(SqliteConnection c, string parentId)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM work_items WHERE parent_id = @p;";
        cmd.Parameters.AddWithValue("@p", parentId);
        using var r = cmd.ExecuteReader();
        var list = new List<WorkItemRow>();
        while (r.Read()) list.Add(ReadItem(r));
        return list;
    }

    public bool ChildrenAllTerminal(SqliteConnection c, string parentId)
    {
        foreach (var ch in ListChildren(c, parentId))
        {
            if (ch.Status is not ("done" or "cancelled")) return false;
        }
        return true;
    }

    public WorkItemRow InsertItem(SqliteConnection c, SqliteTransaction tx, WorkItemInsert ins)
    {
        var id = string.IsNullOrEmpty(ins.Id) ? Guid.NewGuid().ToString("N") : ins.Id!;
        var refNum = GetNextRefNumber(c, ins.ProjectId, tx);
        var now = Now();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO work_items(
              id, project_id, ref_number, client_key, path_hints, type, stream, discovered_from_work_item_id,
              snoozed_until, title, body, status, priority, parent_id, blocked_by_work_item_id, blocked_reason,
              external_ref, assigned_to, sort_order, completed_at, created_at, updated_at)
            VALUES(
              @id, @pid, @rn, @ck, @ph, @type, @stream, @discFrom, @snooze, @title, @body, @status, @pri, @parent,
              @bby, @breason, @ext, @assign, @sort, NULL, @c, @u);
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@pid", ins.ProjectId);
        cmd.Parameters.AddWithValue("@rn", refNum);
        cmd.Parameters.AddWithValue("@ck", (object?)ins.ClientKey ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ph", (object?)ins.PathHintsJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@type", ins.Type);
        cmd.Parameters.AddWithValue("@stream", ins.Stream);
        cmd.Parameters.AddWithValue("@discFrom", (object?)ins.DiscoveredFromId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@snooze", (object?)ins.SnoozedUntil ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@title", ins.Title);
        cmd.Parameters.AddWithValue("@body", (object?)ins.Body ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", ins.Status);
        cmd.Parameters.AddWithValue("@pri", ins.Priority);
        cmd.Parameters.AddWithValue("@parent", (object?)ins.ParentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bby", (object?)ins.BlockedById ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@breason", (object?)ins.BlockedReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ext", (object?)ins.ExternalRef ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@assign", (object?)ins.AssignedTo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sort", ins.SortOrder);
        cmd.Parameters.AddWithValue("@c", now);
        cmd.Parameters.AddWithValue("@u", now);
        cmd.ExecuteNonQuery();
        return GetItemById(c, id)!;
    }

    public void PatchItem(SqliteConnection c, SqliteTransaction? tx, string id,
        string? title, string? status, int? priority, string? parentId, string? blockedById, string? blockedReason,
        string? stream, string? snoozedUntil, bool clearSnooze, string? pathHintsJson, string? assignedTo)
    {
        var sets = new List<string> { "updated_at = @u" };
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.Parameters.AddWithValue("@u", Now());
        cmd.Parameters.AddWithValue("@id", id);
        if (title is not null) { sets.Add("title = @title"); cmd.Parameters.AddWithValue("@title", title); }
        if (status is not null)
        {
            sets.Add("status = @st");
            cmd.Parameters.AddWithValue("@st", status);
            if (status == "done")
            {
                sets.Add("completed_at = @ca");
                cmd.Parameters.AddWithValue("@ca", Now());
            }
            else
            {
                sets.Add("completed_at = NULL");
            }
        }
        if (priority is not null) { sets.Add("priority = @pri"); cmd.Parameters.AddWithValue("@pri", priority.Value); }
        if (parentId is not null)
        {
            sets.Add("parent_id = @par");
            cmd.Parameters.AddWithValue("@par", parentId.Length == 0 ? DBNull.Value : parentId);
        }
        if (blockedById is not null)
        {
            sets.Add("blocked_by_work_item_id = @bby");
            cmd.Parameters.AddWithValue("@bby", blockedById.Length == 0 ? DBNull.Value : blockedById);
        }
        if (blockedReason is not null)
        {
            sets.Add("blocked_reason = @br");
            cmd.Parameters.AddWithValue("@br", blockedReason.Length == 0 ? DBNull.Value : blockedReason);
        }
        if (stream is not null) { sets.Add("stream = @stream"); cmd.Parameters.AddWithValue("@stream", stream); }
        if (clearSnooze) sets.Add("snoozed_until = NULL");
        else if (snoozedUntil is not null) { sets.Add("snoozed_until = @sn"); cmd.Parameters.AddWithValue("@sn", snoozedUntil); }
        if (pathHintsJson is not null)
        {
            sets.Add("path_hints = @ph");
            cmd.Parameters.AddWithValue("@ph", pathHintsJson.Length == 0 ? DBNull.Value : pathHintsJson);
        }
        if (assignedTo is not null)
        {
            sets.Add("assigned_to = @asg");
            cmd.Parameters.AddWithValue("@asg", assignedTo.Length == 0 ? DBNull.Value : assignedTo);
        }
        cmd.CommandText = $"UPDATE work_items SET {string.Join(", ", sets)} WHERE id = @id;";
        cmd.ExecuteNonQuery();
    }

    public void SetStatus(SqliteConnection c, SqliteTransaction? tx, string id, string status, string? completedAt)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE work_items SET status = @st, completed_at = @ca, updated_at = @u WHERE id = @id;";
        cmd.Parameters.AddWithValue("@st", status);
        cmd.Parameters.AddWithValue("@ca", (object?)completedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@u", Now());
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void AddDependency(SqliteConnection c, SqliteTransaction tx, string projectId, string predId, string succId, string kind)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO work_item_dependencies(project_id, predecessor_id, successor_id, kind)
            VALUES(@pid, @p, @s, @k);
            """;
        cmd.Parameters.AddWithValue("@pid", projectId);
        cmd.Parameters.AddWithValue("@p", predId);
        cmd.Parameters.AddWithValue("@s", succId);
        cmd.Parameters.AddWithValue("@k", kind);
        cmd.ExecuteNonQuery();
    }

    public void RemoveDependency(SqliteConnection c, string predId, string succId, string kind)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM work_item_dependencies WHERE predecessor_id=@p AND successor_id=@s AND kind=@k;";
        cmd.Parameters.AddWithValue("@p", predId);
        cmd.Parameters.AddWithValue("@s", succId);
        cmd.Parameters.AddWithValue("@k", kind);
        cmd.ExecuteNonQuery();
    }

    public void AddNote(SqliteConnection c, SqliteTransaction? tx, string projectId, string itemId, string? actor, string body)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO work_item_notes(project_id, work_item_id, at, actor, body)
            VALUES(@pid, @iid, @at, @actor, @body);
            """;
        cmd.Parameters.AddWithValue("@pid", projectId);
        cmd.Parameters.AddWithValue("@iid", itemId);
        cmd.Parameters.AddWithValue("@at", Now());
        cmd.Parameters.AddWithValue("@actor", (object?)actor ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@body", body);
        cmd.ExecuteNonQuery();
    }

    public List<NoteRow> ListNotes(SqliteConnection c, string itemId, int limit)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT id, at, actor, body FROM work_item_notes
            WHERE work_item_id = @id ORDER BY id DESC LIMIT @lim;
            """;
        cmd.Parameters.AddWithValue("@id", itemId);
        cmd.Parameters.AddWithValue("@lim", limit);
        using var r = cmd.ExecuteReader();
        var list = new List<NoteRow>();
        while (r.Read())
            list.Add(new NoteRow(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3)));
        return list;
    }

    public List<WorkItemRow> ListItems(SqliteConnection c, string projectId, ItemListQuery q)
    {
        var sql = """
            SELECT w.* FROM work_items w
            JOIN projects p ON p.id = w.project_id
            WHERE w.project_id = @pid
            """;
        using var cmd = c.CreateCommand();
        cmd.Parameters.AddWithValue("@pid", projectId);
        if (q.Status is not null) { sql += " AND w.status = @st "; cmd.Parameters.AddWithValue("@st", q.Status); }
        if (q.Type is not null) { sql += " AND w.type = @tp "; cmd.Parameters.AddWithValue("@tp", q.Type); }
        if (q.ParentId is not null)
        {
            if (q.ParentId.Length == 0)
                sql += " AND w.parent_id IS NULL ";
            else
            {
                sql += " AND w.parent_id = @par ";
                cmd.Parameters.AddWithValue("@par", q.ParentId);
            }
        }
        if (q.Stream is not null) { sql += " AND w.stream = @str "; cmd.Parameters.AddWithValue("@str", q.Stream); }
        if (q.TitleContains is not null) { sql += " AND w.title LIKE @tc "; cmd.Parameters.AddWithValue("@tc", "%" + q.TitleContains + "%"); }
        if (q.RefPrefix is not null)
        {
            sql += " AND (UPPER(p.ref_prefix) || '-' || w.ref_number) LIKE @rp ";
            cmd.Parameters.AddWithValue("@rp", q.RefPrefix.Trim().ToUpperInvariant() + "-%");
        }
        sql += q.Sort == "updated" ? " ORDER BY w.updated_at DESC, w.ref_number ASC " : " ORDER BY w.priority ASC, w.ref_number ASC ";
        sql += " LIMIT @lim;";
        cmd.Parameters.AddWithValue("@lim", q.Limit <= 0 ? 500 : q.Limit);
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var list = new List<WorkItemRow>();
        while (r.Read()) list.Add(ReadItem(r));
        return list;
    }

    public List<WorkItemRow> AllItemsForProject(SqliteConnection c, string projectId)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM work_items WHERE project_id = @p;";
        cmd.Parameters.AddWithValue("@p", projectId);
        using var r = cmd.ExecuteReader();
        var list = new List<WorkItemRow>();
        while (r.Read()) list.Add(ReadItem(r));
        return list;
    }

    public List<(string PredecessorId, string SuccessorId, string Kind)> ListDependenciesForProject(SqliteConnection c, string projectId)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT predecessor_id, successor_id, kind FROM work_item_dependencies
            WHERE project_id = @p;
            """;
        cmd.Parameters.AddWithValue("@p", projectId);
        using var r = cmd.ExecuteReader();
        var list = new List<(string, string, string)>();
        while (r.Read())
            list.Add((r.GetString(0), r.GetString(1), r.GetString(2)));
        return list;
    }

    private static WorkItemRow ReadItem(SqliteDataReader r)
    {
        int O(string name) => r.GetOrdinal(name);
        string? N(string name) => r.IsDBNull(O(name)) ? null : r.GetString(O(name));
        return new WorkItemRow(
            r.GetString(O("id")), r.GetString(O("project_id")), r.GetInt32(O("ref_number")), N("client_key"), N("path_hints"),
            r.GetString(O("type")), r.GetString(O("stream")), N("discovered_from_work_item_id"), N("snoozed_until"),
            r.GetString(O("title")), N("body"), r.GetString(O("status")), r.GetInt32(O("priority")), N("parent_id"),
            N("blocked_by_work_item_id"), N("blocked_reason"), N("external_ref"), N("assigned_to"), r.GetInt32(O("sort_order")),
            N("completed_at"), r.GetString(O("created_at")), r.GetString(O("updated_at")));
    }

    public record WorkItemRow(
        string Id, string ProjectId, int RefNumber, string? ClientKey, string? PathHints, string Type, string Stream,
        string? DiscoveredFromId, string? SnoozedUntil, string Title, string? Body, string Status, int Priority,
        string? ParentId, string? BlockedById, string? BlockedReason, string? ExternalRef, string? AssignedTo,
        int SortOrder, string? CompletedAt, string CreatedAt, string UpdatedAt);

    public record NoteRow(long Id, string At, string? Actor, string Body);

    public record WorkItemInsert(
        string ProjectId,
        string? Id,
        string? ClientKey,
        string? PathHintsJson,
        string Type,
        string Stream,
        string? DiscoveredFromId,
        string? SnoozedUntil,
        string Title,
        string? Body,
        string Status,
        int Priority,
        string? ParentId,
        string? BlockedById,
        string? BlockedReason,
        string? ExternalRef,
        string? AssignedTo,
        int SortOrder);

    public record ItemListQuery(
        string? Status,
        string? Type,
        string? ParentId,
        string? Stream,
        string? TitleContains,
        string? RefPrefix,
        string Sort,
        int Limit);
}
