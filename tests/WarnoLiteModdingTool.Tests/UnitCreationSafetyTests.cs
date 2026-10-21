using System.IO;
using System.Text;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.Tests;

internal static partial class Program
{
    private static Task UnitCreationSafetyContracts()
    {
        var root = Path.Combine(Path.GetTempPath(), "wl-creation-safety-" + Guid.NewGuid().ToString("N"));
        const string file = "GameData/Generated/Gameplay/Gfx/UniteDescriptor.ndf";
        var full = Path.Combine(root, file); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        const string mother = "Descriptor_Unit_Mother";
        const string definitions = "Flags is TFlagSetModuleDescriptor()\nLink is TLinkTeamModuleDescriptor()\n";
        var definitionPath = Path.Combine(root, "GameData/Modules.ndf");
        File.WriteAllText(definitionPath, definitions, new UTF8Encoding(false));
        string Unit(string name, string refs) => name + " is TEntityDescriptor(ModulesDescriptors=[" + refs + "])\n";
        var baseline = Unit(mother, "Flags,~/Link");
        var newName = mother + "_mod_001";
        List<string> Check(string before, string candidate, string? reject = null, bool provenance = true,
            string? changedDefinitions = null, IReadOnlyList<UnitCreationOrigin>? origins = null)
        {
            File.WriteAllText(full, before, new UTF8Encoding(false));
            var bytes = File.ReadAllBytes(full);
            var changes = new List<PlannedFileChange> { new(file, full, FormalTextFileKind.Ndf, PlannedFileAction.Write, true, bytes, Encoding.UTF8.GetBytes(candidate), null, []) };
            if (changedDefinitions is not null) changes.Add(new("GameData/Modules.ndf", definitionPath, FormalTextFileKind.Ndf, PlannedFileAction.Write, true, File.ReadAllBytes(definitionPath), Encoding.UTF8.GetBytes(changedDefinitions), null, []));
            var messages = new List<string>();
            using var metrics = new AnalysisMetrics();
            try
            {
                WeaponSafety.ValidateFinal(root, changes, messages, generatedClones: provenance,
                    creationOrigins: provenance ? origins ?? [new(file, newName, mother, "TEntityDescriptor")] : null);
                Assert(reject is null, "expected creation safety refusal: " + reject);
            }
            catch (TransactionValidationException ex)
            {
                Assert(reject is not null && ex.Code == "weapon.safety" && ex.Message.Contains(reject), "creation safety refusal: " + ex.Message);
                if (reject == "module.ref.") Assert(ex.Message.StartsWith("单位模块引用或结构无法确认"), "module-specific error heading");
            }
            Assert(metrics.Graphs == 2 && metrics.GraphFiles == (changedDefinitions is null ? 3 : 4),
                $"creation inheritance reuses unchanged sources: {metrics.Graphs} graphs, {metrics.GraphFiles} parsed graph files");
            Assert(bytes.SequenceEqual(File.ReadAllBytes(full)), "validation never writes candidate");
            return messages;
        }
        try
        {
            Assert(Check(baseline, baseline + Unit(newName, "Flags,~/Link")).Any(m => m.Contains("继承母版") && m.Contains("Flags") && m.Contains("~/Link")), "inherited unknowns remain visible");
            Check(baseline, baseline + Unit(newName, "~/Link,TCustomModule(Value=2),Flags"));
            Check(baseline, baseline + Unit(newName, "Flags,~/Link,Missing"), "module.ref.");
            Check(baseline, baseline + Unit(newName, "Flags,~/Other"), "module.ref.");
            Check(baseline, baseline + Unit(newName, "Flags,~/Link,Flags"), "module.ref.");
            Check(baseline, baseline + Unit(newName, "Flags,~/Link"), "module.ref.", provenance: false);
            Check(baseline, baseline + Unit(newName, "Flags,~/Link"), "module.ref.", changedDefinitions: "");
            Check(baseline, baseline + Unit(newName, "Flags,~/Link"), "module.ref.", changedDefinitions: definitions.Replace("TFlagSetModuleDescriptor()", "TFlagSetModuleDescriptor(Value=1)"));
            Check(definitions + baseline, baseline + Unit(newName, "Flags,~/Link"), "module.ref."); // formerly local resolution lost
            Check(baseline, Unit(mother, "Flags") + Unit(newName, "Flags,~/Link"), "module.ref."); // final mother changed
            Check(baseline, baseline + newName + " is TEntityDescriptor(ModulesDescriptors=Unsupported)\n", "module.coverage");
            Check(baseline, baseline + Unit(newName, "Flags,~/Link"), "module.ref.", origins: [new("GameData/Other.ndf", newName, mother, "TEntityDescriptor")]);
            var absent = Unit(mother, "Missing");
            Check(absent, absent + Unit(newName, "Missing")); // inherited is unverified, not asserted valid
            newName = "Descriptor_Unit_Custom";
            Check(baseline, baseline + Unit(newName, "Flags,~/Link"));
            newName = mother + "_WLMT_forged";
            Check(baseline, baseline + Unit(newName, "Flags,~/Link,NewBad"), "module.ref."); // explicit provenance cannot broaden legacy suffix inheritance
            Check(baseline, baseline + Unit(newName, "Flags,~/Link"), "module.ref.", provenance: false);
            newName = mother + "_mod_001";
            const string ammo = "A is TAmmunitionDescriptor(ShotsCountPerSalvo=2 HasDeploymentTime=True)\n";
            const string weapon = "W is TWeaponManagerModuleDescriptor(Salves=[1] SalvoIsMainSalvo=[True] TurretDescriptorList=[TTurretUnitDescriptor(MountedWeaponDescriptorList=[TMountedWeaponDescriptor(AmmoBoxIndex=0 Ammunition=A)])])\n";
            var deployed = ammo + weapon + Unit(mother, "W,Flags");
            Check(deployed, deployed + Unit(newName, "W,Flags"));
            Check(deployed.Replace("HasDeploymentTime=True", "HasDeploymentTime=False"), deployed + Unit(newName, "W,Flags"), "deployment.");
            var valid = ammo + weapon + Unit(mother, "W,Flags,TWeaponDeploymentModuleDescriptor()");
            Check(valid, valid + Unit(newName, "W,Flags"), "deployment.");
            Check(deployed, deployed + weapon.Replace("W is", "W_Copy is") + Unit(newName, "W_Copy,Flags"), origins:
                [new(file, newName, mother, "TEntityDescriptor"), new(file, "W_Copy", "W", "TWeaponManagerModuleDescriptor")]);
            var air = ammo + weapon + Unit(mother, "W,Flags,TAirplaneMovementModuleDescriptor(),TWeaponDeploymentModuleDescriptor()");
            Check(air, air.Replace("SalvoIsMainSalvo=[True]", "SalvoIsMainSalvo=[]") + Unit(newName, "W,Flags,TAirplaneMovementModuleDescriptor(),TWeaponDeploymentModuleDescriptor()"), "airplane.");
            Check(deployed, deployed.Replace("ShotsCountPerSalvo=2", "ShotsCountPerSalvo=2 SimultaneousShotsCount=3") + Unit(newName, "W,Flags"), "simultaneous");
        }
        finally { DeleteTemporaryFixture(root); }
        return Task.CompletedTask;
    }

