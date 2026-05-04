using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace AxonFlow;

public static class DashboardSnapshot
{
    public sealed record BuildResult(string Json, string PageTitle, string RefScopeHint, int ItemCount, bool MultiProject);

    public static BuildResult Build(Store store, SqliteConnection c, string projectSlug, bool allProjects, JsonSerializerOptions jsonOpts)
    {
        if (allProjects)
        {
            var rootNode = BuildSnapshotV2(store, c, projectSlug, jsonOpts);
            var json = rootNode.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
            return new BuildResult(json, "All projects", "multi-project", CountItemsInV2(rootNode), true);
        }

        var pid = store.GetProjectId(c, projectSlug);
        if (pid is null)
            throw new InvalidOperationException($"Project not found for slug '{projectSlug}'.");

        var items = store.AllItemsForProject(c, pid);
        var pfx = store.GetProjectRefPrefix(c, pid);
        var projectDisplayName = store.ListProjects(c).FirstOrDefault(r => r.Slug == projectSlug).Name;
        var deps = store.ListDependenciesForProject(c, pid);
        var byId = items.ToDictionary(x => x.Id, StringComparer.Ordinal);
        string RefOf(string id) =>
            byId.TryGetValue(id, out var w) ? $"{pfx}-{w.RefNumber}" : id;

        var depDtos = deps.Select(d => new
        {
            predecessorRef = RefOf(d.PredecessorId),
            successorRef = RefOf(d.SuccessorId),
            kind = d.Kind
        }).ToList();

        var snapshot = new
        {
            schemaVersion = 1,
            generatedAt = DateTimeOffset.UtcNow.ToString("O"),
            project = new { slug = projectSlug, name = projectDisplayName, refPrefix = pfx },
            items = items.Select(w => ItemJson.ItemDto(store, c, w)).ToList(),
            dependencies = depDtos
        };
        var jsonStr = JsonSerializer.Serialize(snapshot, jsonOpts);
        var pageTitle = string.IsNullOrWhiteSpace(projectDisplayName) ? projectSlug : projectDisplayName;
        return new BuildResult(jsonStr, pageTitle, $"{pfx}-*", items.Count, false);
    }

    private static int CountItemsInV2(JsonObject root)
    {
        var bySlug = root["itemsByProjectSlug"] as JsonObject;
        if (bySlug is null) return 0;
        var n = 0;
        foreach (var prop in bySlug)
        {
            if (prop.Value is not JsonObject o) continue;
            if (o["items"] is JsonArray arr) n += arr.Count;
        }
        return n;
    }

    private static JsonObject BuildSnapshotV2(Store store, SqliteConnection c, string defaultSlug, JsonSerializerOptions jsonOpts)
    {
        var rows = store.ListProjects(c);
        var itemsBySlug = new JsonObject();
        var projectsArr = new JsonArray();
        foreach (var (id, name, slug, refPrefix) in rows)
        {
            projectsArr.Add(JsonSerializer.SerializeToNode(new { id, name, slug, refPrefix }, jsonOpts));
            var projectId = store.GetProjectId(c, slug);
            if (projectId is null) continue;
            var items = store.AllItemsForProject(c, projectId);
            var deps = store.ListDependenciesForProject(c, projectId);
            var pfx = store.GetProjectRefPrefix(c, projectId);
            var byId = items.ToDictionary(x => x.Id, StringComparer.Ordinal);
            string RefOf(string id) =>
                byId.TryGetValue(id, out var w) ? $"{pfx}-{w.RefNumber}" : id;

            var itemsArr = new JsonArray();
            foreach (var w in items)
            {
                var o = JsonSerializer.SerializeToNode(ItemJson.ItemDto(store, c, w), jsonOpts)!.AsObject();
                o["projectSlug"] = slug;
                o["projectName"] = name;
                itemsArr.Add(o);
            }
            var depsArr = new JsonArray();
            foreach (var d in deps)
            {
                depsArr.Add(JsonSerializer.SerializeToNode(new
                {
                    predecessorRef = RefOf(d.PredecessorId),
                    successorRef = RefOf(d.SuccessorId),
                    kind = d.Kind
                }, jsonOpts));
            }
            itemsBySlug[slug] = new JsonObject { ["items"] = itemsArr, ["dependencies"] = depsArr };
        }

        var effectiveDefault = defaultSlug;
        if (!itemsBySlug.ContainsKey(effectiveDefault) && rows.Count > 0)
            effectiveDefault = rows[0].Slug;

        return new JsonObject
        {
            ["schemaVersion"] = 2,
            ["generatedAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["defaultProjectSlug"] = effectiveDefault,
            ["projects"] = projectsArr,
            ["itemsByProjectSlug"] = itemsBySlug
        };
    }
}
