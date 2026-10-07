using System.IO;
using System.Text;
using System.Text.Json;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.SourceWorkspace;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static string SourceFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "wlmt-source-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        foreach (var path in new[] { "Root.ndf", "Custom/Nested/Unknown.NDF", ".hidden/Values.ndf", "GameData/Odd/Constants.ndf" })
        { var full = Path.Combine(root, path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); File.WriteAllText(full, "// 中文 untouched\r\nOdd is TUnknown\n(\r\n    Value = 7 // keep\r\n)\n", new UTF8Encoding(false)); }
        return root;
    }
    private static async Task SourceFocused()
    {
        foreach (var test in new Func<Task>[] { SourceDiscoveryAndDrafts, SourceCommitAndRestore, SourceConflicts, SourceFailures, SourceLanguageAndEncoding, SourceBusinessCoordination }) { await test(); Console.WriteLine("PASS " + test.Method.Name); }
    }
    private static async Task SourceDiscoveryAndDrafts()
    {
        var root = SourceFixture();
        try
        {
            var files = SourceFiles.Discover(root); Assert(files.Files.Count == 4 && files.Errors.Count == 0, "all root, nested, hidden, arbitrary files");
            using var store = new DraftStore(root); await store.LoadAsync();
            var data = SourceFiles.Open(root, "Custom/Nested/Unknown.NDF"); var invalid = data with { Text = "Odd is TUnknown(" };
            await store.UpsertAsync(SourceEditing.Operation("Custom/Nested/Unknown.NDF", invalid));
            Assert(File.ReadAllText(Path.Combine(root, "Custom/Nested/Unknown.NDF")) == data.Text, "draft never writes formal");
            using var reload = new DraftStore(root); var loaded = await reload.LoadAsync();
            Assert(loaded.Document.SchemaVersion == 6 && reload.Operations.Single().Source!.Text == invalid.Text, "schema6 persists invalid input");
            await TestAssert.ThrowsAsync<InvalidDataException>(() => SourceEditing.Prepare(root, reload.Operations, default), "new unclosed syntax blocked at apply");
            Assert(SourceEditing.Resolve(root, reload.Operations.Single()).Status == DraftResolutionStatus.Active, "unvalidated draft remains recoverable");
            var outside = false; try { SourceFiles.Resolve(root, "../outside.ndf"); } catch (InvalidDataException) { outside = true; } Assert(outside, "path boundary");
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task SourceCommitAndRestore()
    {
        var root = SourceFixture();
        try
        {
            using var store = new DraftStore(root); await store.LoadAsync();
            var data = SourceFiles.Open(root, "Root.ndf");
            await store.UpsertAsync(SourceEditing.Operation("Root.ndf", data with { Text = data.Text.Replace("Value = 7", "Value = 19") }));
            await store.UpsertAsync(SourceEditing.Operation("Unseen/Sub/New.ndf", new(null, "BrandNew is TFuture(Reference = ~/Odd)\n")));
            await store.UpsertAsync(SourceEditing.Operation("Names/data.csv", new(null, "TOKEN;TEXT\r\nUNKNOWN;中文\r\n")));
            var preview = await new UnitTransactionService().PrepareApplyAsync(root, [store.Operations[0]]);
            Assert(preview.Operations.Count == 3, "source selected subset expands entire group");
            await TestAssert.ThrowsAsync<InvalidOperationException>(() => SourceTransactions.Commit(preview, store), "unknown semantic acknowledgment mandatory");
            preview.SourceAcknowledged = true;
            var result = await SourceTransactions.Commit(preview, store);
            Assert(result.Succeeded && store.Operations.Count == 0, "commit and cleanup");
            Assert(File.ReadAllText(Path.Combine(root, "Root.ndf")) == data.Text.Replace("Value = 7", "Value = 19"), "exact untouched text and mixed newlines");
            Assert(File.Exists(Path.Combine(root, "Unseen/Sub/New.ndf")), "new file committed");
            Assert(SourceFiles.Discover(root).Files.Count == 6, "backup payloads not indexed as NDF");
            SourceTransactions.Restore(root, result.BackupId);
            Assert(SourceFiles.Equal(SourceFiles.Read(root, "Root.ndf"), data.Baseline) && !File.Exists(Path.Combine(root, "Unseen/Sub/New.ndf")), "restore exact old bytes and new absence");
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task SourceConflicts()
    {
        var root = SourceFixture();
        try
        {
            using var store = new DraftStore(root); await store.LoadAsync(); var data = SourceFiles.Open(root, "Root.ndf");
            await store.UpsertAsync(SourceEditing.Operation("Root.ndf", data with { Text = data.Text + "Future is 5\n" }));
            var preview = await SourceEditing.Prepare(root, store.Operations, default); preview.SourceAcknowledged = true;
            File.WriteAllText(Path.Combine(root, "Custom/Nested/Unknown.NDF"), "External is 9\n");
            await TestAssert.ThrowsAsync<IOException>(() => SourceTransactions.Commit(preview, store), "dependency changed after preview");
            preview = await SourceEditing.Prepare(root, store.Operations, default); preview.SourceAcknowledged = true;
            File.WriteAllText(Path.Combine(root, "Added.ndf"), "External is 0\n");
            await TestAssert.ThrowsAsync<IOException>(() => SourceTransactions.Commit(preview, store), "discovery changed after preview");
            using (ProjectWriteGate.Enter(root)) await TestAssert.ThrowsAsync<IOException>(() => SourceTransactions.Commit(preview, store), "all formal writes serialize");
            Assert(SourceFiles.Equal(SourceFiles.Read(root, "Root.ndf"), data.Baseline), "failure leaves formal unchanged");
            using var stale = new DraftStore(root); await stale.LoadAsync();
            await store.UpsertAsync(SourceEditing.Operation("Root.ndf", data with { Text = "Current is 99\n" }));
            await TestAssert.ThrowsAsync<IOException>(() => stale.UpsertAsync(SourceEditing.Operation("Root.ndf", data with { Text = "Stale is 2\n" })), "stale editor cannot overwrite newer draft");
            using var fresh = new DraftStore(root); await fresh.LoadAsync();
            Assert(fresh.Operations.Single().Source!.Text == "Current is 99\n", "newer draft retained");
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task SourceFailures()
    {
        foreach (var external in new[] { false, true })
        foreach (var failAt in new[] { 0, 1, 2 })
        {
            var root = SourceFixture();
            try
            {
                using var store = new DraftStore(root); await store.LoadAsync(); var data = SourceFiles.Open(root, "Root.ndf");
                await store.UpsertAsync(SourceEditing.Operation("Root.ndf", data with { Text = "Changed is 42\n" }));
                await store.UpsertAsync(SourceEditing.Operation("New.ndf", new(null, "New is 4\n")));
                await store.UpsertAsync(SourceEditing.Operation("Third.ndf", new(null, "Third is 6\n")));
                var preview = await SourceEditing.Prepare(root, store.Operations, default); preview.SourceAcknowledged = true;
                await TestAssert.ThrowsAsync<IOException>(() => SourceTransactions.Commit(preview, store, afterWrite: index =>
                { if (index != failAt) return; if (external) File.WriteAllText(Path.Combine(root, "Root.ndf"), "External is 99\n"); throw new IOException("injected interruption"); }), "write fault");
                var journal = SourceTransactions.List(root).Single();
                Assert(journal.State == (external ? "RecoveryRequired" : "Restored"), "durable recovery state");
                Assert(store.Operations.Count == 3, "drafts retained on failure");
                if (!external) Assert(SourceFiles.Equal(data.Baseline, SourceFiles.Read(root, "Root.ndf")), "automatic rollback");
                else
                {
                    Assert(File.ReadAllText(Path.Combine(root, "Root.ndf")) == "External is 99\n", "never overwrite external content during rollback");
                    await TestAssert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() => SourceTransactions.EnsureRecovered(root)), "unfinished transaction blocks writes");
                    File.WriteAllBytes(Path.Combine(root, "Root.ndf"), data.Baseline!); SourceTransactions.Restore(root, journal.Id);
                    Assert(SourceTransactions.List(root).Single().State == "Restored", "manual conflict resolution can complete recovery");
                }
            }
            finally { DeleteTemporaryFixture(root); }
        }
        var cleanupRoot = SourceFixture();
        try
        {
            using var store = new DraftStore(cleanupRoot); await store.LoadAsync(); var data = SourceFiles.Open(cleanupRoot, "Root.ndf");
            await store.UpsertAsync(SourceEditing.Operation("Root.ndf", data with { Text = "Applied is 12\n" }));
            var preview = await SourceEditing.Prepare(cleanupRoot, store.Operations, default); preview.SourceAcknowledged = true;
            var result = await SourceTransactions.Commit(preview, store, afterWrite: _ => File.AppendAllText(store.DraftPath, " "));
            Assert(result.Succeeded && result.Warnings.Count == 1 && SourceTransactions.List(cleanupRoot).Single().State == "Completed", "cleanup failure preserves successful commit status");
            await TestAssert.ThrowsAsync<InvalidOperationException>(() => SourceEditing.Prepare(cleanupRoot, store.Operations, default), "successful writes cannot be repeated with old baselines");
            using var reopened = new DraftStore(cleanupRoot); await reopened.LoadAsync();
            await SourceTransactions.Cleanup(SourceTransactions.List(cleanupRoot).Single(), reopened);
            Assert(reopened.Operations.Count == 0, "reopen can finish cleanup without repeating formal writes");
            var journalPath = Path.Combine(cleanupRoot, ".warno-editor/source-transactions", result.BackupId, "journal.json");
            File.WriteAllText(journalPath, File.ReadAllText(journalPath).Replace("\"Completed\"", "\"Committing\""));
            SourceTransactions.Restore(cleanupRoot, result.BackupId);
            Assert(SourceFiles.Equal(data.Baseline, SourceFiles.Read(cleanupRoot, "Root.ndf")), "interruption before journal completion restores using actual candidate bytes");
        }
        finally { DeleteTemporaryFixture(cleanupRoot); }
    }
    private static async Task SourceLanguageAndEncoding()
    {
        var root = SourceFixture();
        try
        {
            var text = "// ignored (\r\nX is T(V = '[(]' /* [ */ A = [1,2])\n";
            Assert(SourceLanguage.Diagnostics(text).Count == 0, "comments and quotes do not affect brackets");
            Assert(SourceLanguage.Symbols("Root.ndf", "// Fake is 3\nValue is 7\nX is T(A = 2)").Select(s => s.Name).SequenceEqual(new[] { "Value", "X" }), "constant and object symbols exclude comments");
            Assert(SourceLanguage.Diagnostics("X is T(V = 'broken)").Count > 0, "unclosed quote");
            var hits = SourceLanguage.Find("Root.ndf", text, "is", false); Assert(hits.Single().Line == 2, "search locations");
            Assert(SourceLanguage.Replace(text, hits, "was").Contains("X was T"), "selected replacement");
            await TestAssert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() => SourceLanguage.Replace(text + " ", hits, "was")), "stale replacement rejected");
            File.WriteAllText(Path.Combine(root, "Utf16.ndf"), "中国 is T(Value = 3)\r\n", Encoding.Unicode);
            var data = SourceFiles.Open(root, "Utf16.ndf"); Assert(data.CodePage == 1200 && data.Bom, "encoding detected");
            using var store = new DraftStore(root); await store.LoadAsync(); await store.UpsertAsync(SourceEditing.Operation("Utf16.ndf", data));
            await TestAssert.ThrowsAsync<InvalidDataException>(() => SourceEditing.Prepare(root, store.Operations, default), "conversion explicit");
            await store.UpsertAsync(SourceEditing.Operation("Utf16.ndf", data with { ConvertEncoding = true }));
            var preview = await SourceEditing.Prepare(root, store.Operations, default); preview.SourceAcknowledged = true;
            await SourceTransactions.Commit(preview, store);
            Assert(SourceFiles.Read(root, "Utf16.ndf")!.AsSpan().SequenceEqual(new UTF8Encoding(false).GetBytes(data.Text)), "UTF8 no BOM conversion");
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task SourceBusinessCoordination()
    {
        var root = AviationFixture();
        try
        {
            var (_, unit) = await LoadTankAsync(root); using var store = new DraftStore(root); await store.LoadAsync();
            await store.UpsertAsync(AviationOp(unit, "flight.altitude", "850"));
            var path = unit.Source.RelativeSourceFile; var data = SourceFiles.Open(root, path);
            await TestAssert.ThrowsAsync<InvalidOperationException>(() => store.UpsertAsync(SourceEditing.Operation(path, data with { Text = data.Text + "\n" })), "same file business conflict");
            await store.UpsertAsync(SourceEditing.Operation("Custom/Independent.ndf", new(null, "Independent is 1\n")));
            Assert(store.Operations.Count == 2, "independent source file can coexist");
            var transactions = new UnitTransactionService();
            var business = await transactions.PrepareApplyAsync(root,store.Operations.Where(o=>o.TargetKind!=DraftTargetKind.SourceFile).ToArray());
            await transactions.CommitApplyAsync(business,store);
            Assert(store.Operations.Count==1 && store.Operations.Single().TargetKind==DraftTargetKind.SourceFile && !File.Exists(Path.Combine(root,"Custom/Independent.ndf")),"business-only apply preserves independent source draft without writing it");
            var (_,updatedUnit)=await LoadTankAsync(root);await store.UpsertAsync(AviationOp(updatedUnit,"flight.altitude","900"));
            var preview = await SourceEditing.Prepare(root, store.Operations.Where(o=>o.TargetKind==DraftTargetKind.SourceFile).ToArray(), default); preview.SourceAcknowledged = true;
            await SourceTransactions.Commit(preview, store);
            Assert(store.Operations.Count == 1 && store.Operations.Single().TargetKind != DraftTargetKind.SourceFile, "source cleanup preserves unrelated business draft");
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task SourceOldReader(string oldCore)
    {
        var root = SourceFixture();
        try
        {
            using var store = new DraftStore(root); await store.LoadAsync(); await store.UpsertAsync(SourceEditing.Operation("Root.ndf", SourceFiles.Open(root, "Root.ndf") with { Text = "X is 5\n" }));
            var before = File.ReadAllBytes(store.DraftPath); var context = new System.Runtime.Loader.AssemblyLoadContext("source-old-reader", true);
            try
            {
                var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(oldCore)); var type = assembly.GetType("WarnoLiteModdingTool.Core.Drafts.DraftStore")!;
                using var old = (IDisposable)Activator.CreateInstance(type, root)!; var task = (Task)type.GetMethod("LoadAsync")!.Invoke(old, [CancellationToken.None])!; await task;
                var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
                Assert((bool)result.GetType().GetProperty("IsBlocked")!.GetValue(result)!, "old binary refuses schema6");
                Assert(before.AsSpan().SequenceEqual(File.ReadAllBytes(store.DraftPath)), "old reader preserves bytes");
            }
            finally { context.Unload(); }
        }
        finally { DeleteTemporaryFixture(root); }
    }
    private static async Task SourcePerformance()
    {
        var root = SourceFixture(); var results = new List<object>();
        try
        {
            var text = "// " + new string('x', 19970) + "\nX is T(Value = 3)\n";
            var directory = Path.Combine(root, "Many"); Directory.CreateDirectory(directory);
            for (var i = 0; i < 5000; i++) File.WriteAllText(Path.Combine(directory, i + ".ndf"), text);
            var watch = System.Diagnostics.Stopwatch.StartNew(); var catalog = SourceFiles.Discover(root); results.Add(new { Operation = "discover-5004-files-100MB", Milliseconds = watch.ElapsedMilliseconds });
            watch.Restart(); foreach (var file in catalog.Files) SourceLanguage.Find(file.Path, SourceFiles.Open(root, file.Path).Text, "missing-needle", false); results.Add(new { Operation = "search-100MB", Milliseconds = watch.ElapsedMilliseconds });
            using var store = new DraftStore(root); await store.LoadAsync();
            foreach (var megabytes in new[] { 1, 10, 50 })
            {
                var path = "Large" + megabytes + ".ndf"; var content = "// " + new string('x', megabytes * 1_000_000) + "\nBig is T(Value = 1)\n"; File.WriteAllText(Path.Combine(root, path), content);
                watch.Restart(); var data = SourceFiles.Open(root, path); results.Add(new { Operation = "open-" + megabytes + "MB", Milliseconds = watch.ElapsedMilliseconds });
                watch.Restart(); await store.UpsertAsync(SourceEditing.Operation(path, data with { Text = data.Text + "// edited\n" })); results.Add(new { Operation = "save-draft-" + megabytes + "MB", Milliseconds = watch.ElapsedMilliseconds });
                await store.ClearAsync();
            }
            Directory.CreateDirectory("publish/qa-source-workspace");
            File.WriteAllText("publish/qa-source-workspace/performance.json", JsonSerializer.Serialize(new { OS = Environment.OSVersion.ToString(), Cores = Environment.ProcessorCount, MemoryPeak = System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64, Results = results }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(File.ReadAllText("publish/qa-source-workspace/performance.json"));
        }
        finally { DeleteTemporaryFixture(root); }
    }
}
