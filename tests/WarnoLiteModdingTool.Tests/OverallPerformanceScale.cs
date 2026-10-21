using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.Tests;

internal static partial class Program
{
    // Explicit, reproducible synthetic sample. Never overwrites an existing directory.
    private static void OverallPerformanceSample(string root, int count)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root) || count is < 100 or > 20000) throw new ArgumentException("Choose a new synthetic directory and 100–20000 units.");
        CopyDirectory(Fixture("p5-division"), root);
        const string gfx = "GameData/Generated/Gameplay/Gfx/";
        var p2 = Fixture("p2-unit-complete");
        CopyDirectory(Path.Combine(p2, "GameData/Localisation"), Path.Combine(root, "GameData/Localisation"));
        foreach (var name in new[] { "UniteDescriptor.ndf", "WeaponDescriptor.ndf", "Ammunition.ndf", "AmmunitionMissiles.ndf" })
            File.AppendAllText(Path.Combine(root, gfx, name), "\n" + File.ReadAllText(Path.Combine(p2, gfx, name)), new UTF8Encoding(false));
        var source = File.ReadAllText(Path.Combine(p2, gfx, "UniteDescriptor.ndf"));
        var templates = Scanner.Scan(source, Path.Combine(p2, gfx, "UniteDescriptor.ndf"), "units", p2).Objects;
        var units = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            var template = templates[i % templates.Count];
            var body = source.Substring(template.CharacterOffset, template.CharacterLength);
            body = body.Replace(template.Name, $"Descriptor_Unit_Perf_{i:D5}");
            body = System.Text.RegularExpressions.Regex.Replace(body, @"GUID:\{[^}]+\}", $"GUID:{{90000000-0000-0000-0000-{i:D12}}}");
            body = System.Text.RegularExpressions.Regex.Replace(body, @"NameToken = '[^']+'", $"NameToken = 'PF{i:D8}'");
            body = System.Text.RegularExpressions.Regex.Replace(body, @"\$/GFX/Weapon/WeaponDescriptor_\w+", $"$/GFX/Weapon/WeaponDescriptor_Perf_{i / 2:D5}");
            body = body.Replace("MaxPhysicalDamages = 10", $"MaxPhysicalDamages = {10 + i % 19}")
                .Replace("MaxSpeedInKmph = 65", $"MaxSpeedInKmph = {40 + i % 61}")
                .Replace("MotherCountry = 'US'", $"MotherCountry = '{(i % 3 == 0 ? "FR" : "US")}'");
            if (i % 7 == 0) body = body.Replace("TVisualShootPositionsModuleDescriptor()", "TAirplaneMovementModuleDescriptor(SpeedInKmph = 500 AltitudeGRU = 1000)");
            units.AppendLine(body);
        }
        File.AppendAllText(Path.Combine(root, gfx, "UniteDescriptor.ndf"), units.ToString(), new UTF8Encoding(false));
        var ammoCount = Math.Max(1000, count / 3);
        var ammo = new StringBuilder();
        for (var i = 0; i < ammoCount; i++)
            ammo.AppendLine($"export Ammo_Perf_{i:D5} is TAmmunitionDescriptor(DescriptorId = GUID:{{91000000-0000-0000-0000-{i:D12}}} PhysicalDamages = {2 + i % 31} ShotsCountPerSalvo = {1 + i % 4} SupplyCost = {1 + i % 17} TimeBetweenTwoShots = 1 ProjectileSpeedGRU = {500 + i} HasDeploymentTime = False)");
        File.AppendAllText(Path.Combine(root, gfx, "Ammunition.ndf"), ammo.ToString(), new UTF8Encoding(false));
        var weapons = new StringBuilder();
        for (var i = 0; i < (count + 1) / 2; i++)
        {
            var mounts = string.Join(",\n", Enumerable.Range(0, 1 + i % 3).Select(m =>
                $"TMountedWeaponDescriptor(AmmoBoxIndex = {m} Ammunition = $/GFX/Weapon/Ammo_Perf_{(i + m * 17) % ammoCount:D5})"));
            var salves = string.Join(",", Enumerable.Repeat("4", 1 + i % 3));
            weapons.AppendLine($"export WeaponDescriptor_Perf_{i:D5} is TWeaponManagerModuleDescriptor(Salves = [{salves}] SalvoIsMainSalvo = [{string.Join(",", Enumerable.Repeat("True", 1 + i % 3))}] TurretDescriptorList = [TTurretUnitDescriptor(MountedWeaponDescriptorList = [{mounts}])])");
        }
        File.AppendAllText(Path.Combine(root, gfx, "WeaponDescriptor.ndf"), weapons.ToString(), new UTF8Encoding(false));
        WriteExperience198(root);
        WriteStrategicFixture(root, "\n");
        var existing = Directory.EnumerateFiles(root, "*.ndf", SearchOption.AllDirectories).Count();
        var extra = Math.Max(4500, count * 3 / 2) - existing;
        for (var i = 0; i < extra; i++)
            Write198(root, $"GameData/Perf/{i % 31:D2}/Dependency{i:D5}.ndf",
                $"export PerfDependency{i:D5} is TObject(Value = {i} Unit = $/GFX/Unit/Descriptor_Unit_Perf_{i % count:D5} Weapon = $/GFX/Weapon/WeaponDescriptor_Perf_{i % ((count + 1) / 2):D5})");
        File.WriteAllText(Path.Combine(root, ".performance-synthetic.json"), JsonSerializer.Serialize(new { count, ammoCount, ndf = existing + extra }), new UTF8Encoding(false));
        Console.WriteLine(root);
    }

    private static async Task OverallPerformanceTransactions(string root, string output)
    {
        if (!File.Exists(Path.Combine(root, ".performance-synthetic.json"))) throw new ArgumentException("Only the generated synthetic sample may be written.");
        Directory.CreateDirectory(output);
        using var memory = new PerformanceMemory();
        var results = Path.Combine(output, "transactions.jsonl");
        void Report(string stage, Stopwatch timer, AnalysisMetrics metrics, object? details = null)
        {
            var process = Process.GetCurrentProcess();
            var row = JsonSerializer.Serialize(new { stage, ms = timer.ElapsedMilliseconds, metrics.Graphs, metrics.GraphFiles,
                metrics.SyntaxDocuments, metrics.DraftPasses, privateMiB = process.PrivateMemorySize64 / 1048576d,
                workingMiB = process.WorkingSet64 / 1048576d, sampledPeakPrivateMiB = memory.PeakMiB, details });
            File.AppendAllText(results, row + "\n"); Console.WriteLine(row);
        }
        var snapshot = await ProjectWorkspaceSnapshot.LoadRootAsync(root, ProjectLoadCache.CreateSession(root));
        var units = snapshot.Units.Units.Where(u => u.Name.StartsWith("Descriptor_Unit_Perf_") && u.Field("survival.health")?.CanEdit == true).Take(1000).ToArray();
        var drafts = units.Select(u => CreateFieldDraft(u, u.Field("survival.health")!, "45", "45")).ToArray();
        using var store = new DraftStore(root); await store.LoadAsync();
        await store.ApplyBatchAsync([], store.Operations.Select(o => o.Id).ToArray());
        var analysis = new DraftAnalysis(snapshot.Units, snapshot.Weapons, snapshot.Divisions, snapshot.Strategic);
        store.PrepareChangeAsync = analysis.PrepareAsync;
        foreach (var count in new[] { 100, 1000 })
        {
            using var metrics = new AnalysisMetrics(); var timer = Stopwatch.StartNew();
            await store.ApplyBatchAsync(drafts.Take(count).ToArray(), []);
            var resolved = analysis.Get(store.Operations);
            Assert(resolved.Count == count && resolved.All(r => r.Status == DraftResolutionStatus.Active), "规模草稿分析完整");
            for (var i = 0; i < 30; i++) Assert(ReferenceEquals(resolved, analysis.Get(store.Operations)), "重复选择共享同一分析");
            Report("drafts-" + count, timer, metrics);
        }
        await store.ApplyBatchAsync([], store.Operations.Select(o => o.Id).ToArray());
        var service = new UnitTransactionService();
        for (var run = 0; run < 3; run++)
        {
            foreach (var count in new[] { 1, 100 })
            {
                using var metrics = new AnalysisMetrics(); var timer = Stopwatch.StartNew();
                var preview = await service.PrepareApplyAsync(root, drafts.Take(count).ToArray());
                Assert(preview.Files.Any(f => f.Kind == FormalTextFileKind.Ndf), "普通单位候选完整");
                Report($"preview-units-{count}-{run}", timer, metrics);
            }
            using (var metrics = new AnalysisMetrics())
            {
                var timer = Stopwatch.StartNew();
                var a = snapshot.Weapons!.Ammunition.First(a => a.Name.StartsWith("Ammo_Perf_"));
                var op = CreateWeaponDraft(a.Field("ammo.damage.physical")!, "52", DraftEditScope.AllReferences, [], null);
                _ = await service.PrepareApplyAsync(root, [op]);
                Report("preview-shared-ammo-" + run, timer, metrics);
            }
            using (var metrics = new AnalysisMetrics())
            {
                var timer = Stopwatch.StartNew();
                _ = await service.PrepareApplyAsync(root, [Edit198(snapshot.Units.Rules!.Experience)]);
                Report("preview-experience-" + run, timer, metrics);
            }
            using (var metrics = new AnalysisMetrics())
            {
                var timer = Stopwatch.StartNew();
                using var reads = new ProjectReadScope().Enter();
                // The first synthetic unit is an airplane. Adding a new box there
                // also requires an explicit SalvoIsMainSalvo edit, so exercise the
                // ordinary local structure transaction on its ground-unit sibling.
                var link = snapshot.Weapons!.Units.First(u => u.Name == units[1].Name);
                var weapon = snapshot.Weapons.Weapon(link.Weapons.First())!;
                var state = WeaponStructure.New(link, weapon);
                WeaponStructure.Add(state, state.Weapon, "t:0/m:0", link.Name, "t:0", "t:0/m:0",
                    "$/GFX/Weapon/Ammo_Perf_00000", 7);
                var operations = drafts.Take(100).Append(WeaponStructure.Operation(state)).ToArray();
                var preview = await service.PrepareApplyAsync(root, operations);
                Assert(preview.FormalFileCount >= 2 && preview.Operations.Count == 101, "结构与100项普通字段组合完整");
                Report("preview-structure-and-100-fields-" + run, timer, metrics, new { preview.FormalFileCount });
            }
            using (var metrics = new AnalysisMetrics())
            {
                var timer = Stopwatch.StartNew();
                using var reads = new ProjectReadScope().Enter();
                var graph = UnitProjectGraph.Read(root, deletionScope: true);
                var unit = units[^1];
                var impact = UnitDeletionV2.Analyze(graph, graph.RequireObject(unit.Source.RelativeSourceFile, unit.Name));
                Assert(impact.Uses.All(u => u.Automatic || u.CanReplace), "合成删除引用均可明确处理");
                var choices = impact.Uses.Where(u => !u.Automatic).ToDictionary(u => u.Key,
                    u => graph.ReferenceTo(u.File, graph.RequireObject(units[0].Source.RelativeSourceFile, units[0].Name)));
                var operation = UnitDeletionV2.Operation(unit, graph, choices: choices);
                var preview = await service.PrepareApplyAsync(root, [operation]);
                Assert(preview.Files.Any(f => f.Kind == FormalTextFileKind.Ndf), "删除与全目录引用复核完整");
                Report("preview-deletion-" + run, timer, metrics, new { uses = impact.Uses.Count, preview.FormalFileCount });
            }
        }
        await store.UpsertAsync(drafts[0]);
        var final = await service.PrepareApplyAsync(root, store.Operations);
        await service.CommitApplyAsync(final, store);
        using (var metrics = new AnalysisMetrics())
        {
            var timer = Stopwatch.StartNew();
            var next = await ProjectWorkspaceSnapshot.LoadRootAsync(root, snapshot.Cache.Next(), previous: snapshot, committed: final.Files);
            Assert(next.Units.Units.Single(u => u.Name == drafts[0].ObjectName).Field("survival.health")!.RawValue == "45", "提交后刷新显示正式新值");
            Report("refresh-after-apply", timer, metrics, next.Timings);
        }
    }

    private static async Task OverallPerformanceDrafts(string root, int count)
    {
        if (!File.Exists(Path.Combine(root, ".performance-synthetic.json")) || count is < 1 or > 1000)
            throw new ArgumentException("Only the generated synthetic sample and 1–1000 drafts are supported.");
        var snapshot = await ProjectWorkspaceSnapshot.LoadRootAsync(root, ProjectLoadCache.CreateSession(root));
        var drafts = snapshot.Units.Units.Where(u => u.Name.StartsWith("Descriptor_Unit_Perf_") && u.Field("survival.health")?.CanEdit == true)
            .Take(count).Select(u => CreateFieldDraft(u, u.Field("survival.health")!, "46", "46")).ToArray();
        Assert(drafts.Length == count, "合成样本有足够的草稿目标");
        using var store = new DraftStore(root); await store.LoadAsync();
        await store.ApplyBatchAsync(drafts, store.Operations.Select(o => o.Id).ToArray());
        Console.WriteLine($"Saved {count} synthetic drafts; formal files unchanged.");
    }
}
