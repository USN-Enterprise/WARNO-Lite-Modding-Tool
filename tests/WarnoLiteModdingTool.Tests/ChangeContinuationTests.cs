using System.IO;
using System.Text.Json;
using WarnoLiteModdingTool.Core.Changes;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private const string OtherChangeNdf = "CommonData/Other.ndf";
    private static void AddIndependentChange(ChangeFixture f, int old = 50, int modified = 60)
    {
        ChangeFixture.Write(f.Basis, OtherChangeNdf, ChangeText(old).Replace("export Unit ", "export Other "));
        ChangeFixture.Write(f.Source, OtherChangeNdf, ChangeText(modified).Replace("export Unit ", "export Other "));
        ChangeFixture.Write(f.Target, OtherChangeNdf, ChangeText(80, 9).Replace("export Unit ", "export Other "));
    }
    private static Task ChangePartialContinuation()
    {
        using var f = new ChangeFixture(); AddIndependentChange(f); var package = f.Capture(NumericPolicy.Ratio);
        var p = ChangeRestore.Prepare(package, f.Target); Assert(p.Groups.Count == 2 && p.CanApply, "independent quantity groups");
        p = ChangeRestore.Prepare(package, f.Target, options: new([ChangeNdf]));
        Assert(p.CanApply && p.SelectedPaths.Count == 1 && p.DeferredPaths.SequenceEqual(new[] { OtherChangeNdf }), "explicit partial preview");
        var a = ChangeTransactions.Commit(p); Assert(a.DeferredPaths.Count == 1 && a.RecordPaths.Count == 1, "partial receipt retains remaining work");
        p = ChangeRestore.Prepare(package, f.Target); Assert(!p.AlreadyApplied && p.AppliedPaths.Count == 1 && p.Files.Count == 1 && p.CanApply, "continue remaining only");
        ChangeReject(() => ChangeTransactions.Commit(p, afterWrite: _ => throw new IOException("injected continuation failure")), "failed second batch rolls back only that batch");
        p = ChangeRestore.Prepare(package, f.Target); Assert(p.CanApply && p.AppliedPaths.Count == 1, "first batch survives failed continuation");
        var b = ChangeTransactions.Commit(p);
        Assert(File.ReadAllText(Path.Combine(f.Target, ChangeNdf)).Contains("SupplyCapacity = 180") && File.ReadAllText(Path.Combine(f.Target, OtherChangeNdf)).Contains("SupplyCapacity = 96"), "no repeated ratio on completed group");
        Assert(ChangeRestore.Prepare(package, f.Target).AlreadyApplied, "all groups completed");
        ChangeReject(() => ChangeTransactions.Recover(f.Target, a.Id), "older batch cannot invalidate newer receipts");
        ChangeTransactions.Recover(f.Target, b.Id);
        Assert(ChangeRestore.Prepare(package, f.Target).AppliedPaths.Count == 1, "latest rollback leaves older group applied");
        ChangeTransactions.Recover(f.Target, a.Id);
        Assert(ChangeRestore.Prepare(package, f.Target).AppliedPaths.Count == 0, "all rollback returns clean baseline");
        return Task.CompletedTask;
    }
    private static Task ChangeGroupBoundaries()
    {
        using var f = new ChangeFixture(); AddIndependentChange(f, 0, 20); var package = f.Capture(NumericPolicy.Ratio);
        Assert(!ChangeRestore.Prepare(package, f.Target).CanApply, "one zero-ratio group conflicts");
        var partial = ChangeRestore.Prepare(package, f.Target, options: new([ChangeNdf]));
        Assert(partial.CanApply && partial.AllFiles.Any(x => x.Error is not null), "deferred errors remain visible without blocking independent group");
        var a = ChangeTransactions.Commit(partial);
        Assert(!ChangeRestore.Prepare(package.WithPolicy(NumericPolicy.Delta), f.Target).CanApply, "partial receipt prevents mixed policy");
        ChangeFixture.Write(f.Target, OtherChangeNdf, ChangeText(900).Replace("export Unit ", "export Other "));
        Assert(!ChangeRestore.Prepare(package, f.Target).CanApply, "external change in deferred group invalidates continuation baseline");
        ChangeTransactions.Recover(f.Target, a.Id);
        ChangeFixture.Write(f.Target, "GameData/bridge.ndf", "Bridge is TTest(A=~/Unit B=~/Other)\n");
        var linked = ChangeRestore.Prepare(package, f.Target, options: new([ChangeNdf]));
        Assert(linked.Groups.Count == 1 && linked.Errors.Any(e => e.Contains("cannot be split")), "unchanged bridge joins referenced groups");
        ChangeFixture.Write(f.Source, "GameData/unknown.bin", "resource");
        Assert(ChangeRestore.Prepare(f.Capture(), f.Target, options: new([ChangeNdf])).Groups.Count == 1, "unknown resource relations stay whole");
        return Task.CompletedTask;
    }
    private static Task ChangeExplicitMapping()
    {
        using var f = new ChangeFixture(); var package = f.Capture(NumericPolicy.Ratio); var moved = "CommonData/moved.ndf";
        File.Delete(Path.Combine(f.Target, ChangeNdf));
        ChangeFixture.Write(f.Target, moved, ChangeText(150, 9).Replace("export Unit ", "export Renamed ") + "\nUnrelated is TTest(Value=42)\n");
        Assert(!ChangeRestore.Prepare(package, f.Target).CanApply, "missing old target requires explicit mapping");
        Assert(ChangeMerge.MappingSources(package, ChangeNdf).Single().Name == "Unit" && ChangeMerge.MappingTargets(f.Target).Any(x => x.Name == "Renamed"), "real source and target candidates");
        var map = new ChangeFileMapping(ChangeNdf, moved, new Dictionary<string, string> { ["Unit"] = "Renamed" });
        var p = ChangeRestore.Prepare(package, f.Target, options: new(Mappings: [map]));
        Assert(p.CanApply && p.Files.Single().RecordPath == ChangeNdf, string.Join("\n", p.Errors));
        var j = ChangeTransactions.Commit(p); var text = File.ReadAllText(Path.Combine(f.Target, moved));
        Assert(text.Contains("export Renamed") && text.Contains("SupplyCapacity = 180") && text.Contains("Other = 9") && text.Contains("Value=42") && !File.Exists(Path.Combine(f.Target, ChangeNdf)), "mapping modifies only target quantity spans");
        var reopened = ChangeRestore.Prepare(package, f.Target); Assert(reopened.AlreadyApplied && reopened.Mappings.Single().TargetPath == moved, "mapping is restored from receipt");
        Assert(!ChangeRestore.Prepare(package, f.Target, options: new(Mappings: [map with { TargetPath = ChangeNdf }])).CanApply, "cannot alter completed mapping");
        ChangeTransactions.Recover(f.Target, j.Id);
        var wrong = map with { Objects = new Dictionary<string, string> { ["Unit"] = "Unrelated" } };
        Assert(!ChangeRestore.Prepare(package, f.Target, options: new(Mappings: [wrong])).CanApply, "different type mapping rejected");
        ChangeFixture.Write(f.Source, ChangeNdf, ChangeText(120).Replace("Other = 7", "Other = 8"));
        ChangeReject(() => ChangeMerge.MappingSources(f.Capture(), ChangeNdf), "unknown identity/structure cannot be mapped by guessing");
        return Task.CompletedTask;
    }
    private static Task ChangeLegacyAndReceiptRace()
    {
        using var f = new ChangeFixture(); var package = f.Capture(); var p = ChangeRestore.Prepare(package, f.Target);
        var j = ChangeTransactions.Commit(p);
        ChangeReject(() => ChangeTransactions.Commit(p), "stale receipt snapshot prevents repeat commit");
        var path = Path.Combine(f.Target, ".warno-editor/change-transactions", j.Id, "record.json");
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(j with { Version = 2 }, ChangeJson.Options));
        Assert(ChangeRestore.Prepare(package, f.Target).AlreadyApplied, "preview.2 grouped transaction still read");
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(new ChangeJournal(1, j.Id, j.PackageIdentity, j.CreatedUtc, j.State, j.Files), ChangeJson.Options));
        Assert(ChangeRestore.Prepare(package, f.Target).AlreadyApplied, "preview.1 transaction still read");
        ChangeFixture.Write(f.Target, ChangeNdf, ChangeText(999));
        Assert(!ChangeRestore.Prepare(package, f.Target).AlreadyApplied, "changed completed target is not reported successfully applied");
        ChangeFixture.Write(f.Target, ChangeNdf, ChangeText(170, 9));
        ChangeTransactions.Recover(f.Target, j.Id); Assert(ChangeRestore.Prepare(package, f.Target).CanApply, "legacy recovery works");
        return Task.CompletedTask;
    }
    private static Task ChangeCommonDataAndNoChanges()
    {
        using var f = new ChangeFixture();
        var b = Path.Combine(f.Root, "rules-base"); var m = Path.Combine(f.Root, "rules-mod"); var n = Path.Combine(f.Root, "rules-target");
        ChangeFixture.Write(b, OtherChangeNdf, ChangeText(100)); ChangeFixture.Write(m, OtherChangeNdf, ChangeText(120)); ChangeFixture.Write(n, OtherChangeNdf, ChangeText(150));
        Assert(ChangeRestore.Prepare(ChangeCapture.Capture(m, b, NumericPolicy.Delta), n).CanApply, "CommonData-only does not require units module");
        var empty = ChangeCapture.Capture(b, b, NumericPolicy.Ratio);
        Assert(empty.Manifest.Complete && empty.Manifest.Files.Count == 0 && !ChangeRestore.Prepare(empty, n).CanApply, "complete no-change record is not a write");
        return Task.CompletedTask;
    }
}
