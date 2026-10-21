using System.Globalization;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Core.Weapons;

public enum WeaponSafetyLevel { Error, Warning, Unverified }
public sealed record WeaponSafetyIssue(string Rule, WeaponSafetyLevel Level, string File, string Object, string Field, string Message)
{
    public override string ToString() => $"{Level} [{Rule}] · {File} · {Object} / {Field}: {Message}";
}

/// <summary>Checks the final, isolated object graph. No game installation or stock data is required.</summary>
public static partial class WeaponSafety
{
    private sealed class Node(string file, string name, string type, Func<NdfSyntaxDocument> document)
    {
        public Node(string file, string name, string type, string body) : this(file, name, type, () => new NdfSyntaxDocument(body)) { }
        public string File { get; } = file;
        public string Name { get; } = name;
        public string Type { get; } = type;
        private readonly Lazy<NdfSyntaxDocument> _document = new(document);
        public NdfSyntaxDocument Doc => _document.Value;
        public string Raw(string field)
        {
            var roots = Doc.FindConstructors(Type);
            if (roots.Count == 0) return "?";
            var values = Doc.FindDirectAssignments(roots[0], field);
            return values.Count == 0 ? "" : values.Count == 1 ? Doc.Raw(values[0]) : "?";
        }
        public string Key => File.ToUpperInvariant() + "|" + Name;
    }
    private sealed record Check(string Stamp, WeaponSafetyIssue? Issue);
    private static bool Number(string raw, out double value) => double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    private static bool Integer(string raw, out int value) => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    private static readonly string[] AmmoRelations = ["ShotsCountPerSalvo", "SimultaneousShotsCount", "TimeBetweenTwoShots", "SupplyCost", "CanShootOnPosition", "NbSalvosShootOnPosition", "HasDeploymentTime"];

    // Returns current problems, including warnings whose numerical thresholds are NOT engine contracts.
    public static IReadOnlyList<WeaponSafetyIssue> InspectAmmo(string file, string name, IReadOnlyDictionary<string, string> raw)
    {
        var result = new List<WeaponSafetyIssue>();
        string R(string key) => raw.GetValueOrDefault(key) ?? "";
        void Add(string rule, WeaponSafetyLevel level, string field, string zh, string en) => result.Add(new(rule, level, file, name, field, zh + " / " + en));
        foreach (var definition in WeaponFieldDefinitions.Ammo.Where(d => d.Constructor is null && d.ArgumentName is null && d.MapKey is null && d.ValueKind is WeaponValueKind.Integer or WeaponValueKind.Decimal).DistinctBy(d => d.FieldName)
            .Append(new("safety.simultaneous", "", "", "", WeaponFieldOwner.Ammo, "SimultaneousShotsCount", WeaponValueKind.Integer, true)))
        {
            var value = R(definition.FieldName);
            if (value.Length == 0) continue;
            if (!Number(value, out var number))
                Add("value." + definition.FieldName, WeaponSafetyLevel.Unverified, definition.FieldName, "不是可确认的有限数值", "Not a supported finite numeric literal");
            else if (definition.ValueKind == WeaponValueKind.Integer && !Integer(value, out _) || definition.NonNegative && number < 0)
                Add("value." + definition.FieldName, WeaponSafetyLevel.Error, definition.FieldName, "需要有效整数或非负值", "Expected a valid integer or non-negative value");
        }
        foreach (var field in new[] { "CanShootOnPosition", "HasDeploymentTime" })
            if (R(field).Length > 0 && R(field) is not ("True" or "False"))
                Add("value." + field, WeaponSafetyLevel.Unverified, field, "布尔值无法确认", "Boolean value cannot be verified");
        if (Number(R("TimeBetweenTwoShots"), out var time) && (time == 0 || Math.Abs(time * 10 - Math.Round(time * 10)) > 1e-8))
            Add("time", WeaponSafetyLevel.Warning, "TimeBetweenTwoShots", "短装填可能不符合时间步长；0.1秒仅为已核对模板参考，未确认运行时门槛，不自动取整", "Shot interval may not align with gameplay ticks; 0.1 s is a template reference, not a verified runtime limit; no rounding");
        if (Number(R("SupplyCost"), out var cost) && Number(R("ShotsCountPerSalvo"), out var shots))
        {
            if (shots == 0) Add("supply", WeaponSafetyLevel.Warning, "SupplyCost / ShotsCountPerSalvo", "齐射数为0，不能计算每发补给；请核对", "Zero salvo size; per-shot supply cannot be calculated");
            else if (cost / shots < .001) Add("supply", WeaponSafetyLevel.Warning, "SupplyCost / ShotsCountPerSalvo", "每发补给低于截图参考0.001；实际上下限待核实", "Per-shot supply below reported reference 0.001; engine limits unverified");
        }
        if (Integer(R("SimultaneousShotsCount"), out var simultaneous) && Integer(R("ShotsCountPerSalvo"), out var count) && simultaneous > count)
            Add("simultaneous", WeaponSafetyLevel.Error, "SimultaneousShotsCount / ShotsCountPerSalvo", $"同时射弹数{simultaneous}不能大于齐射数{count}", $"Simultaneous shots {simultaneous} exceed salvo size {count}");
        if (Integer(R("NbSalvosShootOnPosition"), out var salvos) && salvos > 0 && R("CanShootOnPosition") != "True")
            Add("position", R("CanShootOnPosition") == "False" ? WeaponSafetyLevel.Error : WeaponSafetyLevel.Unverified, "CanShootOnPosition / NbSalvosShootOnPosition", "正的点地齐射次数需要启用点地射击", "Positive position salvo count requires CanShootOnPosition=True");
        if (R("CanShootOnPosition") == "True" && (R("NbSalvosShootOnPosition").Length == 0 || R("NbSalvosShootOnPosition") == "0"))
            Add("position", WeaponSafetyLevel.Warning, "CanShootOnPosition / NbSalvosShootOnPosition", "点地射击已启用，但次数缺失或为0；缺省规则待核实", "Position fire enabled with missing/zero salvo count; default semantics unverified");
        return result;
    }

