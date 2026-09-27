using System.IO;
using System.Text;
using WarnoLiteModdingTool.Core.Divisions;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Tests;

internal static partial class Program
{
    private const string EmblemTexturePath = "GameData/Generated/UserInterface/Textures/DivisionTextures.ndf";
    private const string EmblemBase = "Texture_Division_Emblem_Test";
    private const string EmblemLegacy = "Texture_Division_Emblem_mod_legacy";
    private const string EmblemPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==";
    private const string EmblemLegacyPath = "GameData/Assets/legacy.png";
    private static string EmblemEntry(string key, string? target = null) => $"(\"{key}\", MAP [(~/ComponentState/Normal, ~/{target ?? key})])";
    private static string EmblemBank(string name, params string[] entries) => $"{name} is TBUCKToolAdditionalTextureBank\n(\n    Textures = MAP [\n        " + string.Join(",\n        ", entries) + ", // keep this comment\n    ]\n)\n";
    private static string EmblemFixture(string nl = "\n", bool legacy = false)
    {
        var root = CreateTemporaryFixtureCopy("p5-division");
        var path = Path.Combine(root, EmblemTexturePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var text = $"{EmblemBase} is TUIResourceTexture_Common(FileName = 'GameData:/Assets/base.png')\n" + EmblemBank("CustomBank", EmblemEntry(EmblemBase));
        if (legacy) text += $"{EmblemLegacy} is TUIResourceTexture_Common(FileName = 'GameData:/Assets/legacy.png')\n";
        File.WriteAllText(path, text.Replace("\n", nl), new UTF8Encoding(false));
        var divisions = Path.Combine(root, "GameData/Generated/Gameplay/Decks/Divisions.ndf");
        File.WriteAllText(divisions, File.ReadAllText(divisions).Replace("    TypeToken", $"    EmblemTexture = \"{(legacy ? EmblemLegacy : EmblemBase)}\"\n    TypeToken"), new UTF8Encoding(false));
        if (legacy) { Directory.CreateDirectory(Path.Combine(root, "GameData/Assets")); File.WriteAllBytes(Path.Combine(root, EmblemLegacyPath), Convert.FromBase64String(EmblemPng)); }
        return root;
    }
    private static void AssertEmblemRegistered(string root, List<PlannedFileChange> files, params string[] keys)
    {
        var graph = new UnitProjectGraph(root, files);
        var file = graph.Files[EmblemTexturePath];
        var doc = file.Syntax;
        var maps = doc.FindConstructors("TBUCKToolAdditionalTextureBank").SelectMany(n => doc.FindDirectAssignments(n, "Textures"));
        var entries = maps.SelectMany(doc.ReadMapEntries).ToArray();
        foreach (var key in keys)
        {
            var entry = entries.Single(e => NdfSyntaxDocument.Unquote(doc.Raw(e.Key)) == key);
            var normal = doc.ReadMapEntries(entry.Value).Single(e => doc.Raw(e.Key) == "~/ComponentState/Normal");
            Assert(UnitProjectGraph.Same(graph.Resolve(EmblemTexturePath, doc.Raw(normal.Value)), graph.RequireObject(EmblemTexturePath, key)), "纹理库Normal必须指向对应声明");
        }
    }
    private static async Task Emblem1918Registration()
    {
        foreach (var nl in new[] { "\n", "\r\n" })
        {
            var root = EmblemFixture(nl);
            try
            {
                var original = File.ReadAllBytes(Path.Combine(root, EmblemTexturePath));
                var (_, _, data) = await LoadP5Async(root); var mother = data.Divisions.First();
                var state = DivisionIdentity.New(data, mother, false, []);
                var files = new List<PlannedFileChange>();
                var keys = new[] { "Texture_Division_Emblem_mod_first", "Texture_Division_Emblem_mod_second" };
                foreach (var key in keys) EmblemAssets.Plan(root, state with { Emblem = key, Asset = new(key, EmblemPng, EmblemTexturePath) }, files);
                AssertEmblemRegistered(root, files, [EmblemBase, .. keys]);
                Assert(files.Count(f => f.RelativePath == EmblemTexturePath) == 1 && files.Count(f => f.Kind == FormalTextFileKind.Binary) == 2, "同批共文件合并且包含两张PNG");
                var candidate = Encoding.UTF8.GetString(files.Single(f => f.RelativePath == EmblemTexturePath).CandidateBytes);
                Assert(candidate.Contains(", // keep this comment" + nl), "保留原有注释及换行");
                Assert(!candidate.Replace(nl, "").Contains('\n'), "新增内容沿用原换行");
                var stripped = candidate;
                foreach (var key in keys) stripped = stripped.Replace("        " + EmblemEntry(key) + "," + nl, "");
                Assert(stripped.StartsWith(Encoding.UTF8.GetString(original), StringComparison.Ordinal), "登记只插入新条目，原文本逐字保留");
                Assert(File.ReadAllBytes(Path.Combine(root, EmblemTexturePath)).SequenceEqual(original), "规划不写正式文件");
            }
            finally { Directory.Delete(root, true); }
        }
    }
    private static async Task Emblem1918LegacyRepair()
    {
        var root = EmblemFixture(legacy: true);
        try
        {
            var (_, _, data) = await LoadP5Async(root); var mother = data.Divisions.First();
            var op = DivisionIdentity.Operation(mother, DivisionIdentity.New(data, mother, false, []));
            using var store = new DraftStore(root); await store.LoadAsync(); await store.UpsertAsync(op);
            var service = new UnitTransactionService(); var preview = await service.PrepareApplyAsync(root, store.Operations);
            Assert(preview.Files.Count(f => f.Kind == FormalTextFileKind.Ndf) == 1 && preview.Files.All(f => f.Kind is not (FormalTextFileKind.Binary or FormalTextFileKind.Csv)), "旧师徽修复仅写纹理库，不改图片、名称和师引用");
            AssertEmblemRegistered(root, preview.Files.ToList(), EmblemLegacy);
            Assert(preview.PictureReadDependencies.ContainsKey(EmblemLegacyPath), "现有PNG进入提交前依赖检查");
            var png = Path.Combine(root, EmblemLegacyPath); var originalPng = File.ReadAllBytes(png);
            File.AppendAllText(png, "changed");
            await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.CommitApplyAsync(preview, store), "预览后PNG变化拒绝");
            File.WriteAllBytes(png, originalPng);
            var texture = Path.Combine(root, EmblemTexturePath); var original = File.ReadAllBytes(texture);
            File.AppendAllText(texture, "\n// external edit");
            await TestAssert.ThrowsAsync<TransactionValidationException>(() => service.CommitApplyAsync(preview, store), "预览后纹理库变化拒绝");
            File.WriteAllBytes(texture, original);
            preview = await service.PrepareApplyAsync(root, store.Operations);
            await service.CommitApplyAsync(preview, store);
            AssertEmblemRegistered(root, [], EmblemLegacy);
            Assert(File.ReadAllBytes(png).SequenceEqual(originalPng), "修复不覆盖PNG");
            var second = new List<PlannedFileChange>(); EmblemAssets.Repair(root, EmblemLegacy, second, new());
            Assert(second.Count == 0, "重复修复幂等，不重复登记");
            await service.CommitRestoreAsync(service.PrepareRestore(root, preview.BackupId));
            Assert(File.ReadAllBytes(texture).SequenceEqual(original) && File.ReadAllBytes(png).SequenceEqual(originalPng), "恢复只撤销登记且保留旧PNG");
        }
        finally { Directory.Delete(root, true); }
    }
    private static async Task Emblem1918Guards()
    {
        var root = EmblemFixture(legacy: true);
        try
        {
            var path = Path.Combine(root, EmblemTexturePath); var original = File.ReadAllText(path);
            var malformed = new[]
            {
                original.Replace("TBUCKToolAdditionalTextureBank", "UnknownBank"),
                original + EmblemBank("AnotherBank", EmblemEntry(EmblemBase)),
                original.Replace(EmblemEntry(EmblemBase), EmblemEntry(EmblemLegacy, EmblemBase)),
                original.Replace(EmblemEntry(EmblemBase), EmblemEntry(EmblemLegacy) + "," + EmblemEntry(EmblemLegacy)),
                original.Replace(EmblemEntry(EmblemBase), EmblemEntry(EmblemLegacy).Replace("Normal", "Disabled")),
                original.Replace("Textures = MAP", "Textures = SomeExpression + MAP"),
                original.Replace(EmblemEntry(EmblemBase), EmblemEntry(EmblemLegacy).Replace("~/" + EmblemLegacy, "$/Wrong/" + EmblemLegacy)),
                original + $"{EmblemLegacy} is TUIResourceTexture_Common(FileName = 'GameData:/Assets/legacy.png')\n"
            };
            foreach (var text in malformed)
            {
                File.WriteAllText(path, text, new UTF8Encoding(false));
                var rejected = false;
                try { EmblemAssets.Repair(root, EmblemLegacy, [], new()); }
                catch (Exception ex) when (ex is TransactionValidationException or InvalidDataException) { rejected = true; }
                Assert(rejected && File.ReadAllText(path) == text, "未知、歧义、重复或错误注册阻止并不写文件");
            }
            File.WriteAllText(path, original, new UTF8Encoding(false));
            File.Delete(Path.Combine(root, EmblemLegacyPath));
            await TestAssert.ThrowsAsync<TransactionValidationException>(() => { EmblemAssets.Repair(root, EmblemLegacy, [], new()); return Task.CompletedTask; }, "缺失PNG不得仅补注册掩盖问题");
        }
        finally { Directory.Delete(root, true); }
    }
}
