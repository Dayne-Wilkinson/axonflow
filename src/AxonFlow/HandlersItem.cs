using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AxonFlow;

internal static class HandlersItem
{
    internal const int MaxItemBodyBytes = 512_000;

    /// <summary>UTF-8 file body for item add/update; throws IOException on failure.</summary>
    internal static string ReadBodyFileUtf8(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) throw new FileNotFoundException("Body file not found.", full);
        var fi = new FileInfo(full);
        if (fi.Length > MaxItemBodyBytes)
            throw new InvalidOperationException($"Body file exceeds {MaxItemBodyBytes} bytes.");
        return File.ReadAllText(full, Encoding.UTF8);
    }

    public static void Register(RootCommand root)
    {
        var item = new Command("item", "Work items");
        var add = new Command("add", "Add a work item");
        var typeOpt = new Option<string>("--type") { IsRequired = true };
        var titleOpt = new Option<string>("--title") { IsRequired = true };
        var statusOpt = new Option<string>("--status", () => "backlog");
        var priorityOpt = new Option<int>("--priority", () => 100);
        var parentOpt = new Option<string?>("--parent");
        var clientKeyOpt = new Option<string?>("--client-key");
        var pathHintsOpt = new Option<string?>("--path-hints", "Comma-separated repo-relative paths");
        var streamOpt = new Option<string>("--stream", () => "plan");
        var discoveredOpt = new Option<string?>("--discovered-from");
        var noProvOpt = new Option<bool>("--no-provenance", () => false);
        var snoozeOpt = new Option<string?>("--snoozed-until");
        var blockedByOpt = new Option<string?>("--blocked-by");
        var blockedReasonOpt = new Option<string?>("--blocked-reason");
        var addBodyOpt = new Option<string?>("--body", "Item body (UTF-8); mutually exclusive with --body-file");
        var addBodyFileOpt = new Option<FileInfo?>("--body-file", "Read body from UTF-8 file; mutually exclusive with --body");
        add.AddOption(typeOpt);
        add.AddOption(titleOpt);
        add.AddOption(statusOpt);
        add.AddOption(priorityOpt);
        add.AddOption(parentOpt);
        add.AddOption(clientKeyOpt);
        add.AddOption(pathHintsOpt);
        add.AddOption(streamOpt);
        add.AddOption(discoveredOpt);
        add.AddOption(noProvOpt);
        add.AddOption(snoozeOpt);
        add.AddOption(blockedByOpt);
        add.AddOption(blockedReasonOpt);
        add.AddOption(addBodyOpt);
        add.AddOption(addBodyFileOpt);

        add.SetHandler(ctx => ItemAdd(
            ctx.ParseResult.GetValueForOption(typeOpt)!,
            ctx.ParseResult.GetValueForOption(titleOpt)!,
            ctx.ParseResult.GetValueForOption(statusOpt)!,
            ctx.ParseResult.GetValueForOption(priorityOpt),
            ctx.ParseResult.GetValueForOption(parentOpt),
            ctx.ParseResult.GetValueForOption(clientKeyOpt),
            ctx.ParseResult.GetValueForOption(pathHintsOpt),
            ctx.ParseResult.GetValueForOption(streamOpt)!,
            ctx.ParseResult.GetValueForOption(discoveredOpt),
            ctx.ParseResult.GetValueForOption(noProvOpt),
            ctx.ParseResult.GetValueForOption(snoozeOpt),
            ctx.ParseResult.GetValueForOption(blockedByOpt),
            ctx.ParseResult.GetValueForOption(blockedReasonOpt),
            ctx.ParseResult.GetValueForOption(addBodyOpt),
            ctx.ParseResult.GetValueForOption(addBodyFileOpt),
            ctx));

        var list = new Command("list", "List work items");
        var st = new Option<string?>("--status");
        var tp = new Option<string?>("--type");
        var par = new Option<string?>("--parent");
        var str = new Option<string?>("--stream");
        var tc = new Option<string?>("--title-contains");
        var rp = new Option<string?>("--ref-prefix");
        var asg = new Option<string?>("--assigned-to");
        var bc = new Option<string?>("--body-contains");
        var uaf = new Option<string?>("--updated-after", "ISO-8601 instant; items with updated_at >= this (UTC)");
        var sort = new Option<string>("--sort", () => "priority");
        var lim = new Option<int>("--limit", () => 200);
        list.AddOption(st);
        list.AddOption(tp);
        list.AddOption(par);
        list.AddOption(str);
        list.AddOption(tc);
        list.AddOption(rp);
        list.AddOption(asg);
        list.AddOption(bc);
        list.AddOption(uaf);
        list.AddOption(sort);
        list.AddOption(lim);
        list.SetHandler(ctx => ItemList(
            ctx.ParseResult.GetValueForOption(st),
            ctx.ParseResult.GetValueForOption(tp),
            ctx.ParseResult.GetValueForOption(par),
            ctx.ParseResult.GetValueForOption(str),
            ctx.ParseResult.GetValueForOption(tc),
            ctx.ParseResult.GetValueForOption(rp),
            ctx.ParseResult.GetValueForOption(asg),
            ctx.ParseResult.GetValueForOption(bc),
            ctx.ParseResult.GetValueForOption(uaf),
            ctx.ParseResult.GetValueForOption(sort)!,
            ctx.ParseResult.GetValueForOption(lim),
            ctx));

        var show = new Command("show", "Show one item");
        var idOpt = new Option<string?>("--id");
        var refOpt = new Option<string?>("--ref");
        var notesOpt = new Option<bool>("--notes", () => false);
        var notesLim = new Option<int>("--notes-limit", () => 20);
        show.AddOption(idOpt);
        show.AddOption(refOpt);
        show.AddOption(notesOpt);
        show.AddOption(notesLim);
        show.SetHandler(ctx => ItemShow(
            ctx.ParseResult.GetValueForOption(idOpt),
            ctx.ParseResult.GetValueForOption(refOpt),
            ctx.ParseResult.GetValueForOption(notesOpt),
            ctx.ParseResult.GetValueForOption(notesLim),
            ctx));

        var update = new Command("update", "Update fields");
        var uid = new Option<string?>("--id", "Work item id (exactly one of --id or --ref required)");
        var uref = new Option<string?>("--ref", "Work item ref e.g. AF-12 (exactly one of --id or --ref required)");
        var utitle = new Option<string?>("--title");
        var ustatus = new Option<string?>("--status");
        var upri = new Option<int?>("--priority");
        var upar = new Option<string?>("--parent");
        var ubby = new Option<string?>("--blocked-by");
        var ubr = new Option<string?>("--blocked-reason");
        var ustream = new Option<string?>("--stream");
        var usnooze = new Option<string?>("--snoozed-until");
        var uclearSnooze = new Option<bool>("--clear-snooze", () => false);
        var uph = new Option<string?>("--path-hints");
        var uasg = new Option<string?>("--assigned-to");
        var ubody = new Option<string?>("--body", "Set body (UTF-8); mutually exclusive with --body-file and --clear-body");
        var ubodyFile = new Option<FileInfo?>("--body-file", "Set body from UTF-8 file; mutually exclusive with --body and --clear-body");
        var uclearBody = new Option<bool>("--clear-body", () => false, "Clear stored body (SQL NULL)");
        update.AddOption(uid);
        update.AddOption(uref);
        update.AddOption(utitle);
        update.AddOption(ustatus);
        update.AddOption(upri);
        update.AddOption(upar);
        update.AddOption(ubby);
        update.AddOption(ubr);
        update.AddOption(ustream);
        update.AddOption(usnooze);
        update.AddOption(uclearSnooze);
        update.AddOption(uph);
        update.AddOption(uasg);
        update.AddOption(ubody);
        update.AddOption(ubodyFile);
        update.AddOption(uclearBody);
        update.SetHandler(ctx => ItemUpdate(
            ctx.ParseResult.GetValueForOption(uid),
            ctx.ParseResult.GetValueForOption(uref),
            ctx.ParseResult.GetValueForOption(utitle),
            ctx.ParseResult.GetValueForOption(ustatus),
            ctx.ParseResult.GetValueForOption(upri),
            ctx.ParseResult.GetValueForOption(upar),
            ctx.ParseResult.GetValueForOption(ubby),
            ctx.ParseResult.GetValueForOption(ubr),
            ctx.ParseResult.GetValueForOption(ustream),
            ctx.ParseResult.GetValueForOption(usnooze),
            ctx.ParseResult.GetValueForOption(uclearSnooze),
            ctx.ParseResult.GetValueForOption(uph),
            ctx.ParseResult.GetValueForOption(uasg),
            ctx.ParseResult.GetValueForOption(ubody),
            ctx.ParseResult.GetValueForOption(ubodyFile),
            ctx.ParseResult.GetValueForOption(uclearBody),
            ctx));

        var note = new Command("note", "Append note");
        var noteAdd = new Command("add", "Add note to item");
        var nid = new Option<string?>("--id");
        var nref = new Option<string?>("--ref");
        var nbody = new Option<string>("--body") { IsRequired = true };
        var nactor = new Option<string?>("--actor");
        noteAdd.AddOption(nid);
        noteAdd.AddOption(nref);
        noteAdd.AddOption(nbody);
        noteAdd.AddOption(nactor);
        noteAdd.SetHandler(ctx => ItemNoteAdd(
            ctx.ParseResult.GetValueForOption(nid),
            ctx.ParseResult.GetValueForOption(nref),
            ctx.ParseResult.GetValueForOption(nbody)!,
            ctx.ParseResult.GetValueForOption(nactor),
            ctx));
        note.AddCommand(noteAdd);

        var defer = new Command("defer", "Snooze item until time");
        var did = new Option<string?>("--id");
        var dref = new Option<string?>("--ref");
        var duntil = new Option<string?>("--until");
        var dclear = new Option<bool>("--clear", () => false);
        defer.AddOption(did);
        defer.AddOption(dref);
        defer.AddOption(duntil);
        defer.AddOption(dclear);
        defer.SetHandler(ctx => ItemDefer(
            ctx.ParseResult.GetValueForOption(did),
            ctx.ParseResult.GetValueForOption(dref),
            ctx.ParseResult.GetValueForOption(duntil),
            ctx.ParseResult.GetValueForOption(dclear),
            ctx));

        var import = new Command("import", "Bulk import JSON from stdin or --file");
        var fileOpt = new Option<FileInfo?>("--file");
        import.AddOption(fileOpt);
        import.SetHandler(ctx => ItemImport(ctx.ParseResult.GetValueForOption(fileOpt), ctx));

        var start = new Command("start", "Claim and move to in_progress");
        var sid = new Option<string?>("--id");
        var sref = new Option<string?>("--ref");
        var sassign = new Option<string>("--assignee") { IsRequired = true };
        start.AddOption(sid);
        start.AddOption(sref);
        start.AddOption(sassign);
        start.SetHandler(ctx => ItemStart(
            ctx.ParseResult.GetValueForOption(sid),
            ctx.ParseResult.GetValueForOption(sref),
            ctx.ParseResult.GetValueForOption(sassign)!,
            ctx));

        var next = new Command("next", "Pick next actionable item");
        var nassign = new Option<string?>("--assignee");
        var prefE = new Option<bool>("--prefer-emergent", () => false);
        next.AddOption(nassign);
        next.AddOption(prefE);
        next.SetHandler(ctx => ItemNext(
            ctx.ParseResult.GetValueForOption(nassign),
            ctx.ParseResult.GetValueForOption(prefE),
            ctx));

        var complete = new Command("complete", "Mark done");
        var cid = new Option<string?>("--id");
        var cref = new Option<string?>("--ref");
        var cforce = new Option<bool>("--force", () => false);
        complete.AddOption(cid);
        complete.AddOption(cref);
        complete.AddOption(cforce);
        complete.SetHandler(ctx => ItemComplete(
            ctx.ParseResult.GetValueForOption(cid),
            ctx.ParseResult.GetValueForOption(cref),
            ctx.ParseResult.GetValueForOption(cforce),
            ctx));

        var cancel = new Command("cancel", "Mark cancelled");
        var xid = new Option<string?>("--id");
        var xref = new Option<string?>("--ref");
        cancel.AddOption(xid);
        cancel.AddOption(xref);
        cancel.SetHandler(ctx => ItemCancel(
            ctx.ParseResult.GetValueForOption(xid),
            ctx.ParseResult.GetValueForOption(xref),
            ctx));

        var reopen = new Command("reopen", "Move done/cancelled back to ready");
        var rid = new Option<string?>("--id");
        var rref = new Option<string?>("--ref");
        var rforce = new Option<bool>("--force", () => false);
        reopen.AddOption(rid);
        reopen.AddOption(rref);
        reopen.AddOption(rforce);
        reopen.SetHandler(ctx => ItemReopen(
            ctx.ParseResult.GetValueForOption(rid),
            ctx.ParseResult.GetValueForOption(rref),
            ctx.ParseResult.GetValueForOption(rforce),
            ctx));

        item.AddCommand(add);
        item.AddCommand(list);
        item.AddCommand(show);
        item.AddCommand(update);
        item.AddCommand(note);
        item.AddCommand(defer);
        item.AddCommand(import);
        item.AddCommand(start);
        item.AddCommand(next);
        item.AddCommand(complete);
        item.AddCommand(cancel);
        item.AddCommand(reopen);
        root.AddCommand(item);
    }

    private static bool OpenDb(InvocationContext ctx, out Store store, out SqliteConnection c, out string? projectId)
    {
        if (!CliRoot.TryOpenWorkload(ctx, out store!, out c!, out var pid))
        {
            projectId = null;
            return false;
        }

        projectId = pid;
        return true;
    }

    private static void Err(InvocationContext ctx, string code, string msg)
    {
        if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption))
            JsonOut.WriteErr(code, msg);
        else
            Console.Error.WriteLine(msg);
        ctx.ExitCode = 3;
    }

    private static Store.WorkItemRow? Resolve(Store store, SqliteConnection c, string projectId, string? id, string? @ref)
    {
        if (!string.IsNullOrEmpty(id)) return store.ResolveItem(c, projectId, id!);
        if (!string.IsNullOrEmpty(@ref)) return store.ResolveItem(c, projectId, @ref!);
        return null;
    }

    public static void ItemAdd(string type, string title, string status, int priority, string? parent, string? clientKey,
        string? pathHints, string stream, string? discoveredFrom, bool noProvenance, string? snoozedUntil,
        string? blockedBy, string? blockedReason, string? bodyInline, FileInfo? bodyFile, InvocationContext ctx)
    {
        if (!OpenDb(ctx, out var store, out var c, out var pid) || pid is null) return;
        var json = ctx.ParseResult.GetValueForOption(CliRoot.JsonOption);
        var dry = ctx.ParseResult.GetValueForOption(CliRoot.DryRunOption);

        if (stream == "emergent" && string.IsNullOrEmpty(discoveredFrom) && !noProvenance)
        {
            if (json) JsonOut.WriteErr("validation", "emergent items require --discovered-from or --no-provenance");
            else Console.Error.WriteLine("emergent items require --discovered-from or --no-provenance");
            ctx.ExitCode = 2;
            return;
        }
        if (status == "blocked" && string.IsNullOrEmpty(blockedReason) && string.IsNullOrEmpty(blockedBy))
        {
            if (json) JsonOut.WriteErr("validation", "blocked status requires --blocked-reason and/or --blocked-by");
            else Console.Error.WriteLine("blocked status requires --blocked-reason and/or --blocked-by");
            ctx.ExitCode = 2;
            return;
        }

        if (bodyInline is not null && bodyFile is not null)
        {
            if (json) JsonOut.WriteErr("validation", "use only one of --body or --body-file");
            else Console.Error.WriteLine("use only one of --body or --body-file");
            ctx.ExitCode = 2;
            return;
        }

        string? bodyText = null;
        try
        {
            if (bodyFile is not null)
                bodyText = ReadBodyFileUtf8(bodyFile.FullName);
            else if (bodyInline is not null)
            {
                if (Encoding.UTF8.GetByteCount(bodyInline) > MaxItemBodyBytes)
                {
                    if (json) JsonOut.WriteErr("validation", $"body exceeds {MaxItemBodyBytes} UTF-8 bytes");
                    else Console.Error.WriteLine($"body exceeds {MaxItemBodyBytes} UTF-8 bytes");
                    ctx.ExitCode = 2;
                    return;
                }
                bodyText = bodyInline;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            if (json) JsonOut.WriteErr("validation", ex.Message);
            else Console.Error.WriteLine(ex.Message);
            ctx.ExitCode = 2;
            return;
        }

        if (!string.IsNullOrEmpty(clientKey))
        {
            var existing = store.GetItemByClientKey(c, pid, clientKey);
            if (existing is not null)
            {
                if (json) JsonOut.WriteOk(ItemJson.ItemDto(store, c, existing));
                else JsonOut.WriteText($"{store.GetProjectRefPrefix(c, pid)}-{existing.RefNumber}\t{existing.Title}");
                ctx.ExitCode = 0;
                return;
            }
        }

        string? parentId = null;
        if (!string.IsNullOrEmpty(parent))
        {
            var pRow = store.ResolveItem(c, pid, parent!);
            if (pRow is null) { Err(ctx, "not_found", "Parent not found"); return; }
            parentId = pRow.Id;
        }
        string? discId = null;
        if (!string.IsNullOrEmpty(discoveredFrom))
        {
            var dRow = store.ResolveItem(c, pid, discoveredFrom!);
            if (dRow is null) { Err(ctx, "not_found", "discovered-from not found"); return; }
            discId = dRow.Id;
        }
        string? bbyId = null;
        if (!string.IsNullOrEmpty(blockedBy))
        {
            var b = store.ResolveItem(c, pid, blockedBy!);
            if (b is null) { Err(ctx, "not_found", "blocked-by not found"); return; }
            bbyId = b.Id;
        }

        string? pathJson = null;
        if (!string.IsNullOrEmpty(pathHints))
        {
            var parts = pathHints.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            pathJson = JsonSerializer.Serialize(parts);
        }

        if (dry)
        {
            if (json) JsonOut.WriteOk(new { plannedCreates = 1, type, title, status });
            else JsonOut.WriteText("[dry-run] would add item");
            ctx.ExitCode = 0;
            return;
        }

        using var tx = c.BeginTransaction();
        var ins = new Store.WorkItemInsert(pid, null, clientKey, pathJson, type, stream, discId, snoozedUntil, title,
            bodyText, status, priority, parentId, bbyId, blockedReason, null, null, 0);
        var row = store.InsertItem(c, tx, ins);
        tx.Commit();
        if (json) JsonOut.WriteOk(ItemJson.ItemDto(store, c, row));
        else JsonOut.WriteText($"{store.GetProjectRefPrefix(c, pid)}-{row.RefNumber}\t{row.Title}");
        ctx.ExitCode = 0;
    }

    public static void ItemList(string? status, string? type, string? parent, string? stream, string? titleContains, string? refPrefix,
        string? assignedTo, string? bodyContains, string? updatedAfter, string sort, int limit, InvocationContext ctx)
    {
        if (!OpenDb(ctx, out var store, out var c, out var pid) || pid is null) return;
        string? parentIdForQuery = parent;
        if (!string.IsNullOrEmpty(parent))
        {
            var pRow = store.ResolveItem(c, pid, parent);
            if (pRow is null)
            {
                Err(ctx, "not_found", "Parent not found");
                return;
            }
            parentIdForQuery = pRow.Id;
        }
        string? updatedIso = null;
        if (!string.IsNullOrEmpty(updatedAfter))
        {
            if (!DateTimeOffset.TryParse(updatedAfter, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var dto))
            {
                Err(ctx, "validation", "--updated-after must be a valid ISO-8601 date/time");
                return;
            }
            updatedIso = dto.ToString("O");
        }
        var q = new Store.ItemListQuery(status, type, parentIdForQuery, stream, titleContains, refPrefix,
            string.IsNullOrEmpty(assignedTo) ? null : assignedTo,
            string.IsNullOrEmpty(bodyContains) ? null : bodyContains,
            updatedIso,
            sort, limit);
        var rows = store.ListItems(c, pid, q);
        var json = ctx.ParseResult.GetValueForOption(CliRoot.JsonOption);
        if (json) JsonOut.WriteOk(rows.Select(w => ItemJson.ItemDto(store, c, w)).ToList());
        else
            foreach (var w in rows)
                JsonOut.WriteText($"{store.GetProjectRefPrefix(c, pid)}-{w.RefNumber}\t{w.Status}\t{w.Type}\t{w.Title}");
        ctx.ExitCode = 0;
    }

    public static void ItemShow(string? id, string? @ref, bool notes, int notesLimit, InvocationContext ctx)
    {
        if (!OpenDb(ctx, out var store, out var c, out var pid) || pid is null) return;
        var row = Resolve(store, c, pid, id, @ref);
        if (row is null) { Err(ctx, "not_found", "Item not found"); return; }
        var json = ctx.ParseResult.GetValueForOption(CliRoot.JsonOption);
        if (json)
            JsonOut.WriteOk(ItemJson.BuildItemShowEnvelope(store, c, pid, row, notes, notesLimit));
        else
        {
            JsonOut.WriteText($"{store.GetProjectRefPrefix(c, pid)}-{row.RefNumber}\t{row.Status}\t{row.Title}");
            if (notes)
                foreach (var n in store.ListNotes(c, row.Id, notesLimit))
                    JsonOut.WriteText($"  [{n.At}] {n.Body}");
        }
        ctx.ExitCode = 0;
    }

    public static void ItemUpdate(string? id, string? @ref, string? title, string? status, int? priority, string? parent, string? blockedBy, string? blockedReason,
        string? stream, string? snoozedUntil, bool clearSnooze, string? pathHints, string? assignedTo,
        string? bodyInline, FileInfo? bodyFile, bool clearBody, InvocationContext ctx)
    {
        if (!OpenDb(ctx, out var store, out var c, out var pid) || pid is null) return;
        var hasId = !string.IsNullOrEmpty(id);
        var hasRef = !string.IsNullOrEmpty(@ref);
        if (hasId == hasRef)
        {
            if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption)) JsonOut.WriteErr("validation", "exactly one of --id or --ref is required");
            else Console.Error.WriteLine("exactly one of --id or --ref is required");
            ctx.ExitCode = 2;
            return;
        }
        var row = Resolve(store, c, pid, id, @ref);
        if (row is null) { Err(ctx, "not_found", "Item not found"); return; }
        if (status == "blocked" && string.IsNullOrEmpty(blockedReason) && string.IsNullOrEmpty(blockedBy))
        {
            if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption)) JsonOut.WriteErr("validation", "blocked requires reason or blocked-by");
            else Console.Error.WriteLine("blocked requires reason or blocked-by");
            ctx.ExitCode = 2;
            return;
        }
        string? parentId = parent;
        if (parent is not null)
        {
            if (parent.Length == 0) parentId = "";
            else
            {
                var pRow = store.ResolveItem(c, pid, parent);
                if (pRow is null) { Err(ctx, "not_found", "parent not found"); return; }
                parentId = pRow.Id;
            }
        }
        string? bby = blockedBy;
        if (blockedBy is not null && blockedBy.Length > 0)
        {
            var b = store.ResolveItem(c, pid, blockedBy);
            if (b is null) { Err(ctx, "not_found", "blocked-by not found"); return; }
            bby = b.Id;
        }
        string? ph = pathHints;
        if (pathHints is not null && pathHints.Length > 0)
        {
            var parts = pathHints.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            ph = JsonSerializer.Serialize(parts);
        }
        var bodySources = (bodyInline is not null ? 1 : 0) + (bodyFile is not null ? 1 : 0) + (clearBody ? 1 : 0);
        if (bodySources > 1)
        {
            if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption)) JsonOut.WriteErr("validation", "use at most one of --body, --body-file, --clear-body");
            else Console.Error.WriteLine("use at most one of --body, --body-file, --clear-body");
            ctx.ExitCode = 2;
            return;
        }
        string? bodyPatch = null;
        try
        {
            if (clearBody) bodyPatch = "";
            else if (bodyFile is not null) bodyPatch = ReadBodyFileUtf8(bodyFile.FullName);
            else if (bodyInline is not null)
            {
                if (Encoding.UTF8.GetByteCount(bodyInline) > MaxItemBodyBytes)
                {
                    if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption)) JsonOut.WriteErr("validation", $"body exceeds {MaxItemBodyBytes} UTF-8 bytes");
                    else Console.Error.WriteLine($"body exceeds {MaxItemBodyBytes} UTF-8 bytes");
                    ctx.ExitCode = 2;
                    return;
                }
                bodyPatch = bodyInline;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption)) JsonOut.WriteErr("validation", ex.Message);
            else Console.Error.WriteLine(ex.Message);
            ctx.ExitCode = 2;
            return;
        }

        if (ctx.ParseResult.GetValueForOption(CliRoot.DryRunOption))
        {
            if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption)) JsonOut.WriteOk(new { plannedUpdate = row.Id });
            ctx.ExitCode = 0;
            return;
        }
        store.PatchItem(c, null, row.Id, title, status, priority, parentId, bby, blockedReason, stream, snoozedUntil, clearSnooze, ph, assignedTo, bodyPatch);
        var updated = store.GetItemById(c, row.Id)!;
        if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption)) JsonOut.WriteOk(ItemJson.ItemDto(store, c, updated));
        ctx.ExitCode = 0;
    }

    public static void ItemNoteAdd(string? id, string? @ref, string body, string? actor, InvocationContext ctx)
    {
        if (!OpenDb(ctx, out var store, out var c, out var pid) || pid is null) return;
        var row = Resolve(store, c, pid, id, @ref);
        if (row is null) { Err(ctx, "not_found", "Item not found"); return; }
        if (ctx.ParseResult.GetValueForOption(CliRoot.DryRunOption)) { ctx.ExitCode = 0; return; }
        store.AddNote(c, null, pid, row.Id, actor, body);
        if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption)) JsonOut.WriteOk(new { ok = true });
        ctx.ExitCode = 0;
    }

    public static void ItemDefer(string? id, string? @ref, string? until, bool clear, InvocationContext ctx)
    {
        if (!OpenDb(ctx, out var store, out var c, out var pid) || pid is null) return;
        var row = Resolve(store, c, pid, id, @ref);
        if (row is null) { Err(ctx, "not_found", "Item not found"); return; }
        if (clear)
            store.PatchItem(c, null, row.Id, null, null, null, null, null, null, null, null, true, null, null, null);
        else if (!string.IsNullOrEmpty(until))
            store.PatchItem(c, null, row.Id, null, null, null, null, null, null, null, until, false, null, null, null);
        else
        {
            if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption)) JsonOut.WriteErr("validation", "use --until or --clear");
            ctx.ExitCode = 2;
            return;
        }
        if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption)) JsonOut.WriteOk(ItemJson.ItemDto(store, c, store.GetItemById(c, row.Id)!));
        ctx.ExitCode = 0;
    }

    public static void ItemImport(FileInfo? file, InvocationContext ctx)
    {
        if (!OpenDb(ctx, out var store, out var c, out var pid) || pid is null) return;
        string json;
        if (file is not null)
            json = File.ReadAllText(file.FullName);
        else
            json = new StreamReader(Console.OpenStandardInput()).ReadToEnd();
        ImportPayload? payload;
        try { payload = JsonSerializer.Deserialize<ImportPayload>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch
        {
            Err(ctx, "validation", "Invalid import JSON");
            ctx.ExitCode = 2;
            return;
        }
        if (payload?.Items is null || payload.Items.Count == 0)
        {
            Err(ctx, "validation", "import requires items array");
            ctx.ExitCode = 2;
            return;
        }
        var dry = ctx.ParseResult.GetValueForOption(CliRoot.DryRunOption);
        var useJson = ctx.ParseResult.GetValueForOption(CliRoot.JsonOption);

        var tempMap = new Dictionary<string, string>(StringComparer.Ordinal);
        using var tx = c.BeginTransaction();
        try
        {
            foreach (var it in payload.Items)
            {
                if (!string.IsNullOrEmpty(it.ClientKey))
                {
                    var ex = store.GetItemByClientKey(c, pid, it.ClientKey);
                    if (ex is not null)
                    {
                        if (!string.IsNullOrEmpty(it.TempId))
                            tempMap[it.TempId] = ex.Id;
                        continue;
                    }
                }
                var stream = string.IsNullOrEmpty(it.Stream) ? "plan" : it.Stream!;
                string? discId = null;
                if (!string.IsNullOrEmpty(it.DiscoveredFromTempId) && tempMap.TryGetValue(it.DiscoveredFromTempId, out var did))
                    discId = did;
                string? parentId = null;
                if (!string.IsNullOrEmpty(it.ParentTempId) && tempMap.TryGetValue(it.ParentTempId, out var pidv))
                    parentId = pidv;
                else if (!string.IsNullOrEmpty(it.ParentId))
                    parentId = it.ParentId;

                if (stream == "emergent" && discId is null && string.IsNullOrEmpty(it.DiscoveredFromClientKey))
                {
                    tx.Rollback();
                    if (useJson) JsonOut.WriteErr("validation", "emergent row needs discoveredFromTempId or discoveredFromClientKey");
                    ctx.ExitCode = 2;
                    return;
                }
                if (!string.IsNullOrEmpty(it.DiscoveredFromClientKey))
                {
                    var d = store.GetItemByClientKey(c, pid, it.DiscoveredFromClientKey);
                    if (d is not null) discId = d.Id;
                }

                var ins = new Store.WorkItemInsert(pid, null, it.ClientKey, it.PathHints is { Count: > 0 } ? JsonSerializer.Serialize(it.PathHints) : null,
                    it.Type ?? "task", stream, discId, null, it.Title ?? "untitled", it.Body, it.Status ?? "backlog", it.Priority ?? 100, parentId,
                    null, null, null, null, 0);
                var row = store.InsertItem(c, tx, ins);
                if (!string.IsNullOrEmpty(it.TempId))
                    tempMap[it.TempId] = row.Id;
            }
            foreach (var d in payload.Dependencies ?? [])
            {
                if (!tempMap.TryGetValue(d.PredecessorTempId ?? "", out var p) ||
                    !tempMap.TryGetValue(d.SuccessorTempId ?? "", out var s))
                    continue;
                if (store.HasPathSuccessorToPredecessor(c, s, p, tx))
                {
                    tx.Rollback();
                    if (useJson) JsonOut.WriteErr("conflict", "dependency would create cycle");
                    ctx.ExitCode = 4;
                    return;
                }
                store.AddDependency(c, tx, pid, p, s, string.IsNullOrEmpty(d.Kind) ? "finish_start" : d.Kind!);
            }
            if (dry) tx.Rollback();
            else tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        if (useJson) JsonOut.WriteOk(new { tempIdMap = tempMap });
        ctx.ExitCode = 0;
    }

    public static void ItemStart(string? id, string? @ref, string assignee, InvocationContext ctx)
    {
        if (!OpenDb(ctx, out var store, out var c, out var pid) || pid is null) return;
        var row = Resolve(store, c, pid, id, @ref);
        if (row is null) { Err(ctx, "not_found", "Item not found"); return; }
        if (!string.IsNullOrEmpty(row.AssignedTo) && row.AssignedTo != assignee)
        {
            if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption)) JsonOut.WriteErr("claim_conflict", "Item claimed by another assignee");
            ctx.ExitCode = 5;
            return;
        }
        if (ctx.ParseResult.GetValueForOption(CliRoot.DryRunOption)) { ctx.ExitCode = 0; return; }
        store.PatchItem(c, null, row.Id, null, "in_progress", null, null, null, null, null, null, true, null, assignee, null);
        if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption)) JsonOut.WriteOk(ItemJson.ItemDto(store, c, store.GetItemById(c, row.Id)!));
        ctx.ExitCode = 0;
    }

    public static void ItemNext(string? assignee, bool preferEmergent, InvocationContext ctx)
    {
        if (!OpenDb(ctx, out var store, out var c, out var pid) || pid is null) return;
        var now = DateTimeOffset.UtcNow;
        var all = store.ListItems(c, pid, new Store.ItemListQuery(null, null, null, null, null, null, null, null, null, "priority", 2000));
        var excluded = new List<object>();
        var candidates = new List<Store.WorkItemRow>();
        foreach (Store.WorkItemRow w in all)
        {
            if (w.Status is not ("backlog" or "ready")) { excluded.Add(new { @ref = Ref(store, c, pid, w), reason = "status" }); continue; }
            if (!string.IsNullOrEmpty(w.SnoozedUntil) && DateTimeOffset.TryParse(w.SnoozedUntil, out var sn) && sn > now)
            { excluded.Add(new { @ref = Ref(store, c, pid, w), reason = "snoozed" }); continue; }
            if (!string.IsNullOrEmpty(assignee) && !string.IsNullOrEmpty(w.AssignedTo) && w.AssignedTo != assignee)
            { excluded.Add(new { @ref = Ref(store, c, pid, w), reason = "assignee" }); continue; }
            if (!store.PredecessorsSatisfied(c, w.Id))
            { excluded.Add(new { @ref = Ref(store, c, pid, w), reason = "unfinished_predecessor" }); continue; }
            candidates.Add(w);
        }
        IOrderedEnumerable<Store.WorkItemRow> ordered = preferEmergent
            ? candidates.OrderBy(x => x.Stream == "emergent" ? 0 : 1).ThenBy(x => x.Priority).ThenBy(x => x.RefNumber)
            : candidates.OrderBy(x => x.Priority).ThenBy(x => x.RefNumber);
        var top = ordered.FirstOrDefault();
        var topN = ordered.Take(10).ToList();
        if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption))
        {
            JsonOut.WriteOk(new
            {
                picked = top is null ? null : ItemJson.ItemDto(store, c, top),
                candidates = topN.Select(x => ItemJson.ItemDto(store, c, x)).ToList(),
                excluded
            });
        }
        else if (top is not null)
            JsonOut.WriteText($"{store.GetProjectRefPrefix(c, pid)}-{top.RefNumber}\t{top.Title}");
        ctx.ExitCode = 0;
    }

    private static string Ref(Store store, SqliteConnection c, string projectId, Store.WorkItemRow w) =>
        $"{store.GetProjectRefPrefix(c, projectId)}-{w.RefNumber}";

    public static void ItemComplete(string? id, string? @ref, bool force, InvocationContext ctx)
    {
        if (!OpenDb(ctx, out var store, out var c, out var pid) || pid is null) return;
        var row = Resolve(store, c, pid, id, @ref);
        if (row is null) { Err(ctx, "not_found", "Item not found"); return; }
        if (!force && !store.ChildrenAllTerminal(c, row.Id))
        {
            if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption)) JsonOut.WriteErr("validation", "children not terminal; use --force");
            ctx.ExitCode = 2;
            return;
        }
        if (!force && !store.PredecessorsSatisfied(c, row.Id))
        {
            if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption)) JsonOut.WriteErr("validation", "predecessors not satisfied; use --force");
            ctx.ExitCode = 2;
            return;
        }
        if (ctx.ParseResult.GetValueForOption(CliRoot.DryRunOption)) { ctx.ExitCode = 0; return; }
        store.SetStatus(c, null, row.Id, "done", Store.Now());
        if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption)) JsonOut.WriteOk(ItemJson.ItemDto(store, c, store.GetItemById(c, row.Id)!));
        ctx.ExitCode = 0;
    }

    public static void ItemCancel(string? id, string? @ref, InvocationContext ctx)
    {
        if (!OpenDb(ctx, out var store, out var c, out var pid) || pid is null) return;
        var row = Resolve(store, c, pid, id, @ref);
        if (row is null) { Err(ctx, "not_found", "Item not found"); return; }
        if (ctx.ParseResult.GetValueForOption(CliRoot.DryRunOption)) { ctx.ExitCode = 0; return; }
        store.SetStatus(c, null, row.Id, "cancelled", null);
        if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption)) JsonOut.WriteOk(ItemJson.ItemDto(store, c, store.GetItemById(c, row.Id)!));
        ctx.ExitCode = 0;
    }

    public static void ItemReopen(string? id, string? @ref, bool force, InvocationContext ctx)
    {
        if (!OpenDb(ctx, out var store, out var c, out var pid) || pid is null) return;
        var row = Resolve(store, c, pid, id, @ref);
        if (row is null) { Err(ctx, "not_found", "Item not found"); return; }
        if (row.Status is not ("done" or "cancelled"))
        {
            if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption)) JsonOut.WriteErr("validation", "can only reopen done/cancelled");
            ctx.ExitCode = 2;
            return;
        }
        if (ctx.ParseResult.GetValueForOption(CliRoot.DryRunOption)) { ctx.ExitCode = 0; return; }
        store.SetStatus(c, null, row.Id, "ready", null);
        if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption)) JsonOut.WriteOk(ItemJson.ItemDto(store, c, store.GetItemById(c, row.Id)!));
        ctx.ExitCode = 0;
    }

    private sealed class ImportPayload
    {
        public List<ImportItem>? Items { get; set; }
        public List<ImportDep>? Dependencies { get; set; }
    }

    private sealed class ImportItem
    {
        public string? TempId { get; set; }
        public string? ClientKey { get; set; }
        public string? Type { get; set; }
        public string? Title { get; set; }
        public string? Body { get; set; }
        public string? Status { get; set; }
        public int? Priority { get; set; }
        public string? ParentTempId { get; set; }
        public string? ParentId { get; set; }
        public string? Stream { get; set; }
        public string? DiscoveredFromTempId { get; set; }
        public string? DiscoveredFromClientKey { get; set; }
        public List<string>? PathHints { get; set; }
    }

    private sealed class ImportDep
    {
        public string? PredecessorTempId { get; set; }
        public string? SuccessorTempId { get; set; }
        public string? Kind { get; set; }
    }
}
