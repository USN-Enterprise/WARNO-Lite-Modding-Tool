using System.Globalization;
using System.Text.RegularExpressions;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Core.Weapons;

public sealed record MissileMotionBinding(string Unit, string Weapon, int Mount, string Ammo, string Key,
    string Reference, string EntityFile, string Entity, string Body, string DefaultBefore, string DefaultAfter,
    string UncontrollableBefore, string UncontrollableAfter);

// Each owner stores its own physical baselines. Loading a legacy Ammo edit never creates bindings.
public static class MissileMotion
{
    public const string ReferenceKey = "ammo.pro.MissileDescriptor";
    public static bool IsParameter(string key) => key is "ammo.speed.projectile" or "ammo.speed.acceleration";
    public static bool Has(DraftOperation op) => op.MissileMotion is { Count: > 0 };
    public static Func<UnitProjectGraph> Source(string root, CancellationToken token = default)
    {
        var gate = new object();
        WeakReference<UnitProjectGraph>? source = null;
        return () => Projects.ProjectReadScope.Memo("missile-motion:" + Path.GetFullPath(root), () =>
        {
            lock (gate)
            {
                if (source is not null && source.TryGetTarget(out var saved)) return saved;
                var graph = Projects.ProjectReadScope.Current is null
                    ? new UnitProjectGraph(root, deletionScope: true, cancellationToken: token)
                    : UnitProjectGraph.Read(root, deletionScope: true);
                source = new(graph);
                return graph;
            }
        });
    }
    public static UnitProjectGraph Graph(WeaponWorkspaceData data) => data.MotionSource?.Invoke()
        ?? new UnitProjectGraph(data.DamageSource!().Graph.Root, deletionScope: true);
    public static bool Guided(AmmoRecord ammo) => ammo.SafetyValues.GetValueOrDefault("ProjectileType") == "EProjectileType/GuidedMissile"
        || ammo.Field(AmmoProfessional.Key("ProjectileType"))?.RawValue == "EProjectileType/GuidedMissile";
    private static string Number(string raw)
    {
        if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || n < 0)
            throw new TransactionValidationException("导弹运动需要非负直接数值 / Missile motion requires a non-negative numeric literal: " + raw);
        return n.ToString("0.############################", CultureInfo.InvariantCulture);
    }
    public static NdfValueSpan Field(NdfSyntaxDocument doc, string config, string field)
    {
        var root = doc.FindConstructors("TEntityDescriptor").Single();
        var arrays = doc.FindDirectAssignments(root, "ModulesDescriptors");
        if (arrays.Count != 1) throw new TransactionValidationException("导弹模块列表不唯一 / Ambiguous missile module list");
        var modules = doc.FindConstructors("TGuidedMissileMovementModuleDescriptor", arrays[0]);
        if (modules.Count != 1) throw new TransactionValidationException("导弹运动模块不唯一 / Ambiguous missile movement module");
        var configs = doc.FindDirectAssignments(modules[0], config);
        if (configs.Count != 1) throw new TransactionValidationException("导弹运动配置缺失或不唯一 / Missing or ambiguous missile config: " + config);
        var constructors = doc.FindConstructors("TGuidedMissileMovementConfigDescriptor", configs[0]);
        if (constructors.Count != 1 || constructors[0].TypeTokenIndex != configs[0].StartTokenIndex || constructors[0].CloseTokenIndex != configs[0].EndTokenIndex)
            throw new TransactionValidationException("导弹运动配置需唯一内联结构 / Missile config must be uniquely inline: " + config);
        var fields = doc.FindDirectAssignments(constructors[0], field);
        if (fields.Count != 1) throw new TransactionValidationException("导弹运动字段缺失或不唯一 / Missing or ambiguous missile field: " + config + "." + field);
        _ = Number(doc.Raw(fields[0]));
        return fields[0];
    }
    public static string EntityField(string key) => key == "ammo.speed.projectile" ? "MaxSpeedGRU" : "MaxAccelerationGRU";
    private static bool Match(MissileMotionBinding b, WeaponBatchCell c) => b.Ammo == c.Ammo && b.Key == c.Key &&
        (b.Unit.Length == 0 || b.Unit == c.Unit && b.Weapon == c.Weapon && b.Mount == c.Mount);
    private static string FinalReference(WeaponWorkspaceData data, IReadOnlyList<WeaponBatchCell> cells, WeaponBatchCell c) =>
        cells.Where(e => e.Ammo == c.Ammo && e.Key == AmmoProfessional.Key("MissileDescriptor") &&
            (e.Unit.Length == 0 || e.Unit == c.Unit && e.Weapon == c.Weapon && e.Mount == c.Mount))
        .OrderBy(e => e.Unit.Length == 0 ? 0 : 1).LastOrDefault()?.Raw
        ?? data.Ammo(c.Ammo)!.Field(AmmoProfessional.Key("MissileDescriptor"))?.RawValue
        ?? throw new TransactionValidationException("导弹实体引用缺失 / Missing MissileDescriptor: " + c.Ammo);

    public static DraftOperation Prepare(WeaponWorkspaceData data, IReadOnlyList<DraftOperation> drafts, DraftOperation op,
        Func<string, string>? calculate = null)
    {
        if (!WeaponBatch.IsWeaponEdit(op)) return op;
        var own = WeaponBatch.Cells(data, [op]);
        var speed = own.Where(c => IsParameter(c.Key) && Guided(data.Ammo(c.Ammo)!)).ToArray();
        if (speed.Length == 0) return op with { MissileMotion = null };
        var all = WeaponBatch.Cells(data, drafts.Where(o => o.Id != op.Id).Append(op).ToArray());
        var prior = drafts.SelectMany(o => o.MissileMotion ?? []).ToArray();
        var graph = Graph(data); var bindings = new List<MissileMotionBinding>();
        foreach (var c in speed)
        {
            var ammo = data.Ammo(c.Ammo)!; var reference = FinalReference(data, all, c);
            var entity = AmmoProfessionalValidation.MissileTarget(graph, ammo.Source.RelativeSourceFile, reference);
            if (entity?.TypeName != "TEntityDescriptor") throw new TransactionValidationException("导弹实体引用无法唯一解析 / Cannot resolve missile entity: " + c.Ammo);
            var body = graph.Body(entity); var doc = new NdfSyntaxDocument(body); var field = EntityField(c.Key);
            var before = doc.Raw(Field(doc, "DefaultConfig", field)); var lost = doc.Raw(Field(doc, "UncontrollableConfig", field));
            var pending = prior.Where(b => Match(b, c) && b.Reference == reference).OrderBy(b => b.Unit.Length == 0 ? 0 : 1).LastOrDefault();
            var after = Number(calculate?.Invoke(pending?.DefaultAfter ?? before) ?? c.Raw);
            var lostAfter = Number(calculate?.Invoke(pending?.UncontrollableAfter ?? lost) ?? c.Raw);
            bindings.Add(new(c.Unit, c.Weapon, c.Mount, c.Ammo, c.Key, reference, entity.RelativeSourceFile, entity.Name,
                body, before, after, lost, lostAfter));
        }
        return op with { MissileMotion = bindings };
    }
    public static void Validate(WeaponWorkspaceData data, DraftOperation op)
    {
        if (!Has(op)) return;
        var cells = WeaponBatch.Cells(data, [op]); var graph = Graph(data);
        foreach (var b in op.MissileMotion!)
        {
            if (!cells.Any(c => c.Unit == b.Unit && c.Weapon == b.Weapon && c.Mount == b.Mount && c.Ammo == b.Ammo && c.Key == b.Key) || !IsParameter(b.Key))
                throw new TransactionValidationException("导弹联动范围无效 / Invalid missile binding scope");
            var entity = graph.RequireObject(b.EntityFile, b.Entity);
            var ammo = data.Ammo(b.Ammo)!;
            if (graph.Body(entity) != b.Body || !UnitProjectGraph.Same(AmmoProfessionalValidation.MissileTarget(graph, ammo.Source.RelativeSourceFile, b.Reference), entity))
                throw new TransactionValidationException("导弹实体基线已变化 / Missile entity baseline changed: " + b.Entity);
            var doc = new NdfSyntaxDocument(b.Body); var field = EntityField(b.Key);
            if (doc.Raw(Field(doc, "DefaultConfig", field)) != b.DefaultBefore || doc.Raw(Field(doc, "UncontrollableConfig", field)) != b.UncontrollableBefore)
                throw new TransactionValidationException("导弹运动基线无效 / Invalid missile movement baseline");
            _ = Number(b.DefaultAfter); _ = Number(b.UncontrollableAfter);
        }
        if (cells.Where(c => IsParameter(c.Key) && Guided(data.Ammo(c.Ammo)!)).Any(c => !op.MissileMotion!.Any(b => b.Unit == c.Unit && b.Weapon == c.Weapon && b.Mount == c.Mount && b.Ammo == c.Ammo && b.Key == c.Key)))
            throw new TransactionValidationException("导弹联动不完整 / Incomplete missile binding");
    }
    public static void ValidateCombination(WeaponWorkspaceData data, IReadOnlyList<DraftOperation> ops)
    {
        var cells = WeaponBatch.Cells(data, ops);
        foreach (var op in ops.Where(Has))
        {
            Validate(data, op);
            foreach (var b in op.MissileMotion!)
            {
                var c = cells.First(c => c.Unit == b.Unit && c.Weapon == b.Weapon && c.Mount == b.Mount && c.Ammo == b.Ammo && c.Key == b.Key);
                if (FinalReference(data, cells, c) != b.Reference)
                    throw new TransactionValidationException("导弹引用草稿已变化，请重新编辑速度/加速度 / Missile reference changed; re-edit motion: " + b.Ammo);
                if (ops.SelectMany(o => o.MissileMotion ?? []).Any(x => x != b && x.Ammo == b.Ammo && x.Key == b.Key &&
                    (x.Unit.Length == 0 || b.Unit.Length == 0 || x.Unit == b.Unit && x.Weapon == b.Weapon && x.Mount == b.Mount) &&
                    (x.DefaultAfter != b.DefaultAfter || x.UncontrollableAfter != b.UncontrollableAfter || x.Reference != b.Reference)))
                    throw new TransactionValidationException("导弹实体草稿目标冲突 / Conflicting missile movement drafts: " + b.Ammo);
            }
        }
    }
    public static string Details(DraftOperation op) => string.Join("\n", (op.MissileMotion ?? []).Select(b =>
        $"{b.Ammo} · {b.Entity}\n{b.EntityFile}\n{b.Key}\nDefaultConfig: {b.DefaultBefore} → {b.DefaultAfter}\nUncontrollableConfig: {b.UncontrollableBefore} → {b.UncontrollableAfter}\n" +
        (b.Unit.Length == 0 ? "全部Ammo引用 / All Ammo users" : b.Unit) + "\n实体按需隔离 / Entity isolated when needed"));

    internal sealed record DisplayEntity(NdfObjectInfo? Source, IReadOnlyDictionary<string, string> Values);
    internal sealed record SavedDisplay(string Ammo, string Reference, DisplayEntity Entity);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WeaponWorkspaceData,
        System.Collections.Concurrent.ConcurrentDictionary<(string Ammo, string Reference), DisplayEntity>> DisplayCache = new();
    // UI summaries are a small projection of the same exact resolver used by transactions.
    // The temporary read scope owns the full graph; the workspace retains only these values.
    internal static SavedDisplay[] PrepareDisplay(WeaponWorkspaceData data, SavedDisplay[]? saved, CancellationToken cancellationToken)
    {
        var targets = data.Ammunition.Where(Guided).Select(a => (Ammo: a, Reference: a.Field(ReferenceKey)?.RawValue ?? "")).ToArray();
        var cache = DisplayCache.GetOrCreateValue(data);
        if (saved is not null && saved.Length == targets.Length && saved.All(s => s is not null && s.Entity?.Values is { } values &&
            values.ContainsKey("ammo.speed.projectile") && values.ContainsKey("ammo.speed.acceleration")) &&
            targets.All(t => saved.Count(s => s.Ammo == t.Ammo.Name && s.Reference == t.Reference) == 1))
        {
            foreach (var item in saved) cache[(item.Ammo, item.Reference)] = item.Entity;
            return saved;
        }
        using var scope = new Projects.ProjectReadScope(cancellation: cancellationToken).Enter();
        var result = new List<SavedDisplay>(targets.Length);
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(new(target.Ammo.Name, target.Reference, Display(data, target.Ammo, target.Reference)));
        }
        return result.ToArray();
    }
    private static DisplayEntity Display(WeaponWorkspaceData data, AmmoRecord ammo, string reference) =>
        DisplayCache.GetOrCreateValue(data).GetOrAdd((ammo.Name, reference), _ =>
        {
            var graph = Graph(data);
            var entity = AmmoProfessionalValidation.MissileTarget(graph, ammo.Source.RelativeSourceFile, reference);
            var values = new Dictionary<string, string>();
            foreach (var key in new[] { "ammo.speed.projectile", "ammo.speed.acceleration" })
            {
                try
                {
                    if (entity is null) throw new InvalidDataException("MissileDescriptor");
                    var doc = graph.Document(entity); var field = EntityField(key);
                    values[key] = $"\nDefaultConfig: {doc.Raw(Field(doc, "DefaultConfig", field))} · UncontrollableConfig: {doc.Raw(Field(doc, "UncontrollableConfig", field))}\n{entity.Name}";
                }
                catch (Exception e) when (e is InvalidDataException or TransactionValidationException or InvalidOperationException)
                { values[key] = "导弹运动不可联动 / Missile motion unavailable: " + e.Message; }
            }
            return new(entity, values);
        });

    public static NdfObjectInfo? EntitySource(WeaponWorkspaceData data, IReadOnlyList<DraftOperation> ops, string ammo, string unit)
    {
        if (data.Ammo(ammo) is not { } a || !Guided(a)) return null;
        var reference = WeaponBatch.Cells(data, ops).Where(c => c.Ammo == ammo && c.Key == ReferenceKey && (c.Unit.Length == 0 || c.Unit == unit))
            .OrderBy(c => c.Unit.Length == 0 ? 0 : 1).LastOrDefault()?.Raw ?? a.Field(ReferenceKey)?.RawValue ?? "";
        return Display(data, a, reference).Source;
    }

    public static string Status(WeaponWorkspaceData data, IReadOnlyList<DraftOperation> ops, string ammoName, string key, string unit)
    {
        if (!IsParameter(key) || data.Ammo(ammoName) is not { } ammo || !Guided(ammo)) return "";
        var bindings = ops.SelectMany(o => o.MissileMotion ?? []).Where(b => b.Ammo == ammoName && b.Key == key && (b.Unit.Length == 0 || b.Unit == unit)).ToArray();
        if (bindings.Length > 0) return "导弹运动联动（待应用） / Missile motion linked (draft)\n" + string.Join("\n", bindings.Select(b =>
            $"DefaultConfig: {b.DefaultBefore} → {b.DefaultAfter} · UncontrollableConfig: {b.UncontrollableBefore} → {b.UncontrollableAfter}").Distinct());
        try
        {
            var reference = ammo.Field(AmmoProfessional.Key("MissileDescriptor"))?.RawValue ?? "";
            var display = Display(data, ammo, reference).Values[key];
            if (!display.StartsWith('\n')) return display;
            var legacy = ops.Any(o => o.TargetKind == DraftTargetKind.AmmoField && o.ObjectName == ammoName && o.FieldKey == key && !Has(o)) ||
                ops.Where(o => o.TargetKind == DraftTargetKind.WeaponBatch && !Has(o)).SelectMany(WeaponBatch.ReadCells).Any(c => c.Ammo == ammoName && c.Key == key);
            return (legacy ? "旧草稿仅Ammo，未联动实体 / Legacy draft edits Ammo only" : "联动Ammo、默认/失控运动 / Links Ammo and default/uncontrollable movement") +
                display;
        }
        catch (Exception e) when (e is InvalidDataException or TransactionValidationException or InvalidOperationException)
        { return "导弹运动不可联动 / Missile motion unavailable: " + e.Message; }
    }

    // Per apply, deduplicates complete entity variants across Ammo and unit clones.
    public sealed class Planner(WeaponWorkspaceData data, IReadOnlyList<DraftOperation> ops, Func<string, TextFileSnapshot> snapshot,
        Action<string, string, string> append, Action<string, TextReplacement> direct)
    {
        private readonly Dictionary<string, string> _variants = [];
        private readonly HashSet<string> _names = Graph(data).Objects.Select(o => o.Name).ToHashSet(StringComparer.Ordinal);
        private readonly HashSet<string> _debugNames = MissileDebugRepair.Analyze(Graph(data).Root).Reserved();
        public void Rewrite(AmmoRecord ammo, string unit, string weapon, int mount, Dictionary<string, string> values)
        {
            var bindings = ops.SelectMany(o => o.MissileMotion ?? []).Where(b => b.Ammo == ammo.Name &&
                (b.Unit.Length == 0 || b.Unit == unit && b.Weapon == weapon && b.Mount == mount))
                .GroupBy(b => b.Key).Select(g => g.OrderBy(b => b.Unit.Length == 0 ? 0 : 1).Last()).ToArray();
            if (bindings.Length == 0) return;
            if (bindings.Select(b => b.EntityFile + "|" + b.Entity + "|" + b.Body).Distinct().Count() != 1)
                throw new TransactionValidationException("运动参数引用不同实体 / Motion fields refer to different entities");
            var first = bindings[0]; var doc = new NdfSyntaxDocument(first.Body); var edits = new List<TextReplacement>();
            foreach (var b in bindings)
            foreach (var (config, after) in new[] { ("DefaultConfig", b.DefaultAfter), ("UncontrollableConfig", b.UncontrollableAfter) })
            {
                var span = Field(doc, config, EntityField(b.Key));
                if (Number(doc.Raw(span)) != after) edits.Add(new(doc.StartOffset(span), doc.Length(span), doc.Raw(span), after, config + "." + EntityField(b.Key)));
            }
            if (edits.Count == 0) return;
            // Direct edits are allowed only when every external reference is this shared Ammo.
            // Unknown owners, local edits and other Ammo always retain the original entity.
            var graph = Graph(data); var source = graph.RequireObject(first.EntityFile, first.Entity);
            var exclusive = unit.Length == 0 && graph.CoverageIssues.Count == 0;
            foreach (var cell in WeaponBatch.Cells(data, ops).Where(c => c.Key == ReferenceKey && c.Ammo != ammo.Name))
                if (UnitProjectGraph.Same(AmmoProfessionalValidation.MissileTarget(graph, data.Ammo(cell.Ammo)!.Source.RelativeSourceFile, cell.Raw), source)) exclusive = false;
            foreach (var file in graph.Files.Values)
            foreach (var token in file.Syntax.Tokens.Where(t => t.Text[0] is not ('\'' or '"') && t.Text.Contains(first.Entity, StringComparison.Ordinal)))
            {
                if (file.Path.Equals(UnitProjectGraph.Normalize(first.EntityFile), StringComparison.OrdinalIgnoreCase) && token.Start >= source.CharacterOffset && token.End <= source.CharacterOffset + source.CharacterLength) continue;
                var owner = file.Objects.FirstOrDefault(o => token.Start >= o.CharacterOffset && token.End <= o.CharacterOffset + o.CharacterLength);
                if (owner?.Name != ammo.Name || !file.Path.Equals(UnitProjectGraph.Normalize(ammo.Source.RelativeSourceFile), StringComparison.OrdinalIgnoreCase) || token.Text != first.Reference) exclusive = false;
            }
            if (exclusive)
            {
                foreach (var edit in edits) direct(first.EntityFile, edit with { Offset = source.CharacterOffset + edit.Offset });
                return;
            }
            var signature = first.EntityFile + "|" + UnitProjectGraph.Patch(first.Body, edits);
            if (!_variants.TryGetValue(signature, out var name))
            {
                do name = first.Entity + "_WLMT_" + Guid.NewGuid().ToString("N")[..10]; while (!_names.Add(name));
                var declaration = doc.Tokens.Select((t, i) => (t, i)).Single(p => p.t.Text == first.Entity && p.i + 1 < doc.Tokens.Count && doc.Tokens[p.i + 1].Text == "is").t;
                edits.Add(new(declaration.Start, declaration.Length, declaration.Text, name, "隔离导弹实体"));
                var roots = doc.FindConstructors("TEntityDescriptor"); var ids = doc.FindDirectAssignments(roots.Single(), "DescriptorId");
                if (ids.Count != 1 || !Regex.IsMatch(doc.Raw(ids[0]), @"^GUID:\{[0-9A-Fa-f-]{36}\}$"))
                    throw new TransactionValidationException("导弹DescriptorId无法隔离 / Cannot isolate missile DescriptorId");
                edits.Add(new(doc.StartOffset(ids[0]), doc.Length(ids[0]), doc.Raw(ids[0]), $"GUID:{{{Guid.NewGuid()}}}", "导弹新DescriptorId"));
                var debugSpan = MissileDebugRepair.DebugSpan(doc);
                var debugName = MissileDebugRepair.Allocate(name, _debugNames);
                edits.Add(new(doc.StartOffset(debugSpan), doc.Length(debugSpan), doc.Raw(debugSpan), "'" + debugName + "'", "导弹唯一debug名称"));
                foreach (var token in doc.Tokens.Where(t => t.Start != declaration.Start && t.Text[0] is not ('\'' or '"') && NdfSyntaxDocument.Leaf(t.Text) == first.Entity))
                {
                    if (token.Text != first.Entity && token.Text != "~/" + first.Entity)
                        throw new TransactionValidationException("导弹自引用作用域不支持 / Unsupported missile self reference");
                    edits.Add(new(token.Start, token.Length, token.Text, token.Text[..^first.Entity.Length] + name, "导弹自引用"));
                }
                var candidate = UnitProjectGraph.Patch(first.Body, edits);
                var checkedDoc = new NdfSyntaxDocument(candidate);
                foreach (var b in bindings)
                {
                    if (Number(checkedDoc.Raw(Field(checkedDoc, "DefaultConfig", EntityField(b.Key)))) != b.DefaultAfter ||
                        Number(checkedDoc.Raw(Field(checkedDoc, "UncontrollableConfig", EntityField(b.Key)))) != b.UncontrollableAfter)
                        throw new TransactionValidationException("导弹最终运动候选校验失败 / Missile candidate validation failed");
                }
                _ = snapshot(first.EntityFile);
                append(first.EntityFile, name, candidate.TrimEnd('\r', '\n'));
                _variants[signature] = name;
            }
            values[AmmoProfessional.Key("MissileDescriptor")] = first.Reference[..^first.Entity.Length] + name;
        }
    }
}
