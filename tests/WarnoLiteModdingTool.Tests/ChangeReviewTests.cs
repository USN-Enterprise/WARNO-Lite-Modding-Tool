using System.IO;
using System.Text;
using System.Text.Json;
using WarnoLiteModdingTool.Core.Changes;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private const string ChangeCsv = "Localisation/text.csv";
    private static void AddTextChange(ChangeFixture f, string target = "新版名称")
    {
        ChangeFixture.Write(f.Basis, ChangeCsv, "TOKEN;EN;FR\r\nA;Old;Ancien\r\nB;Keep;Garder\r\n");
        ChangeFixture.Write(f.Source, ChangeCsv, "TOKEN;EN;FR\r\nA;Recorded;Ancien\r\nB;Keep;Garder\r\n");
        ChangeFixture.Write(f.Target, ChangeCsv, "TOKEN;EN;FR\nB;Keep;Nouveau B\nA;" + target + ";Nouveau A\nC;Added;Ajout\n");
    }
    private static ChangeTextConflict TextConflict(ChangePreview p) => p.AllFiles.Single(f => f.Path == ChangeCsv).Details.Single(d => d.Conflict is not null).Conflict!;
    private static Task ChangeCsvColumns()
    {
        using var f = new ChangeFixture(); AddTextChange(f, "Old");
        var package = f.Capture(); var preview = ChangeRestore.Prepare(package, f.Target);
        Assert(preview.CanApply, string.Join("\n", preview.Errors));
        Assert(Encoding.UTF8.GetString(preview.Files.Single(x => x.Path == ChangeCsv).After!) == "TOKEN;EN;FR\nB;Keep;Nouveau B\nA;Recorded;Nouveau A\nC;Added;Ajout\n", "cell merge preserves upstream language, row order, rows and newline");
        AddTextChange(f, "Recorded"); Assert(ChangeRestore.Prepare(f.Capture(), f.Target).CanApply, "matching recorded text needs no repeat arithmetic");
        File.WriteAllText(Path.Combine(f.Source, ChangeCsv), File.ReadAllText(Path.Combine(f.Source, ChangeCsv)), new UTF8Encoding(true));
        Assert(!ChangeRestore.Prepare(f.Capture(), f.Target).CanApply, "recorded BOM change requires review rather than disappearing");
        // Quoted multiline text, escaped quotes and UTF-16 target are preserved by spans.
        ChangeFixture.Write(f.Source, ChangeCsv, "TOKEN;EN;FR\r\nA;\"Line;1\r\n\"\"Quote\"\"\";Ancien\r\nB;Keep;Garder\r\n");
        var target = "TOKEN;EN;FR\nA;Old;Nouveau\nB;Keep;Garder\n";
        File.WriteAllText(Path.Combine(f.Target, ChangeCsv), target, Encoding.Unicode);
        preview = ChangeRestore.Prepare(f.Capture(), f.Target); Assert(preview.CanApply, string.Join("\n", preview.Errors));
        var bytes = preview.Files.Single(x => x.Path == ChangeCsv).After!;
        Assert(bytes.AsSpan().StartsWith(Encoding.Unicode.GetPreamble()) && Encoding.Unicode.GetString(bytes).Contains("\"Line;1\r\n\"\"Quote\"\"\";Nouveau\n"), "quoted multiline cells and target encoding");
        ChangeFixture.Write(f.Source, ChangeCsv, "TOKEN;EN;FR\r\nA;Recorded;Ancien\nB;Keep;Garder\r\n");
        Assert(!ChangeRestore.Prepare(f.Capture(), f.Target).CanApply, "recorded row formatting changes not silently lost");
        return Task.CompletedTask;
    }
    private static Task ChangeTextChoices()
    {
        using var f = new ChangeFixture(); AddTextChange(f); var package = f.Capture();
        var preview = ChangeRestore.Prepare(package, f.Target); var conflict = TextConflict(preview);
        Assert(!preview.CanApply && conflict.Before == "Old" && conflict.After == "Recorded" && conflict.Target == "新版名称", "text conflict includes all evidence");
        foreach (var choice in new[] { TextConflictChoice.KeepTarget, TextConflictChoice.UseRecorded })
        {
            var options = new ChangeRestoreOptions(Decisions: [new(conflict, choice)]);
            preview = ChangeRestore.Prepare(package, f.Target, options: options); Assert(preview.CanApply, string.Join("\n", preview.Errors));
            var text = Encoding.UTF8.GetString(preview.Files.Single(x => x.Path == ChangeCsv).After!);
            Assert(text.Contains(choice == TextConflictChoice.KeepTarget ? "A;新版名称;Nouveau A" : "A;Recorded;Nouveau A"), "explicit text decision only affects its cell");
        }
        AddTextChange(f, "另一个新版");
        Assert(!ChangeRestore.Prepare(package, f.Target, options: new(Decisions: [new(conflict, TextConflictChoice.UseRecorded)])).CanApply, "stale text evidence rejected");
        ChangeReject(() => ChangeRestore.Prepare(package, f.Target, options: new(Decisions: [new(conflict, (TextConflictChoice)9)])), "invalid enum rejected");
        var d = new ChangeTextDecision(conflict, TextConflictChoice.KeepTarget);
        ChangeReject(() => ChangeRestore.Prepare(package, f.Target, options: new(Decisions: [d, d])), "duplicate decisions rejected");
        ChangeReject(() => ChangeRestore.Prepare(package, f.Target, options: new(Decisions: [d with { Conflict = conflict with { Path = ChangeNdf } }])), "numeric and identity NDF choices are not enabled");
        AddTextChange(f); ChangeFixture.Write(f.Source, ChangeCsv, "TOKEN;EN;FR\r\nA;Recorded;Auteur\r\nB;Keep;Garder\r\n");
        var multiPackage = f.Capture(); var multi = ChangeRestore.Prepare(multiPackage, f.Target);
        Assert(multi.AllFiles.Single(x => x.Path == ChangeCsv).Details.Count(x => x.Conflict is not null) == 2, "all concurrent cells are visible in one preview");
        multi = ChangeRestore.Prepare(multiPackage, f.Target, options: new(Decisions: [d]));
        Assert(!multi.CanApply && multi.AllFiles.Single(x => x.Path == ChangeCsv).Details.Count(x => x.Conflict is not null) == 2, "one resolved cell does not hide or bypass another conflict");
        ChangeReviewSessions.Save(multi); Assert(!ChangeReviewSessions.Load(multiPackage, f.Target).CanApply, "unfinished conflict review can be saved without becoming writable");
        AddTextChange(f); ChangeFixture.Write(f.Target, ChangeCsv, "TOKEN;EN;FR\nB;Keep;Garder\n");
        Assert(!ChangeRestore.Prepare(package, f.Target).CanApply && ChangeRestore.Prepare(package, f.Target).AllFiles.Single(x => x.Path == ChangeCsv).Details.All(d => d.Conflict is null), "missing token cannot be forced by text choice");
        return Task.CompletedTask;
    }
    private static Task ChangeTextReceipts()
    {
        using var f = new ChangeFixture(); AddTextChange(f); var package = f.Capture();
        var original = File.ReadAllBytes(Path.Combine(f.Target, ChangeCsv)); var conflict = TextConflict(ChangeRestore.Prepare(package, f.Target));
        var preview = ChangeRestore.Prepare(package, f.Target, options: new(Decisions: [new(conflict, TextConflictChoice.KeepTarget)]));
        ChangeReject(() => ChangeTransactions.Commit(preview, afterWrite: _ => throw new IOException("injected")), "failure rolls back text decision batch");
        Assert(File.ReadAllBytes(Path.Combine(f.Target, ChangeCsv)).SequenceEqual(original) && !ChangeRestore.Prepare(package, f.Target).AllProcessed, "failed choice not applied");
        preview = ChangeRestore.Prepare(package, f.Target, options: new(Decisions: [new(conflict, TextConflictChoice.KeepTarget)]));
        var journal = ChangeTransactions.Commit(preview); var reopened = ChangeRestore.Prepare(package, f.Target);
        Assert(journal.Version == 3 && journal.Decisions.Count == 1 && reopened.AllProcessed && !reopened.AlreadyApplied && !reopened.CanApply && reopened.RetainedChanges == 1, "retained text is processed but never reported fully restored");
        Assert(reopened.AllFiles.Single(x => x.Path == ChangeCsv).Details.Single().Status.Contains("omitted"), "omitted change remains visible after reopen");
        Assert(!ChangeRestore.Prepare(package, f.Target, options: new(Decisions: [new(conflict, TextConflictChoice.UseRecorded)])).AllProcessed, "applied choice cannot be changed without recovery");
        ChangeTransactions.Recover(f.Target, journal.Id);
        Assert(File.ReadAllBytes(Path.Combine(f.Target, ChangeCsv)).SequenceEqual(original) && !ChangeRestore.Prepare(package, f.Target).CanApply, "recovery restores original target and pending conflict");
        var use = ChangeTransactions.Commit(ChangeRestore.Prepare(package, f.Target, options: new(Decisions: [new(conflict, TextConflictChoice.UseRecorded)])));
        Assert(ChangeRestore.Prepare(package, f.Target).AlreadyApplied, "use-recorded choice counts as restored");
        ChangeTransactions.Recover(f.Target, use.Id); return Task.CompletedTask;
    }
    private static Task ChangeReviewPersistence()
    {
        using var f = new ChangeFixture(); AddIndependentChange(f, 0, 20); var package = f.Capture(NumericPolicy.Ratio);
        const string moved = "CommonData/moved.ndf"; File.Delete(Path.Combine(f.Target, ChangeNdf));
        ChangeFixture.Write(f.Target, moved, ChangeText(150, 9).Replace("export Unit ", "export Renamed "));
        var options = new ChangeRestoreOptions([ChangeNdf], [new(ChangeNdf, moved, new Dictionary<string, string> { ["Unit"] = "Renamed" })]);
        var preview = ChangeRestore.Prepare(package, f.Target, options: options); Assert(preview.CanApply, string.Join("\n", preview.Errors));
        var saved = ChangeReviewSessions.Save(preview); var second = ChangeReviewSessions.Save(preview);
        Assert(saved.Id != second.Id && Directory.GetFiles(Path.Combine(f.Target, ".warno-editor/change-review"), "*.json").Length == 2, "saved progress never overwrites prior snapshots");
        var reopened = ChangeReviewSessions.Load(package, f.Target);
        Assert(reopened.CanApply && reopened.DeferredPaths.SequenceEqual(preview.DeferredPaths) && reopened.Mappings.Single().Objects["Unit"] == "Renamed", "deferred groups and moved mapping restored and revalidated");
        Assert(f.Capture().Manifest.Files.Count == 2 && File.ReadAllText(Path.Combine(f.Target, moved)).Contains("SupplyCapacity = 150"), "review persistence makes no formal changes");
        var journal = ChangeTransactions.Commit(reopened);
        ChangeReject(() => ChangeReviewSessions.Load(package, f.Target), "receipt state invalidates saved pre-apply progress");
        ChangeTransactions.Recover(f.Target, journal.Id);
        ChangeReject(() => ChangeReviewSessions.Load(package, f.Target), "recovery invalidates prior review even if files match again");
        return Task.CompletedTask;
    }
    private static Task ChangeReviewValidation()
    {
        using var f = new ChangeFixture(); AddTextChange(f); var package = f.Capture(); var conflict = TextConflict(ChangeRestore.Prepare(package, f.Target));
        var options = new ChangeRestoreOptions(Decisions: [new(conflict, TextConflictChoice.UseRecorded)]);
        var preview = ChangeRestore.Prepare(package, f.Target, options: options); var saved = ChangeReviewSessions.Save(preview);
        var loaded = ChangeReviewSessions.Load(package, f.Target); Assert(loaded.CanApply && loaded.Decisions.Single() == options.Decisions![0], "pending text choices restored");
        ChangeReject(() => ChangeReviewSessions.Load(package.WithPolicy(NumericPolicy.Ratio), f.Target), "policy belongs to session identity");
        ChangeFixture.Write(f.Target, "CommonData/unrelated.ndf", "X is 1\n");
        ChangeReject(() => ChangeReviewSessions.Load(package, f.Target), "target inventory changes invalidate review");
        ChangeReject(() => ChangeReviewSessions.Save(preview), "stale preview cannot be saved");
        File.Delete(Path.Combine(f.Target, "CommonData/unrelated.ndf"));
        var path = Path.Combine(f.Target, ".warno-editor/change-review", saved.Id + ".json");
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(saved with { Version = 999 }, ChangeJson.Options));
        ChangeReject(() => ChangeReviewSessions.Load(package, f.Target), "unknown review version rejected and preserved");
        Assert(File.Exists(path), "unknown progress retained");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { ChangeReviewSessions.Save(preview, cancel.Token); throw new InvalidOperationException("cancel ignored"); } catch (OperationCanceledException) { }
        Assert(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Length == 0, "cancel creates no incomplete progress");
        return Task.CompletedTask;
    }
}