    public static Dictionary<string, byte[]> ValidateFinal(string root, IReadOnlyList<PlannedFileChange> files, ICollection<string> messages, bool generatedClones = false,
        IReadOnlyList<UnitCreationOrigin>? creationOrigins = null)
    {
        if (!files.Any(f => f.Kind == FormalTextFileKind.Ndf && new[] { "TAmmunitionDescriptor", "TWeaponManagerModuleDescriptor", "TEntityDescriptor", "TWeaponDeploymentModuleDescriptor", "TAirplaneMovementModuleDescriptor" }.Any(t => System.Text.Encoding.UTF8.GetString(f.CandidateBytes).Contains(t, StringComparison.Ordinal) || System.Text.Encoding.UTF8.GetString(f.OriginalBytes).Contains(t, StringComparison.Ordinal)))) return new(StringComparer.OrdinalIgnoreCase);
        using var ownedReads = Projects.ProjectReadScope.Current is null ? new Projects.ProjectReadScope().Enter() : null;
        var before = UnitProjectGraph.Read(root, tolerateUnreadable: true);
        var after = new UnitProjectGraph(root, files, tolerateUnreadable: true);
        foreach (var path in after.UnverifiedFiles.Where(p => files.Any(f => f.RelativePath.Equals(p, StringComparison.OrdinalIgnoreCase))))
            throw new TransactionValidationException("无法检查修改文件 / Cannot validate changed file: " + path, "weapon.safety");
        foreach (var coverage in after.CoverageIssues) messages.Add("未检查 / Unverified: " + coverage);
        var old = Evaluate(before); var current = Evaluate(after);
        var createdKeys = creationOrigins is { Count: > 0 }
            ? creationOrigins.Select(o => ObjectKey(o.RelativeFile, o.Name)).ToHashSet(StringComparer.Ordinal) : null;
        var inherited = createdKeys is null ? null : InheritedCreationIssues(before, after, old, current, creationOrigins!, messages);
        // Only business-generated clones inherit baseline rule state. Source edits cannot claim an origin by naming convention.
        var origins = generatedClones ? after.Objects.Where(n => n.Name.Contains("_WLMT_", StringComparison.Ordinal) &&
            createdKeys?.Contains(ObjectKey(n.RelativeSourceFile, n.Name)) != true && before.FindObjects(n.Name).Count == 0)
            .Select(n => (n.Name, Original: before.Objects.Where(o => o.TypeName == n.TypeName && n.Name.StartsWith(o.Name + "_WLMT_", StringComparison.Ordinal)).OrderByDescending(o => o.Name.Length).FirstOrDefault()?.Name))
            .Where(p => p.Original is not null).Distinct().ToArray() : [];
        string Canonical(string value)
        {
            foreach (var origin in origins.OrderByDescending(o => o.Name.Length)) value = System.Text.RegularExpressions.Regex.Replace(value, @"(?<![A-Za-z0-9_])" + System.Text.RegularExpressions.Regex.Escape(origin.Name) + @"(?![A-Za-z0-9_])", origin.Original!);
            return value;
        }
        current = current.ToDictionary(p => p.Key, p => new Check(Canonical(p.Value.Stamp), p.Value.Issue));
        var affected = current.Where(p => p.Value.Issue is not null && inherited?.Contains(p.Key) != true &&
            (!old.TryGetValue(Canonical(p.Key), out var previous) || previous.Stamp != p.Value.Stamp)).Select(p => p.Value.Issue!).ToArray();
        var errors = affected.Where(i => i.Level == WeaponSafetyLevel.Error || i.Level == WeaponSafetyLevel.Unverified).ToArray();
        if (errors.Length > 0)
        {
            var header = errors.All(i => i.Rule.StartsWith("module.", StringComparison.Ordinal))
                ? "单位模块引用或结构无法确认；请核对以下项目 / Unit module references or structure cannot be verified; review the following items:"
                : "单位、弹药或武器关联不允许应用；请核对以下项目 / Unit, ammunition or weapon dependencies prevent apply; review the following items:";
            throw new TransactionValidationException(header + "\n" + string.Join("\n", errors.Select(i => i.ToString())), "weapon.safety");
        }
        foreach (var item in affected) messages.Add(item.ToString());
        if (current.Any(p => !old.TryGetValue(Canonical(p.Key), out var previous) || previous.Stamp != p.Value.Stamp))
            messages.Add("已检查受影响弹药和武器的已知组合；未确认的数值门槛仅提示 / Checked known affected ammunition and weapon combinations; unverified numerical limits remain warnings");
        return current.Any(p => !old.TryGetValue(Canonical(p.Key), out var previous) || previous.Stamp != p.Value.Stamp) ? before.Dependencies : new(StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, Check> Evaluate(UnitProjectGraph graph)
    {
        var result = new Dictionary<string, Check>(StringComparer.Ordinal);
        var nodes = graph.Objects.Select(o => new Node(UnitProjectGraph.Normalize(o.RelativeSourceFile), o.Name, o.TypeName, () => graph.Document(o))).ToArray();
        var byKey = nodes.GroupBy(n => n.Key).ToDictionary(g => g.Key, g => g.ToArray());
        var byName = nodes.GroupBy(n => n.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        Node? Resolve(Node from, string raw)
        {
            var o = graph.Resolve(from.File, raw);
            if (o is null) return null;
            var matches = byKey.GetValueOrDefault(UnitProjectGraph.Normalize(o.RelativeSourceFile).ToUpperInvariant() + "|" + o.Name);
            return matches?.Length == 1 ? matches[0] : null;
        }
        void Record(Node node, string rule, string field, string stamp, string? message, WeaponSafetyLevel level = WeaponSafetyLevel.Error)
        {
            result[node.Key + "|" + rule] = new(stamp, message is null ? null : new(rule, level, node.File, node.Name, field, message));
        }
        foreach (var ammo in nodes.Where(n => n.Type == "TAmmunitionDescriptor"))
        {
            var fields = WeaponFieldDefinitions.Ammo.Where(d => d.Constructor is null && d.ArgumentName is null && d.MapKey is null).Select(d => d.FieldName).Concat(AmmoRelations).Distinct().ToDictionary(n => n, ammo.Raw);
            var issues = InspectAmmo(ammo.File, ammo.Name, fields).ToDictionary(i => i.Rule);
            foreach (var field in fields.Keys) Record(ammo, "value." + field, field, fields[field], issues.GetValueOrDefault("value." + field)?.Message, issues.GetValueOrDefault("value." + field)?.Level ?? WeaponSafetyLevel.Error);
            foreach (var pair in new[] { ("time", new[] { "TimeBetweenTwoShots" }), ("supply", new[] { "SupplyCost", "ShotsCountPerSalvo" }), ("position", new[] { "CanShootOnPosition", "NbSalvosShootOnPosition" }), ("simultaneous", new[] { "SimultaneousShotsCount", "ShotsCountPerSalvo" }) })
                Record(ammo, pair.Item1, string.Join(" / ", pair.Item2), string.Join("|", pair.Item2.Select(ammo.Raw)), issues.GetValueOrDefault(pair.Item1)?.Message, issues.GetValueOrDefault(pair.Item1)?.Level ?? WeaponSafetyLevel.Error);
        }
        var weapons = nodes.Where(n => n.Type == "TWeaponManagerModuleDescriptor").ToArray();
        var mounts = new Dictionary<string, List<(string Box, string Reference, Node? Ammo)>>();
        foreach (var weapon in weapons)
        {
            var turretLists = weapon.Doc.FindDirectAssignments(weapon.Doc.FindConstructors(weapon.Type)[0], "TurretDescriptorList");
            var complete = turretLists.Count == 1 && weapon.Doc.Raw(turretLists[0]).StartsWith('[');
            if (complete)
            {
                foreach (var turret in weapon.Doc.ReadArrayElements(turretLists[0]))
                {
                    var type = weapon.Doc.Tokens[turret.StartTokenIndex].Text;
                    var spans = weapon.Doc.FindConstructors(type, turret);
                    if (spans.Count != 1 || spans[0].TypeTokenIndex != turret.StartTokenIndex) { complete = false; break; }
                    var lists = weapon.Doc.FindDirectAssignments(spans[0], "MountedWeaponDescriptorList");
                    if (lists.Count != 1 || !weapon.Doc.Raw(lists[0]).StartsWith('[')) { complete = false; break; }
                    if (weapon.Doc.ReadArrayElements(lists[0]).Any(e => weapon.Doc.Tokens[e.StartTokenIndex].Text != "TMountedWeaponDescriptor")) { complete = false; break; }
                }
            }
            Record(weapon, "mount.coverage", "TurretDescriptorList", weapon.Raw("TurretDescriptorList"), complete ? null : "炮塔或挂载并非可识别内联结构 / Turrets or mounts are not supported inline structures", WeaponSafetyLevel.Unverified);
            var list = new List<(string Box, string Reference, Node? Ammo)>(); mounts[weapon.Key] = list;
            foreach (var mount in weapon.Doc.FindConstructors("TMountedWeaponDescriptor"))
            {
                string Raw(string f) { var a = weapon.Doc.FindDirectAssignments(mount, f); return a.Count == 1 ? weapon.Doc.Raw(a[0]) : "?"; }
                var reference = Raw("Ammunition"); var ammo = Resolve(weapon, reference);
                if (ammo is null && (reference == "$/GFX/Weapon/" + NdfSyntaxDocument.Leaf(reference) || reference == "~/" + NdfSyntaxDocument.Leaf(reference)))
                {
                    // The native Gfx ammunition files also contain private descriptors in the weapon compilation scope.
                    // Never fall back for a different qualified namespace, file family, or directory.
                    var native = (byName.GetValueOrDefault(NdfSyntaxDocument.Leaf(reference)) ?? []).Where(n => n.Type == "TAmmunitionDescriptor" &&
                        Path.GetDirectoryName(n.File) == Path.GetDirectoryName(weapon.File) &&
                        UnitProjectGraph.Normalize(Path.GetDirectoryName(n.File) ?? "").Equals("GameData/Generated/Gameplay/Gfx", StringComparison.OrdinalIgnoreCase) &&
                        Path.GetFileName(n.File) is "Ammunition.ndf" or "AmmunitionMissiles.ndf").ToArray();
                    if (native.Length == 1) ammo = native[0];
                }
                list.Add((Raw("AmmoBoxIndex"), reference, ammo?.Type == "TAmmunitionDescriptor" ? ammo : null));
            }
            foreach (var group in list.GroupBy(m => m.Box))
            {
                var stamp = string.Join(";", group.Select(m => m.Reference + ":" + (m.Ammo?.Raw("ShotsCountPerSalvo") ?? "?")));
                var unknown = !Integer(group.Key, out var box) || box < 0 || group.Any(m => m.Ammo is null || group.Count() > 1 && !Integer(m.Ammo.Raw("ShotsCountPerSalvo"), out _));
                var mismatch = !unknown && group.Select(m => Integer(m.Ammo!.Raw("ShotsCountPerSalvo"), out var count) ? count : 0).Distinct().Count() > 1;
                Record(weapon, "box." + group.Key, "AmmoBoxIndex " + group.Key, stamp,
                    unknown ? "同箱弹药引用或齐射数无法确认 / Cannot verify ammo-box references or salvo sizes: " + stamp : mismatch ? "同箱弹药齐射数不同 / Ammunition sharing a box has different salvo sizes: " + stamp : null,
                    unknown ? WeaponSafetyLevel.Unverified : WeaponSafetyLevel.Error);
            }
        }
        foreach (var unit in nodes.Where(n => n.Type == "TEntityDescriptor"))
        {
            var modules = new List<Node>(); var unresolved = new List<string>(); var visited = new HashSet<string>(); var moduleIndex = 0;
            void AddModule(Node origin, NdfValueSpan span)
            {
                var raw = origin.Doc.Raw(span);
                var type = origin.Doc.Tokens[span.StartTokenIndex].Text;
                if (span.StartTokenIndex < span.EndTokenIndex && origin.Doc.Tokens[span.StartTokenIndex + 1].Text == "(")
                {
                    modules.Add(new(origin.File, origin.Name, type, () => origin.Doc.Slice(origin.Doc.StartOffset(span), origin.Doc.Length(span))));
                    return;
                }
                var resolved = Resolve(origin, raw);
                Record(unit, "module.ref." + moduleIndex++, "ModulesDescriptors", raw + "|" + resolved?.Type,
                    resolved is null ? "单位模块引用未解析 / Unit module reference unresolved: " + raw : null, WeaponSafetyLevel.Unverified);
                if (resolved is null) { unresolved.Add(raw); return; }
                if (!visited.Add(resolved.Key)) return;
                modules.Add(resolved);
            }
            var roots = unit.Doc.FindConstructors(unit.Type); if (roots.Count == 0) continue;
            var assignments = unit.Doc.FindDirectAssignments(roots[0], "ModulesDescriptors");
            if (assignments.Count != 1 || !unit.Doc.Raw(assignments[0]).StartsWith('['))
            {
                if (assignments.Count > 0) Record(unit, "module.coverage", "ModulesDescriptors", unit.Raw("ModulesDescriptors"), "单位模块列表无法验证 / Unit module list cannot be verified", WeaponSafetyLevel.Unverified);
                continue;
            }
            foreach (var element in unit.Doc.ReadArrayElements(assignments[0])) AddModule(unit, element);
            var deployment = modules.Any(m => m.Type == "TWeaponDeploymentModuleDescriptor");
            var airplane = modules.Any(m => m.Type is "TAirplaneMovementModuleDescriptor" or "AirplaneMovementDescriptor");
            foreach (var weapon in modules.Where(m => m.Type == "TWeaponManagerModuleDescriptor"))
            {
                // Inline weapon modules are explicitly outside the graph-backed mount scope.
                if (!mounts.TryGetValue(weapon.Key, out var list))
                {
                    Record(unit, "inline.weapon", "ModulesDescriptors", weapon.Raw("TurretDescriptorList"), "内联武器关联未验证 / Inline weapon dependencies unverified", WeaponSafetyLevel.Warning); continue;
                }
                var requires = list.Any(m => m.Ammo?.Raw("HasDeploymentTime") == "True");
                var deploymentStamp = string.Join("|", list.Select(m => m.Reference + ":" + m.Ammo?.Raw("HasDeploymentTime"))) + "|" + deployment + "|" + string.Join(";", unresolved);
                Record(unit, "deployment." + weapon.Name, "HasDeploymentTime / ModulesDescriptors", deploymentStamp,
                    requires && !deployment ? (unresolved.Count > 0 ? "单位模块存在未解析引用，不能确认部署模块 / Unresolved unit modules prevent deployment verification" : "展开射击缺少TWeaponDeploymentModuleDescriptor / Deployment fire requires TWeaponDeploymentModuleDescriptor") : null,
                    unresolved.Count > 0 ? WeaponSafetyLevel.Unverified : WeaponSafetyLevel.Error);
                if (!airplane) continue;
                var salves = weapon.Raw("Salves"); var flags = weapon.Raw("SalvoIsMainSalvo");
                int Length(string field) { var root = weapon.Doc.FindConstructors(weapon.Type)[0]; var v = weapon.Doc.FindDirectAssignments(root, field); return v.Count == 1 && weapon.Doc.Raw(v[0]).StartsWith('[') ? weapon.Doc.ReadArrayElements(v[0]).Count : -1; }
                var a = Length("Salves"); var b = Length("SalvoIsMainSalvo");
                Record(unit, "airplane." + weapon.Name, "Salves / SalvoIsMainSalvo", salves + "|" + flags,
                    a < 0 || b < 0 ? "飞机库存数组缺失或结构未验证 / Airplane inventory arrays missing or unsupported" : a != b ? $"飞机库存数组长度不同 / Airplane inventory array length mismatch: {a} / {b}" : null,
                    a < 0 || b < 0 ? WeaponSafetyLevel.Unverified : WeaponSafetyLevel.Error);
            }
        }
        return result;
    }
}
