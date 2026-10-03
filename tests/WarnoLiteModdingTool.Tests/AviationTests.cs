using System.IO;
using System.Text;
using WarnoLiteModdingTool.Core.Batch;
using WarnoLiteModdingTool.Core.Changes;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private const string AviationTemplate = """
        template AirplaneMovementDescriptor
        [ AltitudeGRU, AltitudeMinGRU, SpeedInKmph, AgilityRadiusGRU, PitchAngleDegree, RollAngleDegree, RollSpeedDegreePerSecond, EvacAngleDegree, ] is TAirplaneMovementModuleDescriptor
        (
            AltitudeGRU = <AltitudeGRU>
            AltitudeMinGRU = <AltitudeMinGRU>
            AltitudeMaxGRU = ~/MaxAltitudeGRU
            SpeedInKmph = <SpeedInKmph>
        )
        """;
    private const string AviationModule = """
        AirplaneMovementDescriptor
        (
            AltitudeGRU = 700 // flight comment
            AltitudeMinGRU = 100
            SpeedInKmph = 65
            AgilityRadiusGRU = 1000
            PitchAngleDegree = 17
            RollAngleDegree = 60
            RollSpeedDegreePerSecond = 100
            EvacAngleDegree = 5
        ),
        """;
    private static string AviationFixture(string newline = "\n", bool direct = false)
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        var path = Path.Combine(root, "GameData/Generated/Gameplay/Gfx/UniteDescriptor.ndf");
        var module = direct ? AviationModule.Replace("AirplaneMovementDescriptor", "TAirplaneMovementModuleDescriptor").Replace("AltitudeMinGRU = 100", "AltitudeMinGRU = 100\n    AltitudeMaxGRU = 2000") : AviationModule;
        var text = File.ReadAllText(path).Replace("\r\n", "\n").Replace("DescriptorId = GUID:{10000000-0000-0000-0000-000000000001}", "DescriptorId = GUID:{10000000-0000-0000-0000-000000000001}\n    ClassNameForDebug = 'Unit_Test_Tank_US'").Replace("TVisibilityModuleDescriptor(UnitConcealmentBonus = 1.25),", module + "\n        TVisibilityModuleDescriptor(UnitConcealmentBonus = 1.25),");
        text += """

            export Descriptor_Unit_Test_Helicopter is TEntityDescriptor
            (
                ModulesDescriptors = [
                    TGenericMovementModuleDescriptor(MaxSpeedInKmph = 200),
                    THelicopterMovementModuleDescriptor(
                        MaxSpeedInKmph = 210
                        FlyingAltitudeGRU = 106
                        NearGroundFlyingAltitudeGRU = 22
                        UpwardSpeedInKmph = 72
                    ),
                ]
            )
            """;
        File.WriteAllText(path, text.Replace("\n", newline), new UTF8Encoding(false));
        ChangeFixture.Write(root, "GameData/Gameplay/Unit/Tactic/ModulesStandard.ndf", AviationTemplate);
        ChangeFixture.Write(root, "GameData/Gameplay/Unit/AirplaneConstantes.ndf", "MaxAltitudeGRU is 2000\n");
        return root;
    }
    private static DraftOperation AviationOp(UnitRecord unit, string key, string value)
    {
        var field = unit.Field(key)!;
        Assert(UnitValueConverter.TryFormatTarget(field, value, out var normalized, out var raw, out var error), error);
        return CreateFieldDraft(unit, field, normalized, raw);
    }
    private static async Task AviationContracts()
    {
        foreach (var direct in new[] { false, true })
        {
            var root = AviationFixture(direct: direct);
            try
            {
                var (data, plane) = await LoadTankAsync(root);
                Assert(plane.Fields.Count(f => f.Definition.Key.StartsWith("flight.") && f.CanEdit) == 8, "template and direct constructors expose all eight fields");
                var heli = data.Units.Single(u => u.Name.EndsWith("_Helicopter"));
                Assert(heli.Fields.Count(f => f.Definition.Key.StartsWith("helicopter.") && f.CanEdit) == 4, "helicopter fields");
                Assert(!AviationMovement.Relevant(data.Units.Single(u => u.Name.EndsWith("Recon_SOV")), "flight.altitude"), "ground units hide aviation sections");
                foreach (var input in new[] { "-1", "NaN", "Infinity", "1e999", "~/Value", "2 * 100" })
                    Assert(!UnitValueConverter.TryFormatTarget(plane.Field("flight.altitude")!, input, out _, out _, out _), "reject invalid input " + input);
                Assert(!UnitValueConverter.TryFormatTarget(plane.Field("flight.turnRadius")!, "0", out _, out _, out _), "radius must be positive");
                var speed = AviationMovement.SpeedDrafts(heli, "250");
                Assert(speed.Upserts.Count == 2 && speed.Upserts.All(o => o.TargetValue == "250"), "synchronize originally different speeds only on edit");
                Assert(AviationMovement.SpeedDrafts(heli, "200").Removals.Count == 2, "reset preserves original 200/210 difference");
                var batch = UnitBatchPlanner.Preview(new("flight-batch", data.Units, "flight.altitude", UnitBatchOperation.Add, "50", UnitBatchRounding.None, null, null, []));
                Assert(batch.CanAddToDrafts && batch.CompatibleCount == 1 && batch.Upserts.Single().TargetValue == "750" && batch.Warnings.Count > 0, "mixed batch skips non-airplanes");
                var speedBatch = UnitBatchPlanner.Preview(new("speed-batch", [plane, heli], AviationMovement.Speed, UnitBatchOperation.Multiply, "2", UnitBatchRounding.None, null, null, []));
                Assert(speedBatch.Upserts.Count == 4 && speedBatch.ChangedCount == 2, "batch speed pairs");
                var source = File.ReadAllText(plane.Source.SourceFile);
                foreach (var variant in new[] { ("AltitudeGRU = ~/Limit", UnitFieldAvailability.UnsupportedValue), ("AltitudeGRU = 700 AltitudeGRU = 800", UnitFieldAvailability.Ambiguous) })
                {
                    File.WriteAllText(plane.Source.SourceFile, source.Replace("AltitudeGRU = 700", variant.Item1), new UTF8Encoding(false));
                    Assert((await LoadTankAsync(root)).Tank.Field("flight.altitude")!.Availability == variant.Item2, "ambiguous/expression field blocked");
                }
                File.WriteAllText(plane.Source.SourceFile, source.Replace("AltitudeGRU = 700", "AltitudeGRU = 700"), new UTF8Encoding(false));
                var state = new UnitCreationState(plane.Name, "Descriptor_Unit_Aviation_New", Guid.NewGuid().ToString(), "AVIATION01", 900, "Aviation clone", [], false, [], [], []);
                state.Fields[AviationMovement.Speed] = "250";
                var projected = UnitCreation.Project(plane, state, UnitCreation.Source(plane));
                Assert(projected.Field(AviationMovement.PlaneSpeed)!.DisplayValue == "250", "new unit speed uses same pairing");
                if (direct)
                {
                    var preview = await new UnitTransactionService().PrepareApplyAsync(root, [AviationOp(plane, "flight.altitude", "900")]);
                    Assert(Encoding.UTF8.GetString(preview.Files.Single(f => f.RelativePath.EndsWith("UniteDescriptor.ndf")).CandidateBytes).Contains("AltitudeGRU = 900.0"), "direct module candidate and ceiling");
                }
            }
            finally { DeleteTemporaryFixture(root); }
        }
    }
    private static async Task AviationTransactions()
    {
        foreach (var newline in new[] { "\n", "\r\n" })
        {
            var root = AviationFixture(newline); var csv = Path.Combine(root, "GameData/Localisation/UnexpectedP2Dictionary/UNITS.csv");
            try
            {
                var (data, plane) = await LoadTankAsync(root); var original = File.ReadAllBytes(plane.Source.SourceFile);
                using var store = new DraftStore(root); await store.LoadAsync();
                var speed = AviationMovement.SpeedDrafts(plane, "300");
                await store.ApplyBatchAsync(speed.Upserts.Concat([AviationOp(plane, "flight.altitude", "800"), AviationOp(plane, "flight.minimumAltitude", "750")]).ToArray(), []);
                using (var reopened = new DraftStore(root))
                { var loaded = await reopened.LoadAsync(); Assert(!loaded.IsBlocked && loaded.Document.SchemaVersion == 5 && reopened.Operations.Count == 4, "schema5 reopen"); }
                Assert(File.ReadAllBytes(plane.Source.SourceFile).SequenceEqual(original), "drafts leave source unchanged");
                var service = new UnitTransactionService();
                var preview = await service.PrepareApplyAsync(root, [store.Operations.First(o => o.FieldKey == "flight.altitude")]);
                Assert(preview.Operations.Count == 4, "partial apply expands related height/speed drafts");
                var result = await service.CommitApplyAsync(preview, store);
                var expected = Encoding.UTF8.GetString(original).Replace("AltitudeGRU = 700", "AltitudeGRU = 800.0").Replace("AltitudeMinGRU = 100", "AltitudeMinGRU = 750.0").Replace("MaxSpeedInKmph = 65", "MaxSpeedInKmph = 300.0").Replace("SpeedInKmph = 65", "SpeedInKmph = 300.0");
                Assert(result.Succeeded && File.ReadAllBytes(plane.Source.SourceFile).SequenceEqual(Encoding.UTF8.GetBytes(expected)), "exact source preservation and grouped apply");
                await service.CommitRestoreAsync(service.PrepareRestore(root, result.BackupId));
                Assert(File.ReadAllBytes(plane.Source.SourceFile).SequenceEqual(original), "restore original aviation bytes");
                await store.UpsertAsync(AviationOp(plane, "flight.minimumAltitude", "750"));
                await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.PrepareApplyAsync(root, store.Operations), "minimum above normal rejected");
                await store.ApplyBatchAsync([AviationOp(plane, "flight.altitude", "2100")], store.Operations.Select(o => o.Id).ToArray());
                await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.PrepareApplyAsync(root, store.Operations), "ceiling respected");
                await store.UpsertAsync(AviationOp(plane, "flight.altitude", "800"));
                preview = await service.PrepareApplyAsync(root, store.Operations);
                var constants = Path.Combine(root, "GameData/Gameplay/Unit/AirplaneConstantes.ndf");
                File.WriteAllText(constants, "MaxAltitudeGRU is 750\n");
                await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.CommitApplyAsync(preview, store), "changed ceiling blocks stale preview");
                File.WriteAllText(constants, "MaxAltitudeGRU is 2000\n");
                await store.UpsertAsync(CreateNameDraft(plane, "航空失败恢复", plane.NameToken!));
                preview = await service.PrepareApplyAsync(root, store.Operations);
                File.SetAttributes(csv, File.GetAttributes(csv) | FileAttributes.ReadOnly);
                await TestAssert.ThrowsAsync<IOException>(() => service.CommitApplyAsync(preview, store), "multi-file failure");
                Assert(File.ReadAllBytes(plane.Source.SourceFile).SequenceEqual(original) && store.Operations.Count > 0, "failure rolls back and preserves drafts");
            }
            finally { if (File.Exists(csv)) File.SetAttributes(csv, FileAttributes.Normal); DeleteTemporaryFixture(root); }
        }
    }
    private static Task AviationChangeMerge()
    {
        foreach (var type in new[] { "AirplaneMovementDescriptor", "TAirplaneMovementModuleDescriptor", "THelicopterMovementModuleDescriptor" })
        {
            using var f = new ChangeFixture();
            var field = type == "THelicopterMovementModuleDescriptor" ? "FlyingAltitudeGRU" : "AltitudeGRU";
            var companion = type == "THelicopterMovementModuleDescriptor" ? "NearGroundFlyingAltitudeGRU = 10" : "AltitudeMinGRU = 10 AltitudeMaxGRU = 2000";
            string Source(int altitude, int other) => $"export Test is TEntityDescriptor(ModulesDescriptors = [{type}({field} = {altitude} {companion} Other = {other})])\n";
            ChangeFixture.Write(f.Target, "GameData/Gameplay/Unit/Tactic/ModulesStandard.ndf", AviationTemplate);
            ChangeFixture.Write(f.Target, "GameData/Gameplay/Unit/AirplaneConstantes.ndf", "MaxAltitudeGRU is 2000\n");
            ChangeFixture.Write(f.Basis, ChangeNdf, Source(100, 1)); ChangeFixture.Write(f.Source, ChangeNdf, Source(120, 1)); ChangeFixture.Write(f.Target, ChangeNdf, Source(200, 2));
            var preview = ChangeRestore.Prepare(f.Capture(NumericPolicy.Ratio), f.Target);
            Assert(preview.CanApply && Encoding.UTF8.GetString(preview.Files.Single().After!).Contains(field + " = 240"), "new aviation quantities participate in cross-version ratio restore");
            ChangeFixture.Write(f.Target, ChangeNdf, Source(1900, 2));
            if (type != "THelicopterMovementModuleDescriptor") Assert(!ChangeRestore.Prepare(f.Capture(NumericPolicy.Ratio), f.Target).CanApply, "cross-version restore rejects ceiling overflow");
        }
        return Task.CompletedTask;
    }
    private static async Task AviationOldReader(string path)
    {
        var root = AviationFixture(); var context = new System.Runtime.Loader.AssemblyLoadContext("aviation-old-reader", true);
        try
        {
            var (_, plane) = await LoadTankAsync(root); using var store = new DraftStore(root); await store.LoadAsync();
            await store.ApplyBatchAsync(AviationMovement.SpeedDrafts(plane, "300").Upserts, []);
            var original = File.ReadAllBytes(store.DraftPath);
            var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(path)); var type = assembly.GetType("WarnoLiteModdingTool.Core.Drafts.DraftStore")!;
            using var old = (IDisposable)Activator.CreateInstance(type, root)!;
            var task = (Task)type.GetMethod("LoadAsync")!.Invoke(old, [CancellationToken.None])!; await task;
            var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
            Assert((bool)result.GetType().GetProperty("IsBlocked")!.GetValue(result)! && File.ReadAllBytes(store.DraftPath).SequenceEqual(original), "old version blocks schema5 and preserves its bytes");
            Console.WriteLine("PASS 2.10-preview.6 rejects aviation schema 5 without modifying it");
        }
        finally { context.Unload(); DeleteTemporaryFixture(root); }
    }
}
