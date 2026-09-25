using System.Globalization;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Core.Rules;

public sealed record TerrainCell(string Key, string Label, string Raw, int Offset, int Length,
    bool Boolean, bool Basic, string Hint, string Error = "");
public sealed record TerrainRecord(string Name, string Type, string File, IReadOnlyList<TerrainCell> Cells, string Error);

public sealed class TerrainWorkspace
{
    public string Root { get; }
    public List<TerrainRecord> Terrains { get; } = [];
    public List<string> Diagnostics { get; } = [];
    private TerrainWorkspace(string root) => Root = root;
    public static string Label(string name) => name switch
    {
        "ForetLegere" => "轻林", "ForetDense" => "密林", "Batiment" => "建筑", "PetitBatiment" => "小建筑",
        "Ruin" => "废墟", "DefaultTerrain" => "普通地面", "Bloqueur" => "阻挡区", "EauPeuProfonde" => "浅水",
        "EauProfonde" => "深水", "Rocher" => "岩石", "MediumSmoke" => "烟雾", "BloqueConstruction" => "建造阻挡区", _ => name
    };
    private static readonly (string Field, string Label, bool Boolean, string Hint)[] Fields =
    [
        ("ConcealmentBonus", "地形隐蔽", false, "原始隐蔽数值，不是百分比或固定发现距离"),
        ("BloqueVision", "阻挡视线", true, "视线效果还取决于观察位置和地形高度"),
        ("DissimulationModifierGroundAir", "地空遮蔽", false, "原始遮蔽参数，不是米数或百分比"),
        ("DissimulationModifierGroundGround", "地地遮蔽", false, "原始遮蔽参数，不是米数或百分比"),
        ("BloqueInfanterie", "阻挡步兵", true, "普通通行与房屋驻扎是不同机制"),
        ("BloqueVehicule", "阻挡车辆", true, "关闭阻挡不保证可通行；还需检查单位地形速度表"),
        ("BloqueAmphibie", "阻挡两栖", true, "不会赋予单位两栖能力，还需检查运动与速度配置"),
        ("BloqueAtterrissage", "阻挡降落", true, "关闭此项不解除单位和坡度等其他降落限制"),
        ("AuthorizeNearGroundFlying", "允许近地飞行", true, "与允许降落分开，不解除全部飞行限制"),
        ("InflammabilityProbability", "可燃性概率", false, "范围0至1；设0不保证阻止忽略可燃性条件的弹药"),
    ];
    public static TerrainWorkspace Load(string root)
    {
        var result = new TerrainWorkspace(root);
        foreach (var path in UnitProjectGraph.InputFiles(root))
        {
            try
            {
                var text = WarnoLiteModdingTool.Core.Projects.ProjectReadScope.ReadAllText(Path.Combine(root, path));
                if (!text.Contains("TGameplayTerrain", StringComparison.Ordinal)) continue;
                result.Terrains.AddRange(Read(path, text));
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            { result.Diagnostics.Add(path + ": " + ex.Message); }
        }
        var duplicates = result.Terrains.GroupBy(t => t.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var types = result.Terrains.GroupBy(t => t.Type).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        for (var i = 0; i < result.Terrains.Count; i++)
        {
            var t = result.Terrains[i];
            if (duplicates.Contains(t.Name) || types.Contains(t.Type) || result.Diagnostics.Count > 0)
                result.Terrains[i] = t with { Error = "地形身份重复或部分文件不可读取，无法安全定位" };
        }
        return result;
    }
    public static IReadOnlyList<TerrainRecord> Read(string path, string text)
    {
        var scan = new NdfTopLevelScanner().Scan(text, path, "rules");
        if (scan.Diagnostics.Any(d => d.Severity == NdfDiagnosticSeverity.Error)) throw new InvalidDataException("地形文件存在语法错误");
        var doc = new NdfSyntaxDocument(text);
        var result = new List<TerrainRecord>();
        foreach (var registration in doc.FindConstructors("TGameplayTerrainsRegistration"))
        {
            var arrays = doc.FindDirectAssignments(registration, "Terrains");
            if (arrays.Count != 1 || !doc.Raw(arrays[0]).TrimStart().StartsWith('[')) throw new InvalidDataException("地形注册数组不唯一或不支持");
            foreach (var element in doc.ReadArrayElements(arrays[0]))
            {
                var constructors = doc.FindConstructors("TGameplayTerrain", element);
                if (constructors.Count != 1 || constructors[0].TypeTokenIndex != element.StartTokenIndex || constructors[0].CloseTokenIndex != element.EndTokenIndex)
                    throw new InvalidDataException("地形条目不是唯一内联对象");
                var c = constructors[0];
                string Identity(string key)
                {
                    var values = doc.FindDirectAssignments(c, key);
                    if (values.Count != 1) throw new InvalidDataException("地形身份不能唯一定位：" + key);
                    return NdfSyntaxDocument.Unquote(doc.Raw(values[0]));
                }
                var name = Identity("Name"); var type = Identity("TerrainType");
                var cells = new List<TerrainCell>();
                foreach (var f in Fields)
                {
                    var spans = doc.FindDirectAssignments(c, f.Field);
                    if (spans.Count == 0) continue;
                    var raw = doc.Raw(spans[0]);
                    var valid = f.Boolean ? bool.TryParse(raw, out _) : Number(raw, out _);
                    cells.Add(new(f.Field, f.Label, raw, doc.StartOffset(spans[0]), doc.Length(spans[0]), f.Boolean, false, f.Hint,
                        spans.Count != 1 || !valid ? "字段多义或不是支持的直接值" : ""));
                }
                // Height stays visible/read-only until its engine semantics have been verified.
                var height = doc.FindDirectAssignments(c, "HeightInMeters");
                if (height.Count > 0) cells.Add(new("HeightInMeters", "地形高度（米）", doc.Raw(height[0]), doc.StartOffset(height[0]), doc.Length(height[0]), false, false,
                    "不等于地图高程或模型高度", "具体引擎作用尚未验证，本版只读"));
                var maps = doc.FindDirectAssignments(c, "DamageModifierPerFamilyAndResistance");
                var error = "";
                if (maps.Count > 1) error = "伤害修正表重复";
                else if (maps.Count == 1)
                {
                    var outer = Map(doc, maps[0]);
                    var damageKeys = new HashSet<string>();
                    foreach (var attack in outer)
                    {
                        var family = doc.Raw(attack.Key);
                        if (!damageKeys.Add(family)) { error = "伤害族重复"; break; }
                        var resistanceKeys = new HashSet<string>();
                        foreach (var target in Map(doc, attack.Value))
                        {
                            var resistance = doc.Raw(target.Key); var raw = doc.Raw(target.Value);
                            if (!resistanceKeys.Add(resistance)) { error = "目标抗性族重复"; break; }
                            var valid = Number(raw, out var value);
                            var basic = valid && value is >= 0 and <= 1 && resistance == "ResistanceFamily_infanterie" &&
                                new[] { "he", "he_autocanon", "superhe", "balle", "balledca", "howz", "howz_bombe", "fmballe" }.Any(s => family == "DamageFamily_" + s);
                            cells.Add(new("damage/" + family + "/" + resistance, family + " → " + resistance, raw, doc.StartOffset(target.Value), doc.Length(target.Value),
                                false, basic, "此组合影响双方符合条件的单位；不是最终伤害的全部公式", valid ? "" : "倍率不是支持的直接数值"));
                        }
                    }
                }
                result.Add(new(name, type, path, cells, error));
            }
        }
        return result;
    }
    private static IReadOnlyList<NdfMapEntry> Map(NdfSyntaxDocument doc, NdfValueSpan span)
    {
        if (doc.Tokens[span.StartTokenIndex].Text != "MAP" || doc.Tokens[span.EndTokenIndex].Text != "]")
            throw new InvalidDataException("不支持的地形伤害MAP");
        var entries = doc.ReadMapEntries(span);
        if (entries.Count != doc.ReadArrayElements(span).Count) throw new InvalidDataException("地形伤害MAP条目无效");
        return entries;
    }
    public static bool Number(string raw, out decimal number) => decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    public static void Validate(TerrainRecord terrain, TerrainCell cell, string raw)
    {
        if (terrain.Error.Length > 0 || cell.Error.Length > 0) throw new InvalidDataException(terrain.Error + cell.Error);
        if (cell.Boolean ? !bool.TryParse(raw, out _) : !Number(raw, out _)) throw new InvalidDataException("请输入有效的地形参数");
        if (cell.Key == "InflammabilityProbability" && (!Number(raw, out var p) || p < 0 || p > 1)) throw new InvalidDataException("可燃性概率必须在0至1之间");
    }
    public DraftOperation Operation(TerrainRecord terrain, TerrainCell cell, string raw)
    {
        Validate(terrain, cell, raw);
        return new(DraftOperation.CreateId(DraftTargetKind.TerrainField, terrain.File, terrain.Name, cell.Key), null,
            DraftTargetKind.TerrainField, "rules", terrain.File, terrain.Name, terrain.Type, cell.Key, cell.Key,
            cell.Boolean ? "Boolean" : "Decimal", cell.Raw, cell.Raw, raw, raw,
            "地形规则 · " + Label(terrain.Name) + " · " + cell.Label, null, false, DateTimeOffset.UtcNow);
    }
    public ResolvedDraftOperation Resolve(DraftOperation op)
    {
        try
        {
            var t = Terrains.Single(t => t.Name == op.ObjectName && t.File == op.RelativeSourceFile && t.Type == op.ObjectType);
            var c = t.Cells.Single(c => c.Key == op.FieldKey);
            Validate(t, c, op.TargetRaw);
            if (c.Raw != op.BaselineRaw) throw new InvalidDataException("地形参数基线已变化");
            return new(op, DraftResolutionStatus.Active, "");
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException or ArgumentException)
        { return new(op, DraftResolutionStatus.Conflict, ex.Message); }
    }
    public void Plan(IReadOnlyList<DraftOperation> operations, List<PlannedFileChange> files)
    {
        var ops = operations.Where(o => o.TargetKind == DraftTargetKind.TerrainField).ToArray();
        var candidate = new CandidateTextFiles(Root, files);
        foreach (var op in ops)
        {
            var resolved = Resolve(op);
            if (resolved.Status != DraftResolutionStatus.Active) throw new TransactionValidationException(resolved.Reason);
            var text = candidate.Get(op.RelativeSourceFile);
            var t = Read(op.RelativeSourceFile, text).Single(t => t.Name == op.ObjectName && t.Type == op.ObjectType);
            var c = t.Cells.Single(c => c.Key == op.FieldKey); Validate(t, c, op.TargetRaw);
            if (c.Raw != op.BaselineRaw) throw new TransactionValidationException("地形候选字段冲突");
            var edited = text.Remove(c.Offset, c.Length).Insert(c.Offset, op.TargetRaw);
            var check = Read(op.RelativeSourceFile, edited).Single(t => t.Name == op.ObjectName);
            if (check.Error.Length > 0 || check.Cells.Single(c => c.Key == op.FieldKey).Raw != op.TargetRaw) throw new TransactionValidationException("地形候选验证失败");
            candidate.Set(op.RelativeSourceFile, edited);
        }
        candidate.Complete("地形共享规则（双方适用）；仅修改既有参数");
    }
}
