using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WarnoLiteModdingTool.Core.Changes;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private const string ChangeNdf = "GameData/Generated/Test.ndf";
    private static string ChangeText(int amount, int other = 7) => $"// unchanged comment\r\nexport Unit is TEntityDescriptor\r\n(\r\n ModulesDescriptors = [TSupplyModuleDescriptor(SupplyCapacity = {amount}), TUnknown(Other = {other})]\r\n)\r\n";
    private sealed class ChangeFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "wlmt-change-tests-" + Guid.NewGuid().ToString("N"));
        public string Basis => Path.Combine(Root, "base"); public string Source => Path.Combine(Root, "source"); public string Target => Path.Combine(Root, "target");
        public ChangeFixture() { foreach (var dir in new[] { Basis, Source, Target }) Directory.CreateDirectory(dir); Write(Basis, ChangeNdf, ChangeText(100)); Write(Source, ChangeNdf, ChangeText(120)); Write(Target, ChangeNdf, ChangeText(150, 9)); }
        public ChangePackage Capture(NumericPolicy policy = NumericPolicy.Delta) => ChangeCapture.Capture(Source, Basis, policy);
        public void Dispose() => DeleteTemporaryFixture(Root);
        public static void Write(string root, string relative, string text) { var path = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text, new UTF8Encoding(false)); }
    }
    private static void ChangeReject(Action action, string label)
    {
        try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or JsonException) { return; }
        throw new InvalidOperationException("Expected rejection: " + label);
    }
    private static Task ChangeCaptureRoundtrip()
    {
        using var f = new ChangeFixture();
        ChangeFixture.Write(f.Basis, "CommonData/deleted.ndf", "Old is 1\n");
        ChangeFixture.Write(f.Source, "GameData/custom.txt", "outside editor\r\n保持原文");
        ChangeFixture.Write(f.Source, "GameData/empty.txt", "");
        Directory.CreateDirectory(Path.Combine(f.Source, "GameData/Assets")); File.WriteAllBytes(Path.Combine(f.Source, "GameData/Assets/image.bin"), [0, 255, 17, 9]);
        ChangeFixture.Write(f.Source, ".warno-editor/draft-v1.json", "unapplied");
        var package = f.Capture(); Assert(package.Manifest.Complete && package.Manifest.Files.Count == 5, "all changed files captured, pending draft excluded");
        var saved = Path.Combine(f.Root, "test.wlmtchanges"); ChangePackageStore.Save(package, saved);
        Directory.Move(f.Source, Path.Combine(f.Root, "unavailable")); var read = ChangePackageStore.Load(saved);
        Assert(read.Identity == package.Identity && read.Payloads.Count == package.Payloads.Count, "independent package roundtrip");
        Assert(read.Manifest.Files.Any(x => x.After.Presence == FilePresence.Missing) && read.Manifest.Files.Any(x => x.After.Length == 0 && x.After.Presence == FilePresence.Present), "deleted and empty distinct");
        var preview = ChangeRestore.Prepare(read, f.Target); Assert(preview.CanApply, string.Join("\n", preview.Errors));
        var journal = ChangeTransactions.Commit(preview);
        Assert(File.ReadAllBytes(Path.Combine(f.Target, "GameData/Assets/image.bin")).SequenceEqual(new byte[] { 0, 255, 17, 9 }) &&
            File.Exists(Path.Combine(f.Target, "GameData/empty.txt")), "independent package restores binary and empty file");
        Assert(ChangeRestore.Prepare(ChangePackageStore.Load(saved), f.Target).AlreadyApplied, "independent reload detects completed application");
        ChangeTransactions.Recover(f.Target, journal.Id);
        Assert(!File.Exists(Path.Combine(f.Target, "GameData/Assets/image.bin")) && File.ReadAllText(Path.Combine(f.Target, ChangeNdf)) == ChangeText(150, 9), "independent package recovery restores the new baseline");
        ChangeReject(() => ChangePackageStore.Save(package, saved), "never silently overwrite package"); return Task.CompletedTask;
    }
    private static Task ChangeNumericMerge()
    {
        using var f = new ChangeFixture();
        foreach (var policy in new[] { NumericPolicy.Delta, NumericPolicy.Ratio })
        {
            var p = ChangeRestore.Prepare(f.Capture(policy), f.Target);
            Assert(p.CanApply, string.Join("\n", p.Errors));
            var text = Encoding.UTF8.GetString(p.Files.Single().After!);
            Assert(text.Contains(policy == NumericPolicy.Delta ? "SupplyCapacity = 170" : "SupplyCapacity = 180") && text.Contains("Other = 9") && text.Contains("\r\n"), "policy and upstream value preserved");
        }
        ChangeFixture.Write(f.Basis, ChangeNdf, ChangeText(0)); var zero = ChangeRestore.Prepare(f.Capture(NumericPolicy.Ratio), f.Target);
        Assert(!zero.CanApply && zero.Errors.Any(e => e.Contains("zero baseline")), "zero ratio does not silently become delta");
        ChangeFixture.Write(f.Basis, ChangeNdf, ChangeText(100)); ChangeFixture.Write(f.Source, ChangeNdf, ChangeText(-100));
        Assert(!ChangeRestore.Prepare(f.Capture(), f.Target).CanApply, "negative resulting supply rejected"); return Task.CompletedTask;
    }
    private static Task ChangeUnknownPreservation()
    {
        using var f = new ChangeFixture();
        ChangeFixture.Write(f.Source, ChangeNdf, ChangeText(120).Replace("// unchanged comment", "// changed comment"));
        var package = f.Capture(); var p = ChangeRestore.Prepare(package, f.Target);
        Assert(!p.CanApply && package.Manifest.Complete, "unknown text fully captured but upstream merging blocked");
        ChangeFixture.Write(f.Target, ChangeNdf, ChangeText(100)); p = ChangeRestore.Prepare(package, f.Target);
        Assert(p.CanApply && p.Files[0].After!.SequenceEqual(File.ReadAllBytes(Path.Combine(f.Source, ChangeNdf))), "same-base exact bytes including comment");
        ChangeFixture.Write(f.Source, ChangeNdf, ChangeText(120).Replace("TUnknown(Other = 7)", "TUnknown(Other = 8)"));
        ChangeFixture.Write(f.Target, ChangeNdf, ChangeText(150, 10)); Assert(!ChangeRestore.Prepare(f.Capture(), f.Target).CanApply, "unknown numeric meaning not scaled");
        return Task.CompletedTask;
    }
    private static Task ChangeCommitRecovery()
    {
        using var f = new ChangeFixture(); var package = f.Capture(NumericPolicy.Ratio); var preview = ChangeRestore.Prepare(package, f.Target);
        var journal = ChangeTransactions.Commit(preview); Assert(journal.State == "Completed", "committed journal");
        var again = ChangeRestore.Prepare(package, f.Target); Assert(again.AlreadyApplied && !again.CanApply, "do not multiply twice");
        Assert(!ChangeRestore.Prepare(package.WithPolicy(NumericPolicy.Delta), f.Target).CanApply, "changing policy cannot reapply onto a modified baseline");
        ChangeTransactions.Recover(f.Target, journal.Id); Assert(File.ReadAllText(Path.Combine(f.Target, ChangeNdf)) == ChangeText(150, 9), "restore target, not old official baseline");
        Assert(ChangeRestore.Prepare(package, f.Target).CanApply, "receipt rolled back consistently");
        ChangeFixture.Write(f.Source, "GameData/extra.txt", "new resource"); preview = ChangeRestore.Prepare(f.Capture(), f.Target);
        ChangeReject(() => ChangeTransactions.Commit(preview, afterWrite: i => { if (i == 0) throw new IOException("injected write failure"); }), "rollback failure boundary");
        Assert(File.ReadAllText(Path.Combine(f.Target, ChangeNdf)) == ChangeText(150, 9) && !File.Exists(Path.Combine(f.Target, "GameData/extra.txt")), "all files restored after failure");
        Assert(ChangeTransactions.List(f.Target).All(j => j.State == "RolledBack"), "journals settled"); return Task.CompletedTask;
    }
    private static Task ChangePreconditions()
    {
        using var f = new ChangeFixture(); var package = f.Capture(); var p = ChangeRestore.Prepare(package, f.Target);
        ChangeFixture.Write(f.Target, "GameData/new.ndf", "Added is 3");
        ChangeReject(() => ChangeTransactions.Commit(p), "new external dependency after preview");
        Assert(File.ReadAllText(Path.Combine(f.Target, ChangeNdf)) == ChangeText(150, 9), "stale preview did not write");
        ChangeFixture.Write(f.Target, ".warno-editor/draft-v1.json", "{\"operations\":[{\"pending\":true}]}");
        Assert(!ChangeRestore.Prepare(package, f.Target).CanApply, "pending draft not overwritten");
        ChangeFixture.Write(f.Target, ".warno-editor/draft-v1.json", "{\"operations\":[]}");
        var j = ChangeTransactions.Commit(ChangeRestore.Prepare(package, f.Target)); ChangeFixture.Write(f.Target, ChangeNdf, ChangeText(999));
        ChangeReject(() => ChangeTransactions.Recover(f.Target, j.Id), "preserve edits after apply");
        Assert(File.ReadAllText(Path.Combine(f.Target, ChangeNdf)).Contains("999"), "external modification remains"); return Task.CompletedTask;
    }
    private static Task ChangeArchiveValidation()
    {
        using var f = new ChangeFixture(); var package = f.Capture(); var path = Path.Combine(f.Root, "bad.wlmtchanges"); ChangePackageStore.Save(package, path);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update)) { var e = zip.GetEntry(package.Manifest.Files[0].AfterPayload!)!; var name = e.FullName; e.Delete(); using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write("corrupted"); }
        ChangeReject(() => ChangePackageStore.Load(path), "corrupt payload");
        foreach (var name in new[] { "../outside", "C:/outside", "/outside", "con.txt", "x/../y", "x.", "a//b" }) ChangeReject(() => ChangePaths.Resolve(f.Target, name), "unsafe path");
        var m = package.Manifest; ChangeReject(() => ChangePackageStore.Validate(new(m with { Version = 999 }, package.Payloads)), "unknown format");
        ChangeReject(() => ChangePackageStore.Validate(new(m with { Files = [] }, package.Payloads)), "missing inventory changes");
        var baseZip = Path.Combine(f.Root, "base.zip"); ChangeFixture.Write(f.Basis, "CommonData/x.ndf", "X is 1"); ZipFile.CreateFromDirectory(f.Basis, baseZip);
        ChangeFixture.Write(f.Source, "GameData/resource.png", "image placeholder");
        var zipPackage = ChangeCapture.Capture(f.Source, baseZip, NumericPolicy.Delta);
        Assert(!zipPackage.Manifest.Complete && zipPackage.Manifest.Files.Single(x => x.Path.EndsWith("png")).Before.Presence == FilePresence.Unknown, "ZIP does not establish missing resource baseline");
        Assert(!ChangeRestore.Prepare(zipPackage, f.Target).CanApply, "incomplete package is not applied"); return Task.CompletedTask;
    }
    private static Task ChangeCsvAndReferences()
    {
        using var f = new ChangeFixture(); var csv = "GameData/Localisation/UNITS.csv";
        ChangeFixture.Write(f.Basis, csv, "TOKEN;REFTEXT\r\nA;old\r\nB;unchanged\r\n");
        ChangeFixture.Write(f.Source, csv, "TOKEN;REFTEXT\r\nA;new\r\nB;unchanged\r\nC;added\r\n");
        ChangeFixture.Write(f.Target, csv, "TOKEN;REFTEXT\r\nA;old\r\nB;upstream\r\n");
        var p = ChangeRestore.Prepare(f.Capture(), f.Target); Assert(p.CanApply, string.Join("\n", p.Errors));
        Assert(Encoding.UTF8.GetString(p.Files.Single(x => x.Path == csv).After!).Contains("B;upstream\r\nC;added"), "CSV token merge keeps upstream row");
        ChangeFixture.Write(f.Target, csv, "TOKEN;REFTEXT\r\nA;old\r\nB;upstream");
        p = ChangeRestore.Prepare(f.Capture(), f.Target); Assert(p.CanApply && Encoding.UTF8.GetString(p.Files.Single(x => x.Path == csv).After!).Contains("B;upstream\r\nC;added"), "CSV append supplies missing line terminator");
        ChangeFixture.Write(f.Source, "GameData/added.ndf", "Added is TTest(Ref=~/Missing)\n");
        Assert(!ChangeRestore.Prepare(f.Capture(), f.Target).CanApply, "unresolved added reference blocks combined commit");
        return Task.CompletedTask;
    }
    private static Task ChangeCaptureCancellation()
    {
        using var f = new ChangeFixture(); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { ChangeCapture.Capture(f.Source, f.Basis, NumericPolicy.Delta, cancel: cancel.Token); throw new InvalidOperationException("cancel ignored"); } catch (OperationCanceledException) { }
        var package = f.Capture(); var path = Path.Combine(f.Root, "cancel.wlmtchanges");
        try { ChangePackageStore.Save(package, path, cancel.Token); throw new InvalidOperationException("cancel ignored"); } catch (OperationCanceledException) { }
        Assert(!File.Exists(path) && !Directory.GetFiles(f.Root, "*.tmp").Any(), "cancel leaves no apparent package");
        return Task.CompletedTask;
    }
    private static Task ChangeRulesAndIdentities()
    {
        using var f = new ChangeFixture();
        var rule = "GameData/Gameplay/Constantes/Experience.ndf";
        ChangeFixture.Write(f.Basis, rule, "ExperienceMultiplierBonusOnKill is 2\nOther is 9\n");
        ChangeFixture.Write(f.Source, rule, "ExperienceMultiplierBonusOnKill is 3\nOther is 9\n");
        ChangeFixture.Write(f.Target, rule, "ExperienceMultiplierBonusOnKill is 4\nOther is 10\n");
        var preview = ChangeRestore.Prepare(f.Capture(NumericPolicy.Ratio), f.Target);
        Assert(preview.CanApply, string.Join("\n", preview.Errors));
        Assert(Encoding.UTF8.GetString(preview.Files.Single(x => x.Path == rule).After!) == "ExperienceMultiplierBonusOnKill is 6\nOther is 10\n", "known standalone constant arithmetic");
        ChangeFixture.Write(f.Basis, "GameData/guids.ndf", "A is TTest(DescriptorId='GUID:{same}')\n");
        ChangeFixture.Write(f.Target, "GameData/guids.ndf", "A is TTest(DescriptorId='GUID:{same}')\n");
        ChangeFixture.Write(f.Source, "GameData/guids.ndf", "A is TTest(DescriptorId='GUID:{same}')\nB is TTest(DescriptorId='GUID:{same}')\n");
        Assert(!ChangeRestore.Prepare(f.Capture(), f.Target).CanApply, "new GUID collision rejected");
        ChangeReject(() => ChangeMerge.ValidateBytes("GameData/bad.ndf", Encoding.UTF8.GetBytes("X is 'unclosed")), "lexical string boundary");
        ChangeReject(() => ChangeMerge.ValidateBytes("GameData/bad.ndf", Encoding.UTF8.GetBytes("/* unclosed")), "lexical comment boundary");
        return Task.CompletedTask;
    }
    private static Task ChangeInterruptedRecovery()
    {
        using var f = new ChangeFixture(); ChangeFixture.Write(f.Source, "GameData/extra.txt", "added");
        var package = f.Capture(); var journal = ChangeTransactions.Commit(ChangeRestore.Prepare(package, f.Target));
        var path = Path.Combine(f.Target, ".warno-editor/change-transactions", journal.Id, "record.json");
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(journal with { State = "Committing" }, ChangeJson.Options));
        // Simulate interruption after one file replacement but before progress was persisted.
        ChangeFixture.Write(f.Target, ChangeNdf, ChangeText(150, 9));
        Assert(!ChangeRestore.Prepare(package, f.Target).CanApply, "unfinished transaction blocks new commit");
        ChangeTransactions.Recover(f.Target, journal.Id);
        Assert(!File.Exists(Path.Combine(f.Target, "GameData/extra.txt")) && File.ReadAllText(Path.Combine(f.Target, ChangeNdf)) == ChangeText(150, 9), "recovery inspects actual before/after content");
        return Task.CompletedTask;
    }
    private static Task ChangeCaptureCoherence()
    {
        using var f = new ChangeFixture();
        var mutated = false;
        var progress = new InlineProgress<ChangeProgress>(_ => { if (!mutated) { mutated = true; ChangeFixture.Write(f.Source, "GameData/arrived.txt", "concurrent"); } });
        ChangeReject(() => ChangeCapture.Capture(f.Source, f.Basis, NumericPolicy.Delta, progress), "new file during capture");
        var path = Path.Combine(f.Root, "duplicate.wlmtchanges"); ChangePackageStore.Save(f.Capture(), path);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update)) { using var writer = new StreamWriter(zip.CreateEntry("MANIFEST.JSON").Open()); writer.Write("{}"); }
        ChangeReject(() => ChangePackageStore.Load(path), "case-insensitive duplicate ZIP entry");
        return Task.CompletedTask;
    }
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
    private static Task ChangePerformance()
    {
        using var f = new ChangeFixture(); var padding = "// " + new string('x', 65536) + "\n";
        for (var i = 0; i < 128; i++)
        {
            var path = "GameData/bulk/" + i + ".ndf";
            ChangeFixture.Write(f.Basis, path, padding + ChangeText(100).Replace("export Unit", "export Unit" + i));
            ChangeFixture.Write(f.Source, path, padding + ChangeText(120).Replace("export Unit", "export Unit" + i));
            ChangeFixture.Write(f.Target, path, padding + ChangeText(150, 9).Replace("export Unit", "export Unit" + i));
        }
        var timer = System.Diagnostics.Stopwatch.StartNew(); var package = f.Capture(); var captureMs = timer.ElapsedMilliseconds;
        var pathOut = Path.Combine(f.Root, "bulk.wlmtchanges"); timer.Restart(); ChangePackageStore.Save(package, pathOut); var exportMs = timer.ElapsedMilliseconds;
        timer.Restart(); var preview = ChangeRestore.Prepare(ChangePackageStore.Load(pathOut), f.Target); var previewMs = timer.ElapsedMilliseconds;
        Assert(preview.CanApply, string.Join("\n", preview.Errors));
        var evidence = new { Files = package.Manifest.Inventory.Count, PayloadBytes = package.Payloads.Values.Sum(b => (long)b.Length), CaptureMs = captureMs, ExportAndVerifyMs = exportMs, ReadAndPreviewMs = previewMs, PeakWorkingSet = System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64 };
        Directory.CreateDirectory("publish/qa-2.10-preview.6"); File.WriteAllText("publish/qa-2.10-preview.6/performance.json", JsonSerializer.Serialize(evidence, ChangeJson.Options)); Console.WriteLine(JsonSerializer.Serialize(evidence)); return Task.CompletedTask;
    }
    private static async Task ChangeFocusedTests()
    {
        foreach (var test in new Func<Task>[] { ChangeCaptureRoundtrip, ChangeNumericMerge, ChangeUnknownPreservation, ChangeCommitRecovery, ChangePreconditions, ChangeArchiveValidation, ChangeCsvAndReferences, ChangeCaptureCancellation, ChangeRulesAndIdentities, ChangeInterruptedRecovery, ChangeCaptureCoherence,
            ChangePartialContinuation, ChangeGroupBoundaries, ChangeExplicitMapping, ChangeLegacyAndReceiptRace, ChangeCommonDataAndNoChanges,
            ChangeCsvColumns, ChangeTextChoices, ChangeTextReceipts, ChangeReviewPersistence, ChangeReviewValidation,
            ChangeMapMixedMerge, ChangeMapBoundaries, ChangeMapSeparatorsAndSatisfied, ChangeMapUnnamedAndIdentity, ChangeMapTransactionAndLimits,
            ChangeScalarChoices, ChangeScalarBoundaries, ChangeScalarExcludedIdentity, ChangeScalarTransactions, ChangeScalarPersistence,
            ChangeFieldPresenceMerge, ChangeFieldPresenceConflicts, ChangeFieldPresenceRoundtrip })
        { await test(); Console.WriteLine("PASS " + test.Method.Name); }
    }
}