    private static string CreationSafetyFixture()
    {
        var root = Fixture199();
        var path = Path.Combine(root, Unit199Path);
        File.WriteAllText(path, File.ReadAllText(path).Replace("ModulesDescriptors = [", "ModulesDescriptors = [\n        TankFlagsModuleDescriptor, ~/LinkTeamModuleDescriptor,"), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "GameData/Modules.ndf"), "TankFlagsModuleDescriptor is TFlagSetModuleDescriptor()\nLinkTeamModuleDescriptor is TLinkTeamModuleDescriptor()\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "GameData/OtherModules.ndf"), "LinkTeamModuleDescriptor is TLinkTeamModuleDescriptor()\n", new UTF8Encoding(false));
        return root;
    }

    private static async Task UnitCreationSafetyTransactions()
    {
        var root = CreationSafetyFixture();
        try
        {
            var (_, units, weapons) = await LoadP4Async(root);
            var mother = units.Units.Single(u => u.Name == "Descriptor_Unit_Test_Tank_US");
            var state = UnitCreation.New(mother, units, []) with { IndependentWeapons = true };
            foreach (var name in mother.Weapons)
            {
                var weapon = weapons.Weapon(name)!;
                state.WeaponBaselines[name] = File.ReadAllText(weapon.Source.SourceFile).Substring(weapon.Source.CharacterOffset, weapon.Source.CharacterLength);
            }
            var operation = UnitCreation.Operation(mother, state);
            using (var initial = new DraftStore(root)) { await initial.LoadAsync(); await initial.UpsertAsync(operation); }
            using var store = new DraftStore(root); await store.LoadAsync();
            var other = UnitCreation.Operation(mother, UnitCreation.New(mother, units, store.Operations) with { Id = "Descriptor_Unit_Custom_Safety" });
            await store.UpsertAsync(other);
            var service = new UnitTransactionService();
            var preview = await service.PrepareApplyAsync(root, [store.Operations.Single(o => o.Id == operation.Id)]);
            Assert(preview.ValidationMessages.Any(m => m.Contains("继承母版")), "real planner propagates creation provenance after reopening draft");
            Assert(preview.Operations.Count == 1, "partial apply does not absorb unrelated creation");
            var path = Path.Combine(root, Unit199Path); var original = File.ReadAllBytes(path);
            // Existing transaction failure recovery must also cover the newly accepted creation.
            var csv = preview.Files.Single(f => f.Kind == FormalTextFileKind.Csv).FullPath;
            File.SetAttributes(csv, FileAttributes.ReadOnly);
            try { await TestAssert.ThrowsAsync<IOException>(() => service.CommitApplyAsync(preview, store), "creation failure rolls back"); }
            finally { File.SetAttributes(csv, FileAttributes.Normal); }
            Assert(original.SequenceEqual(File.ReadAllBytes(path)) && store.Operations.Count == 2, "failure preserves formal file and both drafts");
            preview = await service.PrepareApplyAsync(root, [operation]);
            await service.CommitApplyAsync(preview, store);
            Assert(store.Operations.Count == 1 && store.Operations[0].Id == other.Id, "only selected creation removed");
            var (_, after, _) = await LoadP4Async(root);
            Assert(after.Units.Any(u => u.Name == state.Id) && UnitCreation.Source(after.Units.Single(u => u.Name == mother.Name)) == UnitCreation.Source(mother), "new unit exists and mother unchanged");
            Assert(File.ReadAllText(Path.Combine(root, UnitCreation.SerializerPath)).Contains("$/GFX/Unit/" + state.Id), "created registration present");
            preview = await service.PrepareApplyAsync(root, store.Operations);
            Assert(preview.ValidationMessages.Any(m => m.Contains("Descriptor_Unit_Custom_Safety")), "custom-named old draft retains provenance");
            File.AppendAllText(path, "\n// external edit", new UTF8Encoding(false));
            await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.CommitApplyAsync(preview, store), "final freshness check retained");
        }
        finally { DeleteTemporaryFixture(root); }
    }
}
