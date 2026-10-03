using System.IO;
using System.Text;
using WarnoLiteModdingTool.Core.Changes;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static string FieldAmmo(int damage, string fields, int other = 7, string newline = "\n") =>
        $"export Ammo is TAmmunitionDescriptor\n(\n    PhysicalDamages = {damage}\n{fields}    Other = {other}\n)\n".Replace("\n", newline);
    private const string FieldRemoved = "    SupplyCost = 2 // supply comment\n";
    private const string FieldAdded = "    IsFireAndForget = True // author comment\n    AimingTime = 5\n";
    private static void AddFieldChange(ChangeFixture f)
    {
        ChangeFixture.Write(f.Basis, ChangeNdf, FieldAmmo(100, FieldRemoved));
        ChangeFixture.Write(f.Source, ChangeNdf, FieldAmmo(120, FieldAdded));
        ChangeFixture.Write(f.Target, ChangeNdf, FieldAmmo(150, FieldRemoved + "    UpstreamOnly = 42\n", 9, "\r\n"));
    }
    private static Task ChangeFieldPresenceMerge()
    {
        using var f = new ChangeFixture(); AddFieldChange(f);
        foreach (var policy in new[] { NumericPolicy.Ratio, NumericPolicy.Delta })
        {
            var package = f.Capture(policy); var file = package.Manifest.Files.Single();
            var description = ChangeMerge.Describe(file.Path, package.Before(file), package.After(file));
            Assert(description.Count(d => d.Status.Contains("Field added or removed")) == 3, "standalone package describes field presence changes");
            var preview = ChangeRestore.Prepare(package, f.Target); Assert(preview.CanApply, string.Join("\n", preview.Errors));
            var expected = FieldAmmo(policy == NumericPolicy.Ratio ? 180 : 170, "    UpstreamOnly = 42\n", 9, "\r\n").Replace(")\r\n", FieldAdded.Replace("\n", "\r\n") + ")\r\n");
            Assert(Encoding.UTF8.GetString(preview.Files.Single().After!) == expected, "added quantities use recorded literal and preserve target CRLF, unrelated values and comments");
            Assert(preview.Files.Single().Details.Count == 4 && preview.Files.Single().Details.Single(d => d.Field.EndsWith("AimingTime")).Result == "5", "presence changes and common arithmetic appear together");
        }
        // Additions already present with the recorded value and already absent deletions are no-ops.
        ChangeFixture.Write(f.Target, ChangeNdf, FieldAmmo(150, FieldAdded.Replace("= 5", "= 5.0"), 9));
        var satisfied = ChangeRestore.Prepare(f.Capture(), f.Target);
        Assert(satisfied.CanApply && satisfied.Files.Single().Details.Count(d => d.Status.Contains("already satisfied")) == 3, "presence satisfaction is distinct from numeric reapplication");
        Assert(Encoding.UTF8.GetString(satisfied.Files.Single().After!).Contains("AimingTime = 5.0"), "already satisfied target raw value is preserved");
        string Accuracy(int value) => $"    BaseHitValueModifiers = MAP [\n        (EBaseHitValueModifier/Idling, {value}),\n    ]\n";
        ChangeFixture.Write(f.Basis, ChangeNdf, FieldAmmo(100, FieldRemoved + Accuracy(50)));
        ChangeFixture.Write(f.Source, ChangeNdf, FieldAmmo(120, FieldAdded + Accuracy(60)));
        ChangeFixture.Write(f.Target, ChangeNdf, FieldAmmo(150, FieldRemoved + Accuracy(80), 9));
        var mixed = ChangeRestore.Prepare(f.Capture(NumericPolicy.Ratio), f.Target);
        Assert(mixed.CanApply && Encoding.UTF8.GetString(mixed.Files.Single().After!).Contains("(EBaseHitValueModifier/Idling, 96)"), "field presence and existing MAP arithmetic compose");
        // Existing modules, quoted strings and comma-style assignments use the same path.
        string Unit(string extra, int amount) => $"Unit is TEntityDescriptor\n(\n ModulesDescriptors = [\n  TUnitUIModuleDescriptor\n  (\n   DisplayRoadSpeedInKmph = {amount},\n{extra}  )\n ]\n)\n";
        ChangeFixture.Write(f.Basis, ChangeNdf, Unit("", 100)); ChangeFixture.Write(f.Source, ChangeNdf, Unit("   UnitRole = 'Recon', // new role\n", 120));
        ChangeFixture.Write(f.Target, ChangeNdf, Unit("   Other = MAP [ ('key', 7) ]\n", 150));
        var unit = ChangeRestore.Prepare(f.Capture(NumericPolicy.Ratio), f.Target);
        Assert(unit.CanApply && Encoding.UTF8.GetString(unit.Files.Single().After!).Contains("Other = MAP [ ('key', 7) ],\n   UnitRole = 'Recon', // new role"), "new literal adopts target comma style without altering unrelated MAP");
        return Task.CompletedTask;
    }
    private static Task ChangeFieldPresenceConflicts()
    {
        using var f = new ChangeFixture(); AddFieldChange(f); var package = f.Capture();
        foreach (var fields in new[] { FieldRemoved.Replace("= 2", "= 3"), FieldRemoved.Replace("supply comment", "upstream comment"), FieldRemoved + "    AimingTime = 8\n", FieldRemoved + "    AimingTime = 5\n    AimingTime = 5\n" })
        {
            ChangeFixture.Write(f.Target, ChangeNdf, FieldAmmo(150, fields, 9)); var blocked = ChangeRestore.Prepare(package, f.Target);
            Assert(!blocked.CanApply && blocked.Files.Single().Before!.SequenceEqual(blocked.Files.Single().After!), "changed deletion or occupied addition leaves target unchanged");
            Assert(blocked.Files.Single().Details.Any(d => d.Status.Contains("changed upstream") || d.Status.Contains("different value") || d.Status.Contains("Duplicate")), "field conflict keeps visible evidence");
        }
        AddFieldChange(f);
        foreach (var after in new[]
        {
            FieldAmmo(120, FieldAdded).Replace("AimingTime = 5", "UnknownNumber = 5"),
            FieldAmmo(120, FieldAdded).Replace("AimingTime = 5", "AimingTime = ~/Expression"),
            FieldAmmo(120, FieldAdded).Replace("AimingTime = 5", "ShotsCountPerSalvo = 1.5"),
            FieldAmmo(120, FieldAdded).Replace("IsFireAndForget = True // author comment", "IsFireAndForget = Unknown"),
            FieldAmmo(120, FieldAdded).Replace("    AimingTime = 5\n", "    AimingTime = 5 OtherInline = 2\n"),
            FieldAmmo(120, FieldAdded).Replace("Other = 7", "Other = 7 // additional author edit")
        })
        {
            ChangeFixture.Write(f.Source, ChangeNdf, after); Assert(!ChangeRestore.Prepare(f.Capture(), f.Target).CanApply, "unsupported structure/value still requests review");
        }
        AddFieldChange(f); ChangeFixture.Write(f.Target, ChangeNdf, FieldAmmo(150, FieldRemoved, 9).Replace("TAmmunitionDescriptor", "TUnknown"));
        Assert(!ChangeRestore.Prepare(package, f.Target).CanApply, "constructor identity must remain valid");
        // Preserve exact-object restoration even when the rest of the file changed upstream.
        AddFieldChange(f); ChangeFixture.Write(f.Target, ChangeNdf, FieldAmmo(100, FieldRemoved) + "OtherObject is TUnknown(Value = 9)\n");
        var exact = ChangeRestore.Prepare(package, f.Target); Assert(exact.CanApply && Encoding.UTF8.GetString(exact.Files.Single().After!).StartsWith(FieldAmmo(120, FieldAdded)), "unchanged object keeps existing exact restore behavior");
        return Task.CompletedTask;
    }
    private static Task ChangeFieldPresenceRoundtrip()
    {
        using var f = new ChangeFixture(); AddFieldChange(f); AddTextChange(f, "Old");
        var package = f.Capture(NumericPolicy.Ratio); var path = Path.Combine(f.Root, "fields.wlmtchanges"); ChangePackageStore.Save(package, path);
        Directory.Move(f.Source, Path.Combine(f.Root, "old-mod-unavailable")); var read = ChangePackageStore.Load(path);
        var preview = ChangeRestore.Prepare(read, f.Target); Assert(preview.CanApply && preview.Groups.Count == 1, string.Join("\n", preview.Errors));
        Assert(!ChangeRestore.Prepare(read, f.Target, options: new(SelectedPaths: [ChangeNdf])).CanApply, "field structure changes are not split from the group");
        var original = File.ReadAllBytes(Path.Combine(f.Target, ChangeNdf));
        ChangeReviewSessions.Save(preview); Assert(ChangeReviewSessions.Load(read, f.Target).CanApply, "existing saved review supports field presence changes");
        ChangeReject(() => ChangeTransactions.Commit(preview, afterWrite: _ => throw new IOException("injected field batch failure")), "failed batch recovers field additions and removals");
        Assert(File.ReadAllBytes(Path.Combine(f.Target, ChangeNdf)).SequenceEqual(original), "rollback restores exact original bytes");
        var journal = ChangeTransactions.Commit(ChangeRestore.Prepare(read, f.Target));
        Assert(journal.Version == 3 && read.Manifest.Version == 1 && ChangeRestore.Prepare(read, f.Target).AlreadyApplied, "formats unchanged and reopen never repeats arithmetic");
        ChangeTransactions.Recover(f.Target, journal.Id); Assert(File.ReadAllBytes(Path.Combine(f.Target, ChangeNdf)).SequenceEqual(original), "explicit recovery restores target fields");
        return Task.CompletedTask;
    }
}
