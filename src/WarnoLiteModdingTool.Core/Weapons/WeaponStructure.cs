using System.Text.Json;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Core.Weapons;

public sealed record WeaponStructureSource(string File, string Name, string Body);
public sealed record WeaponStructureAddition(string Id, string Turret, string Box, string Body,
    WeaponStructureSource Source, string SourceMount, string SourceUnit, string VisualMount,
    Dictionary<string, string> PropertyMap);
public sealed record WeaponStructureTurret(string Id, string Body, string VisualTurret);
public sealed record WeaponStructureState(int Version, string Id, string Unit, WeaponStructureSource Weapon,
    string UnitBaseline, bool Shared, List<WeaponStructureAddition> Added, List<string> Removed,
    Dictionary<string, int> Boxes, Dictionary<string, Dictionary<string, string>> Fields,
    List<WeaponStructureTurret> Turrets)
{
    public Dictionary<string, Dictionary<string, string>> AmmoFields { get; init; } = [];
    public Dictionary<string, string> AmmoBaselines { get; init; } = [];
    public List<WeaponStructureSource> PresentationBaselines { get; init; } = [];
    public string? CreationId { get; init; }
    public bool Initialize { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasChanges => Initialize ? Added.Count > 0 : Added.Count > 0 || Removed.Count > 0 || Fields.Values.Any(f => f.Count > 0) || AmmoFields.Values.Any(f => f.Count > 0) || Boxes.Keys.Any(k => !k.StartsWith("b:", StringComparison.Ordinal)) || Boxes.Any(p => p.Key.StartsWith("b:", StringComparison.Ordinal) && OriginalBox(p.Key) != p.Value);
    private int OriginalBox(string key) { var s = new WeaponStructureSyntax(Weapon.Body); return int.Parse(s.Document.Raw(s.Boxes[int.Parse(key[2..])]), System.Globalization.CultureInfo.InvariantCulture); }
}
public sealed record WeaponStructureRow(string Id, string Turret, string Body, string Box, bool Added, bool Removed)
{
    public string Ammo => NdfSyntaxDocument.Leaf(WeaponStructureSyntax.Values(Body, "TMountedWeaponDescriptor", ["Ammunition"])["Ammunition"]);
}

public static class WeaponStructure
{
    public static readonly string[] PresentationFields = ["EffectTag", "HandheldEquipmentKey", "WeaponActiveAndCanShootPropertyName", "WeaponIgnoredPropertyName", "WeaponShootDataPropertyName"];
    public static readonly string[] EditableMountFields = ["Ammunition", "NbWeapons", "HideInInterface"];
    public static WeaponStructureState Copy(WeaponStructureState state) => JsonSerializer.Deserialize<WeaponStructureState>(JsonSerializer.Serialize(state))!;
    public static WeaponStructureState Read(DraftOperation op)
    {
        try
        {
            var state = JsonSerializer.Deserialize<WeaponStructureState>(op.TargetRaw);
            if (state is null || state.Version != 1 || state.Added is null || state.Removed is null || state.Boxes is null || state.Fields is null || state.Turrets is null || state.AmmoFields is null || state.AmmoBaselines is null || !Guid.TryParseExact(state.Id, "N", out _)) throw new JsonException();
            return state;
        }
        catch (JsonException) { throw WeaponStructureSyntax.Error("武器槽草稿格式无效"); }
    }
    public static WeaponStructureSource Source(WeaponRecord weapon) => new(weapon.Source.RelativeSourceFile, weapon.Name,
        Projects.ProjectReadScope.ReadAllText(weapon.Source.SourceFile).Substring(weapon.Source.CharacterOffset, weapon.Source.CharacterLength));
    public static WeaponStructureState New(UnitRecord unit, WeaponRecord weapon, bool shared = false, string? creationId = null)
    {
        var source = Source(weapon); var syntax = new WeaponStructureSyntax(source.Body);
        var initialize = unit.Weapons.Count == 0;
        return new(1, Guid.NewGuid().ToString("N"), creationId ?? unit.Name, source, UnitCreation.Source(unit), shared && !initialize, [], initialize ? syntax.Mounts.Select(m => m.Id).ToList() : [],
            syntax.Boxes.Select((b, i) => (Key: "b:" + i, Value: int.Parse(syntax.Document.Raw(b), System.Globalization.CultureInfo.InvariantCulture))).ToDictionary(p => p.Key, p => p.Value), [], []) { CreationId = creationId, Initialize = initialize };
    }
    public static DraftOperation Operation(WeaponStructureState state)
    {
        var json = JsonSerializer.Serialize(state); var scope = state.Shared ? "*" : state.Unit;
        return new(DraftOperation.CreateId(DraftTargetKind.WeaponStructure, state.Weapon.File, state.Weapon.Name, "weapon.structure:" + scope), "weapon-structure:" + state.Id,
            DraftTargetKind.WeaponStructure, "weapons", state.Weapon.File, state.Weapon.Name, "TWeaponManagerModuleDescriptor", "weapon.structure:" + scope, "Weapon/Structure", "WeaponStructureV1",
            "", state.Weapon.Body, $"+{state.Added.Count} / −{state.Removed.Count}", json,
            $"武器槽 · {state.Unit} · +{state.Added.Count} / −{state.Removed.Count}", null, false, DateTimeOffset.UtcNow,
            EditScope: state.Shared ? DraftEditScope.AllReferences : DraftEditScope.CurrentUnit, SelectedUnitNames: state.Shared ? [] : [state.Unit], ContextWeaponName: state.Weapon.Name);
    }
    public static ResolvedDraftOperation Resolve(UnitWorkspaceData data, WeaponWorkspaceData? weapons, DraftOperation op, IEnumerable<DraftOperation> operations)
    {
        try
        {
            var state = Read(op); var w = weapons?.Weapon(state.Weapon.Name) ?? throw WeaponStructureSyntax.Error("武器已不存在");
            if (Source(w) != state.Weapon) throw WeaponStructureSyntax.Error("武器结构基线已变化");
            var unit = data.Units.SingleOrDefault(u => u.Name == state.Unit);
            if (state.CreationId is not null)
            {
                var creation = operations.SingleOrDefault(o => o.TargetKind == DraftTargetKind.UnitCreate && o.ObjectName == state.CreationId) ?? throw WeaponStructureSyntax.Error("待创建单位草稿不存在");
                var cs = UnitCreation.Read(creation); unit = data.Units.SingleOrDefault(u => u.Name == cs.Mother);
                if (creation.BaselineRaw != state.UnitBaseline) throw WeaponStructureSyntax.Error("创建母版已变化");
            }
            if (unit is null || UnitCreation.Source(unit) != state.UnitBaseline || (state.Initialize ? unit.Weapons.Count != 0 || state.Shared : !unit.Weapons.Contains(w.Name))) throw WeaponStructureSyntax.Error("目标单位或武器引用已变化");
            if (operations.Any(o => o.TargetKind == DraftTargetKind.UnitDelete && o.ObjectName == state.Unit)) throw WeaponStructureSyntax.Error("待删除单位不能修改武器槽");
            foreach (var source in state.Added.Select(a => a.Source).Distinct())
            {
                var donor = weapons!.Weapon(source.Name);
                if (donor is null || Source(donor) != source) throw WeaponStructureSyntax.Error("参考挂载基线已变化：" + source.Name);
            }
            Validate(state, weapons!);
            return new(op, DraftResolutionStatus.Active, "");
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or ArgumentException or FormatException or JsonException)
        { return new(op, DraftResolutionStatus.Conflict, e.Message); }
    }
    public static IReadOnlyList<WeaponStructureRow> Rows(WeaponStructureState state)
    {
        var syntax = new WeaponStructureSyntax(state.Weapon.Body); var rows = new List<WeaponStructureRow>();
        foreach (var m in syntax.Mounts) rows.Add(new(m.Id, m.TurretId, ApplyFields(state, m.Id, m.Body), "b:" + m.Box, false, state.Removed.Contains(m.Id)));
        foreach (var m in state.Added) rows.Add(new(m.Id, m.Turret, ApplyFields(state, m.Id, m.Body), m.Box, true, false));
        return rows;
    }
    private static string ApplyFields(WeaponStructureState state, string id, string body) => state.Fields.TryGetValue(id, out var fields) ? WeaponStructureSyntax.Set(body, "TMountedWeaponDescriptor", fields) : body;
    public static string Add(WeaponStructureState state, WeaponStructureSource source, string sourceMount, string sourceUnit, string targetTurret, string visualMount, string ammoRaw, int salves, string? box = null, bool copyTurret = false)
    {
        var donor = new WeaponStructureSyntax(source.Body); var mount = donor.Mounts.Single(m => m.Id == sourceMount);
        var target = new WeaponStructureSyntax(state.Weapon.Body); var visual = target.Mounts.SingleOrDefault(m => m.Id == visualMount) ?? throw WeaponStructureSyntax.Error("请选择目标单位已有的表现挂载");
        if (visual.TurretId != targetTurret) throw WeaponStructureSyntax.Error("表现挂载必须属于目标炮塔");
        var id = "new:" + Guid.NewGuid().ToString("N");
        var tid = targetTurret;
        if (copyTurret)
        {
            var donorTurret = donor.Turrets.Single(t => t.Id == mount.TurretId); var visualTurret = target.Turrets.Single(t => t.Id == targetTurret);
            if (donorTurret.Type != visualTurret.Type) throw WeaponStructureSyntax.Error("复制炮塔需要相同类型的目标挂点");
            tid = "new:" + Guid.NewGuid().ToString("N");
            // A new gameplay group reuses a known target bone; creating physical bones/models is outside this editor.
            var body = donorTurret.Body; var td = new NdfSyntaxDocument(body); var node = WeaponStructureSyntax.Constructor(td, new(0, td.Tokens.Count - 1), donorTurret.Type);
            var list = WeaponStructureSyntax.Assignment(td, node, "MountedWeaponDescriptorList");
            body = WeaponStructureSyntax.Set(body, donorTurret.Type, new Dictionary<string, string> { ["MountedWeaponDescriptorList"] = "[]" });
            var targetValues = WeaponStructureSyntax.Values(visualTurret.Body, visualTurret.Type, ["Tag", "YulBoneOrdinal", "MasterTurretYulBoneOrdinal"]);
            var donorValues = WeaponStructureSyntax.Values(body, donorTurret.Type, targetValues.Keys);
            if (targetValues.Keys.Any(k => !donorValues.ContainsKey(k))) throw WeaponStructureSyntax.Error("炮塔挂点字段不兼容");
            body = WeaponStructureSyntax.Set(body, donorTurret.Type, targetValues);
            state.Turrets.Add(new(tid, FreshIds(body), targetTurret));
        }
        if (salves < 0) throw WeaponStructureSyntax.Error("库存必须为非负整数");
        var boxId = box ?? "new:" + Guid.NewGuid().ToString("N");
        if (box is null) state.Boxes[boxId] = salves;
        else if (!state.Boxes.ContainsKey(box)) throw WeaponStructureSyntax.Error("弹药箱已不存在");
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in WeaponStructureSyntax.Values(mount.Body, "TMountedWeaponDescriptor", PresentationFields.Where(f => f != "EffectTag")).Values)
            foreach (var t in new NdfSyntaxDocument(raw).Tokens.Where(t => t.Text[0] is '\'' or '"'))
            {
                var key = NdfSyntaxDocument.Unquote(t.Text);
                map.TryAdd(key, key + "_WLMT_" + id[4..14]);
            }
        var bodyMount = FreshIds(ReplaceStrings(mount.Body, map));
        bodyMount = WeaponStructureSyntax.Set(bodyMount, "TMountedWeaponDescriptor", new Dictionary<string, string> { ["Ammunition"] = ammoRaw });
        state.Added.Add(new(id, tid, boxId, bodyMount, source, sourceMount, sourceUnit, visualMount, map));
        return id;
    }
    public static void Remove(WeaponStructureState state, string id)
    {
        if (state.Added.RemoveAll(a => a.Id == id) == 0)
        {
            if (!new WeaponStructureSyntax(state.Weapon.Body).Mounts.Any(m => m.Id == id)) throw WeaponStructureSyntax.Error("槽位已不存在");
            if (!state.Removed.Contains(id)) state.Removed.Add(id);
        }
        else { state.Fields.Remove(id); state.AmmoFields.Remove(id); }
        foreach (var key in state.Boxes.Keys.Where(k => k.StartsWith("new:", StringComparison.Ordinal) && !state.Added.Any(a => a.Box == k)).ToArray()) state.Boxes.Remove(key);
        state.Turrets.RemoveAll(t => !state.Added.Any(a => a.Turret == t.Id));
    }
    public static void UndoRemove(WeaponStructureState state, string id) => state.Removed.Remove(id);
    private static string FreshIds(string body)
    {
        var doc = new NdfSyntaxDocument(body);
        return UnitProjectGraph.Patch(body, doc.FindAssignmentsAnywhere("DescriptorId").Select(span =>
            new TextReplacement(doc.StartOffset(span), doc.Length(span), doc.Raw(span), "GUID:{" + Guid.NewGuid() + "}", "复制节点GUID")));
    }
    public static string ReplaceStrings(string body, IReadOnlyDictionary<string, string> map)
    {
        var doc = new NdfSyntaxDocument(body);
        return UnitProjectGraph.Patch(body, doc.Tokens.Where(t => t.Text[0] is '\'' or '"' && map.ContainsKey(NdfSyntaxDocument.Unquote(t.Text)))
            .Select(t => new TextReplacement(t.Start, t.Text.Length, t.Text, t.Text[0] + map[NdfSyntaxDocument.Unquote(t.Text)] + t.Text[^1], "独立表现属性")));
    }
    public static void Validate(WeaponStructureState state, WeaponWorkspaceData data)
    {
        var syntax = new WeaponStructureSyntax(state.Weapon.Body); var rows = Rows(state);
        if (rows.Select(r => r.Id).Distinct().Count() != rows.Count || state.Removed.Distinct().Count() != state.Removed.Count || state.Removed.Any(id => !syntax.Mounts.Any(m => m.Id == id))) throw WeaponStructureSyntax.Error("槽位身份无效");
        foreach (var row in rows.Where(r => !r.Removed))
        {
            if (!data.Ammunition.Any(a => a.Name == row.Ammo)) throw WeaponStructureSyntax.Error("弹药不存在：" + row.Ammo);
            if (!state.Boxes.TryGetValue(row.Box, out var n) || n < 0) throw WeaponStructureSyntax.Error("弹药箱库存无效");
            var values = WeaponStructureSyntax.Values(row.Body, "TMountedWeaponDescriptor", EditableMountFields);
            if (values.TryGetValue("NbWeapons", out var count) && (!int.TryParse(count, out var c) || c < 0)) throw WeaponStructureSyntax.Error("武器数量无效");
            if (values.TryGetValue("HideInInterface", out var hidden) && hidden is not ("True" or "False")) throw WeaponStructureSyntax.Error("隐藏设置无效");
        }
        foreach (var fields in state.Fields)
            if (!rows.Any(r => r.Id == fields.Key) || fields.Value.Keys.Any(k => !EditableMountFields.Contains(k))) throw WeaponStructureSyntax.Error("挂载字段目标无效");
        foreach (var edit in state.AmmoFields)
        {
            var row = rows.Single(r => r.Id == edit.Key); if (row.Removed) continue;
            var a = data.Ammo(row.Ammo)!; var final = a.Fields.ToDictionary(f => f.Key, f => f.DisplayValue);
            foreach (var f in edit.Value)
            {
                var field = a.Field(f.Key) ?? throw WeaponStructureSyntax.Error("弹药字段不存在");
                if (!WeaponValueConverter.TryFormat(field, f.Value, out var value, out _, out var error)) throw WeaponStructureSyntax.Error(error);
                final[f.Key] = value;
            }
            AmmoRangeValidator.Validate(a, final);
        }
    }
}
