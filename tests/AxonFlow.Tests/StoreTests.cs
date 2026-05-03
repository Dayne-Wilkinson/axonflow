using Microsoft.Data.Sqlite;

namespace AxonFlow.Tests;

public class StoreTests
{
    [Fact]
    public void Init_insert_item_and_next_ref()
    {
        var db = Path.Combine(Path.GetTempPath(), $"axonflow-test-{Guid.NewGuid():N}.db");
        try
        {
            var cs = $"Data Source={db};Mode=ReadWriteCreate";
            using (var conn = new SqliteConnection(cs))
            {
                conn.Open();
                Migration.ApplyInitialSchema(conn);
            }
            var store = new Store(cs);
            using var c = store.Open();
            if (store.CountProjects(c) == 0)
                store.InsertDefaultProject(c);
            var pid = store.GetProjectId(c, "default")!;
            using var tx = c.BeginTransaction();
            var row = store.InsertItem(c, tx, new Store.WorkItemInsert(pid, null, null, null, "task", "plan", null, null,
                "Hello", null, "backlog", 10, null, null, null, null, null, 0));
            tx.Commit();
            Assert.Equal(1, row.RefNumber);
            Assert.True(store.PredecessorsSatisfied(c, row.Id));

            Assert.Empty(store.ListDependenciesForProject(c, pid));
            using (var tx2 = c.BeginTransaction())
            {
                var row2 = store.InsertItem(c, tx2, new Store.WorkItemInsert(pid, null, null, null, "task", "plan", null, null,
                    "Second", null, "backlog", 20, null, null, null, null, null, 0));
                store.AddDependency(c, tx2, pid, row.Id, row2.Id, "finish_start");
                tx2.Commit();
            }
            var deps = store.ListDependenciesForProject(c, pid);
            Assert.Single(deps);
            Assert.Equal(row.Id, deps[0].PredecessorId);
        }
        finally
        {
            try { File.Delete(db); } catch { /* ignore */ }
        }
    }
}
