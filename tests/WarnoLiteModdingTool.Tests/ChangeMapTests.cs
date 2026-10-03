using System.IO;
using System.Text;
using WarnoLiteModdingTool.Core.Changes;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static string VisionMap(string entries, int other = 7, string newline = "\n", bool named = true) =>
        ((named ? "export Unit is TEntityDescriptor(\n ModulesDescriptors = [" : "") + "TScannerConfigurationDescriptor(\n    VisionRangesGRU = MAP [\n" + entries +
        "    ]\n    Other = " + other + "\n)" + (named ? "]\n)" : "") + "\n").Replace("\n", newline, StringComparison.Ordinal);
    private const string MapStandard = "        (EVisionRange/Standard, 100), // standard\n";
    private const string MapLow = "        (EVisionRange/LowAltitude, 50), // low\n";
    private const string MapHigh = "        (EVisionRange/HighAltitude, 300), // author addition\n";
    private const string MapUpstream = "        (EVisionRange/Future, 777), // new version only\n";
    private static void AddMapChange(ChangeFixture f)
    {
        ChangeFixture.Write(f.Basis, ChangeNdf, VisionMap(MapStandard + MapLow));
        ChangeFixture.Write(f.Source, ChangeNdf, VisionMap(MapStandard.Replace("100", "120") + MapHigh));
        ChangeFixture.Write(f.Target, ChangeNdf, VisionMap(MapUpstream + MapLow + MapStandard.Replace("100", "150"), 9, "\r\n"));
    }
    private static Task ChangeMapMixedMerge()
    {
        using var f = new ChangeFixture(); AddMapChange(f);
        foreach (var policy in new[] { NumericPolicy.Delta, NumericPolicy.Ratio })
        {
            var package = f.Capture(policy); var p = ChangeRestore.Prepare(package, f.Target);
            Assert(p.CanApply, string.Join("\n", p.Errors)); var text = Encoding.UTF8.GetString(p.Files.Single().After!);
            Assert(text == VisionMap(MapUpstream + MapStandard.Replace("100", policy == NumericPolicy.Delta ? "170" : "180") + MapHigh, 9, "\r\n"), "keyed MAP merge retains target order, other entries, comment, newline and fields");
            Assert(p.Files.Single().Details.Count == 3 && p.Files[0].Details.Any(d => d.Field.Contains("HighAltitude") && d.Status.Contains("recorded value")) && p.Files[0].Details.Any(d => d.Field.Contains("LowAltitude") && d.Status.Contains("Remove")), "all added, removed and arithmetic outcomes visible");
            Assert(!ChangeMerge.QuantityOnly(ChangeNdf, package.Before(package.Manifest.Files.Single()), package.After(package.Manifest.Files.Single())), "MAP structure is never treated as quantity-only grouping or mapping");
        }
        var captured = f.Capture(); var details = ChangeMerge.Describe(ChangeNdf, captured.Before(captured.Manifest.Files[0]), captured.After(captured.Manifest.Files[0]));
        Assert(details.Count == 3 && details.Any(d => d.Before.Contains("Missing")) && details.Any(d => d.After.Contains("Missing")), "standalone record describes MAP additions and removals");
        return Task.CompletedTask;
    }
    private static Task ChangeMapBoundaries()
    {
        using var f = new ChangeFixture(); AddMapChange(f); var package = f.Capture();
        void RejectTarget(string entries, string reason)
        {
            ChangeFixture.Write(f.Target, ChangeNdf, VisionMap(entries, 9));
            var preview = ChangeRestore.Prepare(package, f.Target);
            Assert(!preview.CanApply && preview.Files.Single().Error is not null && preview.Files.Single().Before!.SequenceEqual(preview.Files.Single().After!), reason);
        }
        RejectTarget(MapStandard.Replace("100", "150") + MapLow.Replace("50", "60"), "updated deleted value blocks entire candidate");
        RejectTarget(MapStandard.Replace("100", "150") + MapLow.Replace("// low", "// upstream note"), "updated deleted comment is not silently discarded");
        RejectTarget(MapStandard.Replace("100", "150") + MapLow + MapHigh.Replace("300", "400"), "added key occupied with another value blocks");
        RejectTarget(MapStandard.Replace("100", "150") + MapLow + MapStandard, "duplicate target key blocks");
        RejectTarget(MapStandard.Replace("100", "150 * 2") + MapLow, "expression is not interpreted as quantity");
        RejectTarget(MapLow, "changed quantity missing in target is not recreated");
        AddMapChange(f); ChangeFixture.Write(f.Source, ChangeNdf, VisionMap(MapStandard.Replace("100", "120") + MapHigh.Replace("HighAltitude", "Unknown")));
        Assert(!ChangeRestore.Prepare(f.Capture(), f.Target).CanApply, "new unknown key remains a preserved conflict");
        AddMapChange(f); ChangeFixture.Write(f.Source, ChangeNdf, VisionMap(MapStandard.Replace("100", "120").Replace("// standard", "// unrelated changed comment") + MapHigh));
        Assert(!ChangeRestore.Prepare(f.Capture(), f.Target).CanApply, "unexplained comment change is not silently dropped");
        return Task.CompletedTask;
    }
    private static Task ChangeMapSeparatorsAndSatisfied()
    {
        using var f = new ChangeFixture();
        var standardNoComma = MapStandard.Replace("),", ")");
        ChangeFixture.Write(f.Basis, ChangeNdf, VisionMap(standardNoComma));
        ChangeFixture.Write(f.Source, ChangeNdf, VisionMap(MapStandard + MapLow + MapHigh.Replace("),", ")")));
        ChangeFixture.Write(f.Target, ChangeNdf, VisionMap(standardNoComma.Replace("100", "150"), 9));
        var package = f.Capture(NumericPolicy.Ratio); var p = ChangeRestore.Prepare(package, f.Target);
        Assert(p.CanApply, string.Join("\n", p.Errors));
        Assert(Encoding.UTF8.GetString(p.Files.Single().After!) == VisionMap(MapStandard.Replace("100", "150") + MapLow + MapHigh, 9), "missing final comma inserted before comment and multiple additions separated");
        ChangeFixture.Write(f.Target, ChangeNdf, VisionMap(MapHigh + MapStandard.Replace("100", "150") + MapLow, 9));
        p = ChangeRestore.Prepare(package, f.Target); Assert(p.CanApply && p.Files.Single().Before!.SequenceEqual(p.Files.Single().After!), "already-present additions stay unchanged and are not duplicated");
        AddMapChange(f); package = f.Capture(); ChangeFixture.Write(f.Target, ChangeNdf, VisionMap(MapStandard.Replace("100", "150") + MapHigh, 9));
        p = ChangeRestore.Prepare(package, f.Target); Assert(p.CanApply && p.Files.Single().Details.Any(d => d.Status.Contains("Removal already satisfied")), "already absent removed key is satisfied");
        ChangeFixture.Write(f.Basis, ChangeNdf, VisionMap("")); ChangeFixture.Write(f.Source, ChangeNdf, VisionMap(MapHigh)); ChangeFixture.Write(f.Target, ChangeNdf, VisionMap("", 9));
        p = ChangeRestore.Prepare(f.Capture(NumericPolicy.Ratio), f.Target); Assert(p.CanApply && Encoding.UTF8.GetString(p.Files.Single().After!).Contains("HighAltitude, 300"), "missing entry is not treated as a zero numeric baseline");
        AddMapChange(f); ChangeFixture.Write(f.Basis, ChangeNdf, VisionMap(MapStandard.Replace("100", "0") + MapLow));
        p = ChangeRestore.Prepare(f.Capture(NumericPolicy.Ratio), f.Target); Assert(!p.CanApply && p.Files.Single().Details.Any(d => d.Before == "0" && d.Status.Contains("zero baseline")), "existing zero still blocks ratio and retains evidence");
        return Task.CompletedTask;
    }
    private static Task ChangeMapUnnamedAndIdentity()
    {
        using var f = new ChangeFixture(); AddMapChange(f);
        foreach (var root in new[] { f.Basis, f.Source, f.Target })
        {
            var text = File.ReadAllText(Path.Combine(root, ChangeNdf));
            ChangeFixture.Write(root, ChangeNdf, text.Replace("export Unit is TEntityDescriptor(\n ModulesDescriptors = [", "").Replace("export Unit is TEntityDescriptor(\r\n ModulesDescriptors = [", "").Replace(")]\n)", ")").Replace(")]\r\n)", ")"));
        }
        var p = ChangeRestore.Prepare(f.Capture(), f.Target); Assert(p.CanApply, string.Join("\n", p.Errors));
        Assert(p.Files.Single().Details.Count == 3 && Encoding.UTF8.GetString(p.Files.Single().After!).Contains("Standard, 170"), "unnamed unique constructor maps merge");
        foreach (var field in new[] { "UnitIds", "DivisionIds" })
        {
            string Registry(string entry, int other) => "TDeckSerializerEntries(\n " + field + " = MAP [\n    (Known, 1),\n" + entry + " ]\n Other = " + other + "\n)\n";
            ChangeFixture.Write(f.Basis, ChangeNdf, Registry("", 1)); ChangeFixture.Write(f.Source, ChangeNdf, Registry("    (Added, 2),\n", 1)); ChangeFixture.Write(f.Target, ChangeNdf, Registry("    (Upstream, 2),\n", 9));
            p = ChangeRestore.Prepare(f.Capture(NumericPolicy.Ratio), f.Target);
            Assert(!p.CanApply && p.Files.Single().Error!.Contains("identities") && p.Files.Single().Details.Any(d => d.Field.Contains("Added") && d.Status.Contains("identity")), "registration identities are shown and never calculated or auto-renumbered");
            ChangeReject(() => ChangeMerge.ValidateBytes(ChangeNdf, Encoding.UTF8.GetBytes(Registry("    (Added, 01),\n", 1))), "registration number aliases cannot evade duplicate checks");
        }
        ChangeReject(() => ChangeMerge.ValidateBytes(ChangeNdf, Encoding.UTF8.GetBytes(VisionMap(MapStandard + MapStandard))), "same-baseline known MAP duplicate keys rejected");
        ChangeReject(() => ChangeMerge.ValidateBytes(ChangeNdf, Encoding.UTF8.GetBytes(VisionMap(MapHigh.Replace("300", "-1"), named: false))), "unnamed known quantity map limits checked");
        const string ammoType = "TAmmunitionDescriptor";
        string Ammo(string entries, int other) => "Ammo is " + ammoType + "(\n BaseHitValueModifiers = MAP [\n" + entries + " ]\n Other = " + other + "\n)\n";
        const string idle = "    (EBaseHitValueModifier/Idling, 50),\n"; const string moving = "    (EBaseHitValueModifier/Moving, 30),\n";
        ChangeFixture.Write(f.Basis, ChangeNdf, Ammo(idle, 1)); ChangeFixture.Write(f.Source, ChangeNdf, Ammo(idle.Replace("50", "60") + moving, 1)); ChangeFixture.Write(f.Target, ChangeNdf, Ammo(idle.Replace("50", "70"), 2));
        p = ChangeRestore.Prepare(f.Capture(NumericPolicy.Ratio), f.Target);
        Assert(p.CanApply && Encoding.UTF8.GetString(p.Files.Single().After!).Contains("Idling, 84") && Encoding.UTF8.GetString(p.Files.Single().After!).Contains("Moving, 30"), "known ammo accuracy maps share global policy, additions use original quantity");
        ChangeFixture.Write(f.Source, ChangeNdf, Ammo(idle + moving.Replace("30", "2.5"), 1));
        Assert(!ChangeRestore.Prepare(f.Capture(), f.Target).CanApply, "new integer MAP quantity cannot be fractional");
        string Rule(string entries, int other) => "TWargameTunableConstante(\n BaseIncome = MAP [\n" + entries + " ]\n Other = " + other + "\n)\n";
        const string income = "    (ECombatRule/Conquest, 100),\n"; const string destruction = "    (ECombatRule/Destruction, 50),\n";
        ChangeFixture.Write(f.Basis, ChangeNdf, Rule(income, 1)); ChangeFixture.Write(f.Source, ChangeNdf, Rule(income.Replace("100", "120") + destruction, 1)); ChangeFixture.Write(f.Target, ChangeNdf, Rule(income.Replace("100", "150"), 2));
        p = ChangeRestore.Prepare(f.Capture(), f.Target);
        Assert(p.CanApply && Encoding.UTF8.GetString(p.Files.Single().After!).Contains("Conquest, 170") && Encoding.UTF8.GetString(p.Files.Single().After!).Contains("Destruction, 50"), "anonymous rule MAP uses registered rule semantics");
        return Task.CompletedTask;
    }
    private static Task ChangeMapTransactionAndLimits()
    {
        using var f = new ChangeFixture(); AddMapChange(f); AddIndependentChange(f);
        var package = f.Capture(); var p = ChangeRestore.Prepare(package, f.Target);
        Assert(p.CanApply && p.Groups.Count == 1, "structural MAP and other files remain a single dependency group");
        Assert(!ChangeRestore.Prepare(package, f.Target, options: new([ChangeNdf])).CanApply, "cannot split structural MAP from remaining files");
        var original = File.ReadAllBytes(Path.Combine(f.Target, ChangeNdf));
        ChangeReject(() => ChangeTransactions.Commit(p, afterWrite: _ => throw new IOException("injected")), "MAP batch failure restores all files");
        Assert(File.ReadAllBytes(Path.Combine(f.Target, ChangeNdf)).SequenceEqual(original), "MAP rollback byte identity");
        p = ChangeRestore.Prepare(package, f.Target); var j = ChangeTransactions.Commit(p);
        Assert(ChangeRestore.Prepare(package, f.Target).AlreadyApplied, "MAP changes not repeated on reopen"); ChangeTransactions.Recover(f.Target, j.Id);
        Assert(File.ReadAllBytes(Path.Combine(f.Target, ChangeNdf)).SequenceEqual(original), "MAP recovery byte identity");
        ChangeFixture.Write(f.Source, ChangeNdf, VisionMap(MapLow + MapStandard.Replace("100", "120")));
        Assert(!ChangeRestore.Prepare(f.Capture(), f.Target).CanApply, "recorded existing-key reordering requires review");
        AddMapChange(f); ChangeFixture.Write(f.Target, ChangeNdf, VisionMap(MapStandard + MapLow, 9).Replace("\n    ]", "]"));
        Assert(!ChangeRestore.Prepare(f.Capture(), f.Target).CanApply, "inline closing bracket does not receive guessed insertion");
        ChangeFixture.Write(f.Basis, ChangeNdf, VisionMap(MapStandard)); ChangeFixture.Write(f.Source, ChangeNdf, VisionMap(MapStandard.Replace("),", ")")));
        ChangeFixture.Write(f.Target, ChangeNdf, VisionMap(MapStandard.Replace("100", "150"), 9));
        Assert(!ChangeRestore.Prepare(f.Capture(), f.Target).CanApply, "comma-only author change is not silently ignored");
        ChangeFixture.Write(f.Source, ChangeNdf, VisionMap(MapStandard.Replace("100", "120")));
        ChangeFixture.Write(f.Target, ChangeNdf, VisionMap(MapStandard.Replace("100", "150") + MapUpstream.Replace("777", "SomeValue * 2"), 9));
        var compatible = ChangeRestore.Prepare(f.Capture(), f.Target);
        Assert(compatible.CanApply && Encoding.UTF8.GetString(compatible.Files.Single(f => f.Path == ChangeNdf).After!).Contains("SomeValue * 2"), "existing quantity-only edits still preserve unrelated new expressions");
        ChangeFixture.Write(f.Basis, ChangeNdf, VisionMap(MapStandard.Replace("100", "0")));
        ChangeFixture.Write(f.Target, ChangeNdf, VisionMap(MapStandard.Replace("100", "150"), 9).Replace("export Unit ", "export Renamed "));
        var mapped = ChangeRestore.Prepare(f.Capture(NumericPolicy.Ratio), f.Target, options: new(Mappings: [new(ChangeNdf, ChangeNdf, new Dictionary<string, string> { ["Unit"] = "Renamed" })]));
        Assert(!mapped.CanApply && mapped.AllFiles.Single(f => f.RecordPath == ChangeNdf).Details.Any(d => d.Before == "0" && d.Target == "150" && d.Status.Contains("zero baseline")), "mapping failure retains the known MAP calculation evidence");
        return Task.CompletedTask;
    }
}
