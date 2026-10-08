using System.IO;
using System.Text;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Weapons;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.SourceWorkspace;

namespace WarnoLiteModdingTool.Tests;

internal static partial class Program
{
    private const string SafetyFixture = """
        export A is TAmmunitionDescriptor(ShotsCountPerSalvo=2 SimultaneousShotsCount=1 SupplyCost=2 TimeBetweenTwoShots=0.1 CanShootOnPosition=False HasDeploymentTime=False PhysicalDamages=4)
        export B is TAmmunitionDescriptor(ShotsCountPerSalvo=2 SimultaneousShotsCount=1 SupplyCost=2 TimeBetweenTwoShots=0.1 CanShootOnPosition=False HasDeploymentTime=False)
        export W is TWeaponManagerModuleDescriptor(Salves=[3] SalvoIsMainSalvo=[True] TurretDescriptorList=[TTurretUnitDescriptor(MountedWeaponDescriptorList=[TMountedWeaponDescriptor(AmmoBoxIndex=0 Ammunition=A),TMountedWeaponDescriptor(AmmoBoxIndex=0 Ammunition=B)])])
        U is TEntityDescriptor(ModulesDescriptors=[W,TAirplaneMovementModuleDescriptor()])
        """;

    private static Task WeaponSafetyContracts()
    {
        var root = Path.Combine(Path.GetTempPath(), "wl-safety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "GameData"));
        const string path = "GameData/test.ndf"; var full = Path.Combine(root, path);
        void Baseline(string text) => File.WriteAllText(full, text, new UTF8Encoding(false));
        List<string> Check(string text, bool business = false)
        {
            var messages = new List<string>();
            WeaponSafety.ValidateFinal(root, [new(path, full, FormalTextFileKind.Ndf, PlannedFileAction.Write, true, File.ReadAllBytes(full), Encoding.UTF8.GetBytes(text), null, [])], messages, business);
            return messages;
        }
        void Reject(string text, string code)
        {
            var bytes = File.ReadAllBytes(full);
            try { Check(text); throw new Exception("Expected safety refusal: " + code); }
            catch (TransactionValidationException ex) { Assert(ex.Code == "weapon.safety" && ex.Message.Contains(code), "specific final rule: " + code); }
            Assert(File.ReadAllBytes(full).SequenceEqual(bytes), "refused candidate did not write source");
        }
        try
        {
            Baseline(SafetyFixture); Check(SafetyFixture.Replace("export A is TAmmunitionDescriptor(ShotsCountPerSalvo=2", "export A is TAmmunitionDescriptor(ShotsCountPerSalvo=02")); Check(SafetyFixture.Replace("PhysicalDamages=4", "PhysicalDamages=5"));
            Reject(SafetyFixture.Replace("PhysicalDamages=4", "PhysicalDamages=-1"), "PhysicalDamages");
            Reject(SafetyFixture.Replace("SimultaneousShotsCount=1", "SimultaneousShotsCount=3"), "simultaneous");
            Check(SafetyFixture.Replace("ShotsCountPerSalvo=2", "ShotsCountPerSalvo=3").Replace("SimultaneousShotsCount=1", "SimultaneousShotsCount=3"));
            Reject(SafetyFixture.Replace("export A is TAmmunitionDescriptor(ShotsCountPerSalvo=2", "export A is TAmmunitionDescriptor(ShotsCountPerSalvo=3"), "AmmoBoxIndex");
            Reject(SafetyFixture.Replace("SalvoIsMainSalvo=[True]", "SalvoIsMainSalvo=[]"), "airplane");
            Check(SafetyFixture.Replace("SalvoIsMainSalvo=[True]", "SalvoIsMainSalvo=[]").Replace(",TAirplaneMovementModuleDescriptor()", ""));
            Reject(SafetyFixture.Replace("HasDeploymentTime=False", "HasDeploymentTime=True"), "deployment");
            Check(SafetyFixture.Replace("HasDeploymentTime=False", "HasDeploymentTime=True").Replace("ModulesDescriptors=[W,", "ModulesDescriptors=[TWeaponDeploymentModuleDescriptor(),W,"));
            Reject(SafetyFixture.Replace("CanShootOnPosition=False", "CanShootOnPosition=False NbSalvosShootOnPosition=2"), "position");
            Check(SafetyFixture.Replace("CanShootOnPosition=False", "CanShootOnPosition=True NbSalvosShootOnPosition=2"));
            var warnings = Check(SafetyFixture.Replace("TimeBetweenTwoShots=0.1", "TimeBetweenTwoShots=0.163").Replace("SupplyCost=2", "SupplyCost=0").Replace("CanShootOnPosition=False", "CanShootOnPosition=True"));
            Assert(warnings.Any(m => m.Contains("time")) && warnings.Any(m => m.Contains("supply")) && warnings.Any(m => m.Contains("position")), "unverified limits remain visible warnings");
            var oldInvalid = SafetyFixture.Replace("SimultaneousShotsCount=1", "SimultaneousShotsCount=4"); Baseline(oldInvalid);
            Check(oldInvalid.Replace("PhysicalDamages=4", "PhysicalDamages=5"));
            Reject(oldInvalid.Replace("SimultaneousShotsCount=4", "SimultaneousShotsCount=3"), "simultaneous");
            // Cloning for an unrelated edit preserves historical rule states, but changed related values still fail.
            var clone = "\nexport A_WLMT_1234567890 is TAmmunitionDescriptor(ShotsCountPerSalvo=2 SimultaneousShotsCount=4 SupplyCost=2 TimeBetweenTwoShots=0.1 CanShootOnPosition=False HasDeploymentTime=False PhysicalDamages=5)";
            Check(oldInvalid + clone, true);
            Reject(oldInvalid + clone, "simultaneous"); // raw source cannot claim a generated origin
            Baseline(SafetyFixture);
            var fields = new Dictionary<string,string>{{"TimeBetweenTwoShots","0.30000000000000004"},{"ShotsCountPerSalvo","0"},{"SupplyCost","0"}};
            var issues = WeaponSafety.InspectAmmo(path,"A",fields);
            Assert(!issues.Any(i=>i.Rule=="time") && issues.Any(i=>i.Rule=="supply"), "floating-point tolerance and no zero division");
        }
        finally { Directory.Delete(root, true); }
        return Task.CompletedTask;
    }

    private static async Task WeaponSafetyTransactions()
    {
        var root = CreateTemporaryFixtureCopy("p4-shared");
        const string path = "GameData/Generated/Gameplay/Gfx/Ammunition.ndf";
        var full = Path.Combine(root, path);
        try
        {
            File.WriteAllText(full, File.ReadAllText(full).Replace("ShotsCountPerSalvo = 1", "ShotsCountPerSalvo = 2"), new UTF8Encoding(false));
            var baseline = File.ReadAllBytes(full);
            var (_, _, data) = await LoadP4Async(root);
            using var store = new DraftStore(root); await store.LoadAsync();
            var a = CreateWeaponDraft(data.Ammo("Ammo_P4_Shared")!.Field("ammo.shotsPerSalvo")!, "3", DraftEditScope.AllReferences, [], null);
            var b = CreateWeaponDraft(data.Ammo("Ammo_P4_Alt")!.Field("ammo.shotsPerSalvo")!, "3", DraftEditScope.AllReferences, [], null);
            await store.UpsertAsync(a); await store.UpsertAsync(b);
            var service = new UnitTransactionService();
            try { await service.PrepareApplyAsync(root, [a]); throw new Exception("Partial apply split a shared-box contract"); }
            catch (TransactionValidationException ex) { Assert(ex.Code == "weapon.safety", "partial selection checks actual combination"); }
            Assert(File.ReadAllBytes(full).SequenceEqual(baseline) && store.Operations.Count==2, "partial refusal preserves files and drafts");
            var preview = await service.PrepareApplyAsync(root, [a,b]);
            await service.CommitApplyAsync(preview, store);
            Assert(store.Operations.Count==0 && File.ReadAllText(full).Contains("ShotsCountPerSalvo = 3"), "paired drafts commit together");
            var source = SourceFiles.Open(root,path);
            var bad = source.Text.Replace("ShotsCountPerSalvo = 3", "ShotsCountPerSalvo = -1");
            await store.UpsertAsync(SourceEditing.Operation(path, source with { Text = bad }));
            try { await SourceEditing.Prepare(root, store.Operations, default); throw new Exception("Raw source bypassed scalar safety"); }
            catch (Exception ex) when (ex is TransactionValidationException or InvalidDataException) { }
            Assert(File.ReadAllText(full)==source.Text && store.Operations.Count==1, "source buffer preserved; formal source refused");
        }
        finally { DeleteTemporaryFixture(root); }
    }
}
