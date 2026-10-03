using System.IO;
using System.Text;
using System.Text.Json;
using WarnoLiteModdingTool.Core.Changes;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static string ScalarText(int amount, string role, string factory, int other = 7) => ChangeText(amount, other)
        .Replace("TUnknown", $"TUnitUIModuleDescriptor(UnitRole = '{role}'), TProductionModuleDescriptor(FactoryType = EFactory/{factory}), TUnknown");
    private static void AddScalarChange(ChangeFixture f)
    {
        ChangeFixture.Write(f.Basis, ChangeNdf, ScalarText(100, "Old", "Old"));
        ChangeFixture.Write(f.Source, ChangeNdf, ScalarText(120, "Recorded", "Recorded"));
        ChangeFixture.Write(f.Target, ChangeNdf, ScalarText(150, "新版角色", "Upstream", 9));
    }
    private static ChangeTextDecision[] ScalarChoices(ChangePreview p, TextConflictChoice choice = TextConflictChoice.UseRecorded) =>
        p.AllFiles.SelectMany(f => f.Details).Where(d => d.Conflict is { Kind: ChangeConflictKind.NdfScalar }).Select(d => new ChangeTextDecision(d.Conflict!, choice)).ToArray();
    private static Task ChangeScalarChoices()
    {
        using var f = new ChangeFixture(); AddScalarChange(f);
        foreach (var policy in new[] { NumericPolicy.Delta, NumericPolicy.Ratio })
        {
            var package = f.Capture(policy); var initial = ChangeRestore.Prepare(package, f.Target); var choices = ScalarChoices(initial);
            Assert(!initial.CanApply && choices.Length == 2 && initial.Files.Single().Before!.SequenceEqual(initial.Files.Single().After!), "all scalar conflicts visible without a partial candidate");
            Assert(choices.Single(c => c.Conflict.Column.EndsWith("UnitRole")).Conflict.Target == "'新版角色'", "scalar evidence preserves raw quoted text");
            choices[1] = choices[1] with { Choice = TextConflictChoice.KeepTarget };
            var resolved = ChangeRestore.Prepare(package, f.Target, options: new(Decisions: choices));
            Assert(resolved.CanApply, string.Join("\n", resolved.Errors));
            var text = Encoding.UTF8.GetString(resolved.Files.Single().After!);
            Assert(text == ScalarText(policy == NumericPolicy.Delta ? 170 : 180, "Recorded", "Upstream", 9), "mixed choices and global arithmetic only change selected spans");
            var partial = ChangeRestore.Prepare(package, f.Target, options: new(Decisions: [choices[0]]));
            Assert(!partial.CanApply && ScalarChoices(partial).Length == 2, "one selected conflict cannot hide another");
            var undecided = ChangeReviewSessions.Save(partial);
            Assert(undecided.Version == 2 && !ChangeReviewSessions.Load(package, f.Target).CanApply, "incomplete review stays incomplete after loading");
        }
        // Three distinct quoted values may contain escaped quotes and punctuation.
        ChangeFixture.Write(f.Source, ChangeNdf, ScalarText(120, "作者\\'角色;[]", "Recorded"));
        var specialPackage = f.Capture(); var special = ChangeRestore.Prepare(specialPackage, f.Target);
        special = ChangeRestore.Prepare(specialPackage, f.Target, options: new(Decisions: ScalarChoices(special)));
        Assert(special.CanApply && Encoding.UTF8.GetString(special.Files.Single().After!).Contains("'作者\\'角色;[]'"), "quoted literal remains one exact NDF token");
        // Already matching literals keep target text; no choice is required.
        ChangeFixture.Write(f.Target, ChangeNdf, ScalarText(150, "作者\\'角色;[]", "Recorded", 9));
        Assert(ChangeRestore.Prepare(specialPackage, f.Target).CanApply, "matching NDF literals need no decision");
        string IdentityLabels(string country, string coalition, int other) => $"Unit is TEntityDescriptor(ModulesDescriptors = [TTypeUnitModuleDescriptor(MotherCountry = '{country}', Coalition = ECoalition/{coalition}), TUnknown(Other = {other})])\n";
        ChangeFixture.Write(f.Basis, ChangeNdf, IdentityLabels("Old", "Old", 7)); ChangeFixture.Write(f.Source, ChangeNdf, IdentityLabels("Recorded", "Recorded", 7));
        ChangeFixture.Write(f.Target, ChangeNdf, IdentityLabels("Upstream", "Upstream", 9)); var labelsPackage = f.Capture();
        var labels = ChangeRestore.Prepare(labelsPackage, f.Target); Assert(ScalarChoices(labels).Length == 2, "known country and coalition literals are offered");
        labels = ChangeRestore.Prepare(labelsPackage, f.Target, options: new(Decisions: ScalarChoices(labels)));
        Assert(labels.CanApply && Encoding.UTF8.GetString(labels.Files.Single().After!) == IdentityLabels("Recorded", "Recorded", 9), "display/category literals preserve unrelated upstream content");
        return Task.CompletedTask;
    }
    private static Task ChangeScalarBoundaries()
    {
        using var f = new ChangeFixture(); AddScalarChange(f); var package = f.Capture(); var choices = ScalarChoices(ChangeRestore.Prepare(package, f.Target));
        void Blocked(ChangeRestoreOptions options, string label) => Assert(!ChangeRestore.Prepare(package, f.Target, options: options).CanApply, label);
        Blocked(new(Decisions: [choices[0] with { Conflict = choices[0].Conflict with { Before = "'Forged'" } }, choices[1]]), "altered evidence rejected");
        Blocked(new(Decisions: [.. choices, new(choices[0].Conflict with { Column = "TSupplyModuleDescriptor.SupplyCapacity" }, TextConflictChoice.KeepTarget)]), "numeric field cannot be forced by scalar choice");
        ChangeReject(() => ChangeRestore.Prepare(package, f.Target, options: new(Decisions: [choices[0], choices[0]])), "duplicate NDF choices");
        ChangeReject(() => ChangeRestore.Prepare(package, f.Target, options: new(Decisions: [choices[0] with { Conflict = choices[0].Conflict with { Kind = (ChangeConflictKind)9 } }])), "unknown conflict kind");
        ChangeFixture.Write(f.Target, ChangeNdf, ScalarText(150, "Later", "Upstream", 9)); Blocked(new(Decisions: choices), "changed target invalidates choice");
        AddScalarChange(f);
        foreach (var replacement in new[]
        {
            ("TUnitUIModuleDescriptor(UnitRole = 'Recorded')", "TUnknownLiteral(UnitRole = 'Recorded')"),
            ("EFactory/Recorded", "EOtherFamily/Recorded"),
            ("'Recorded'", "~/Recorded"),
            ("// unchanged comment", "// author also changed comment"),
            ("TUnknown(Other = 7)", "TUnknown(Other = 8)")
        })
        {
            ChangeFixture.Write(f.Source, ChangeNdf, ScalarText(120, "Recorded", "Recorded").Replace(replacement.Item1, replacement.Item2));
            var p = f.Capture(); var initial = ChangeRestore.Prepare(p, f.Target);
            Assert(!ChangeRestore.Prepare(p, f.Target, options: new(Decisions: ScalarChoices(initial))).CanApply, "choices cannot force unknown fields, enum family, references, comments or unknown numbers");
        }
        AddScalarChange(f);
        foreach (var target in new[]
        {
            ScalarText(150, "新版角色", "Upstream", 9).Replace("TUnitUIModuleDescriptor(UnitRole = '新版角色')", "TUnitUIModuleDescriptor()"),
            ScalarText(150, "新版角色", "Upstream", 9).Replace("UnitRole = '新版角色'", "UnitRole = '新版角色', UnitRole = 'Other'"),
            ScalarText(150, "新版角色", "Upstream", 9).Replace("TUnknown(Other = 9)", "TUnitUIModuleDescriptor(UnitRole = 'Second')"),
            ScalarText(150, "新版角色", "Upstream", 9).Replace("export Unit is", "export Renamed is"),
            ScalarText(150, "新版角色", "Upstream", 9).Replace("TEntityDescriptor", "TOtherDescriptor")
        })
        {
            ChangeFixture.Write(f.Target, ChangeNdf, target); Blocked(new(Decisions: choices), "missing or ambiguous field/object/type remains blocked");
        }
        AddScalarChange(f); ChangeFixture.Write(f.Basis, ChangeNdf, ScalarText(0, "Old", "Old"));
        Assert(!ChangeRestore.Prepare(f.Capture(NumericPolicy.Ratio), f.Target, options: new(Decisions: choices)).CanApply, "literal choices cannot bypass zero-base ratio");
        AddScalarChange(f); Blocked(new(Mappings: [new(ChangeNdf, ChangeNdf, new Dictionary<string, string> { ["Unit"] = "Unit" })], Decisions: choices), "mapping cannot silently ignore scalar decisions");
        return Task.CompletedTask;
    }
    private static Task ChangeScalarExcludedIdentity()
    {
        using var f = new ChangeFixture();
        foreach (var field in new[] { "DescriptorId", "NameInMenuToken", "Reference", "UnknownText" })
        {
            string Make(string value, int other) => $"export Unit is TEntityDescriptor({field} = {value}, Other = {other})\n";
            ChangeFixture.Write(f.Basis, ChangeNdf, Make("'Old'", 7)); ChangeFixture.Write(f.Source, ChangeNdf, Make("'Recorded'", 7)); ChangeFixture.Write(f.Target, ChangeNdf, Make("'Upstream'", 9));
            var p = ChangeRestore.Prepare(f.Capture(), f.Target);
            Assert(!p.CanApply && ScalarChoices(p).Length == 0, "unknown literals/identity do not expose force choices");
        }
        string Ref(string value) => $"Unit is TEntityDescriptor(ModulesDescriptors = [TExperienceModuleDescriptor(ExperienceLevelsPackDescriptor = ~/{value})])\n";
        ChangeFixture.Write(f.Basis, ChangeNdf, Ref("Old")); ChangeFixture.Write(f.Source, ChangeNdf, Ref("Recorded")); ChangeFixture.Write(f.Target, ChangeNdf, Ref("Upstream"));
        Assert(ScalarChoices(ChangeRestore.Prepare(f.Capture(), f.Target)).Length == 0, "choice metadata does not turn references into enum literals");
        foreach (var (root, value) in new[] { (f.Basis, "Old"), (f.Source, "Recorded"), (f.Target, "Upstream") })
            ChangeFixture.Write(root, ChangeNdf, Ref(value).Replace("~/", "EFake/"));
        Assert(ScalarChoices(ChangeRestore.Prepare(f.Capture(), f.Target)).Length == 0, "reference fields remain excluded even with enum-shaped invalid evidence");
        // Existing legal Boolean literals have only two values; normal three-way merge resolves them.
        string Ammo(string value, int other) => $"Ammo is TAmmunitionDescriptor(CanShootOnPosition = {value}, Other = {other})\n";
        ChangeFixture.Write(f.Basis, ChangeNdf, Ammo("False", 7)); ChangeFixture.Write(f.Source, ChangeNdf, Ammo("True", 7)); ChangeFixture.Write(f.Target, ChangeNdf, Ammo("True", 9));
        var boolPreview = ChangeRestore.Prepare(f.Capture(), f.Target); Assert(boolPreview.CanApply && ScalarChoices(boolPreview).Length == 0, "valid Boolean equality needs no artificial conflict");
        ChangeFixture.Write(f.Target, ChangeNdf, Ammo("Unknown", 9));
        Assert(!ChangeRestore.Prepare(f.Capture(), f.Target).CanApply && ScalarChoices(ChangeRestore.Prepare(f.Capture(), f.Target)).Length == 0, "invalid Boolean cannot be forced");
        return Task.CompletedTask;
    }
    private static Task ChangeScalarTransactions()
    {
        using var f = new ChangeFixture(); AddScalarChange(f); AddTextChange(f);
        var package = f.Capture(); var initial = ChangeRestore.Prepare(package, f.Target);
        var decisions = initial.AllFiles.SelectMany(x => x.Details).Where(d => d.Conflict is not null).Select(d => new ChangeTextDecision(d.Conflict!, TextConflictChoice.KeepTarget)).ToArray();
        var original = File.ReadAllBytes(Path.Combine(f.Target, ChangeNdf));
        var preview = ChangeRestore.Prepare(package, f.Target, options: new(Decisions: decisions));
        Assert(preview.CanApply && preview.Groups.Count == 1, "NDF and CSV choices participate in complete conservative group");
        ChangeReject(() => ChangeTransactions.Commit(preview, afterWrite: _ => throw new IOException("injected scalar failure")), "scalar batch rollback");
        Assert(original.SequenceEqual(File.ReadAllBytes(Path.Combine(f.Target, ChangeNdf))) && !ChangeRestore.Prepare(package, f.Target).AllProcessed, "rollback restores numeric edits and discards completion");
        preview = ChangeRestore.Prepare(package, f.Target, options: new(Decisions: decisions)); var journal = ChangeTransactions.Commit(preview);
        var reopened = ChangeRestore.Prepare(package, f.Target);
        Assert(journal.Version == 4 && journal.Decisions.Count == 3 && reopened.AllProcessed && !reopened.AlreadyApplied && reopened.RetainedChanges == 3, "mixed retained choices remain explicit in v4 receipt");
        Assert(reopened.AllFiles.Sum(x => x.Details.Count) == 3 && !reopened.CanApply, "reopening exposes omissions and prevents repeat arithmetic");
        var receipt = Path.Combine(f.Target, ".warno-editor/change-transactions", journal.Id, "record.json");
        File.WriteAllBytes(receipt, JsonSerializer.SerializeToUtf8Bytes(journal with { Version = 3 }, ChangeJson.Options));
        ChangeReject(() => ChangeTransactions.List(f.Target), "NDF choices cannot masquerade as v3 receipt");
        File.WriteAllBytes(receipt, JsonSerializer.SerializeToUtf8Bytes(journal, ChangeJson.Options));
        ChangeTransactions.Recover(f.Target, journal.Id); Assert(original.SequenceEqual(File.ReadAllBytes(Path.Combine(f.Target, ChangeNdf))), "v4 recovery restores target bytes");
        var restored = ChangeTransactions.Commit(ChangeRestore.Prepare(package, f.Target, options: new(Decisions: decisions.Select(d => d with { Choice = TextConflictChoice.UseRecorded }).ToArray())));
        Assert(ChangeRestore.Prepare(package, f.Target).AlreadyApplied, "all use-recorded outcomes count as restored");
        ChangeTransactions.Recover(f.Target, restored.Id); return Task.CompletedTask;
    }
    private static Task ChangeScalarPersistence()
    {
        using var f = new ChangeFixture(); AddScalarChange(f); var package = f.Capture();
        var path = Path.Combine(f.Root, "scalar.wlmtchanges"); ChangePackageStore.Save(package, path);
        var initial = ChangeRestore.Prepare(package, f.Target); var preview = ChangeRestore.Prepare(package, f.Target, options: new(Decisions: ScalarChoices(initial)));
        var saved = ChangeReviewSessions.Save(preview); Assert(saved.Version == 2 && package.Manifest.Version == 1, "NDF review uses v2 without changing portable package");
        Directory.Move(f.Source, Path.Combine(f.Root, "unavailable")); Directory.Move(f.Basis, Path.Combine(f.Root, "old-base-unavailable"));
        var loaded = ChangeReviewSessions.Load(ChangePackageStore.Load(path), f.Target);
        Assert(loaded.CanApply && loaded.Decisions.SequenceEqual(preview.Decisions), "independent package and typed review restored without original inputs");
        var progress = Path.Combine(f.Target, ".warno-editor/change-review", saved.Id + ".json");
        File.WriteAllBytes(progress, JsonSerializer.SerializeToUtf8Bytes(saved with { Version = 1 }, ChangeJson.Options));
        ChangeReject(() => ChangeReviewSessions.Load(package, f.Target), "NDF decisions cannot masquerade as v1 review");
        File.WriteAllBytes(progress, JsonSerializer.SerializeToUtf8Bytes(saved, ChangeJson.Options));
        ChangeFixture.Write(f.Target, ChangeNdf, ScalarText(150, "Later", "Upstream", 9));
        ChangeReject(() => ChangeReviewSessions.Load(package, f.Target), "changed target invalidates saved NDF choice");
        Assert(File.Exists(progress), "invalidated review retained"); return Task.CompletedTask;
    }
}
