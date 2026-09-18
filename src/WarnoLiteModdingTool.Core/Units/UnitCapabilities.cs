using System.Text.Json;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.Core.Units;

public sealed record UnitCapabilityState(int Version, string[] Skills, string[] Specialties, string[] Tags);
public sealed record UnitCapabilityChoice(string Reference, string Name, string Summary, string? Error);
public sealed record UnitTrait(string Key, string Label, string Specialty, string[] Skills, string? Tag = null);

public static class UnitCapabilities
{
    public static IReadOnlyList<UnitTrait> Traits { get; } = [
        new("resolute", "坚定", "_resolute", ["resolute"]), new("militia", "民兵", "_militia", ["militia"]),
        new("reservist", "预备役", "_reservist", ["reserviste"]), new("security", "警戒", "_security", ["security"]),
        new("gsr", "地面监视雷达", "_gsr", ["GSR"]), new("cbr", "反炮兵雷达", "_cbr", ["Counter_Battery_Radar"]),
        new("electronic_warfare", "雷达防空干扰", "_electronic_warfare", ["electronic_warfare"]),
        new("eo_dazzler", "光电干扰", "_eo_dazzler", ["eo_dazzler"]),
        new("choc", "突击", "_choc", ["Choc", "Choc_feedback"]), new("mp", "宪兵", "_mp", ["MilitaryPolice", "MilitaryPolice_feedback"]),
        new("sniper", "狙击", "_sniper", ["sniper", "sniper_no_snipe", "sniper_feedback"]),
        new("jammer", "地面干扰", "_jammer", ["jammer", "jammer_arty", "jammer_gsr"]),
        new("instructor_inf", "教官 · 步兵", "_instructor", ["Instructor_INF"]), new("instructor_tnk", "教官 · 坦克", "_instructor", ["Instructor_TNK"]),
        new("ifv_provider", "步战车协同 · 提供支援", "_ifv", ["IFV"]), new("ifv_receiver", "步战车协同 · 接受支援", "_ifv", ["IFV_feedback"], "Infanterie_IFV"),
        new("sigint", "信号情报", "_singint", ["sigint_close", "sigint_far", "sigint_feedback"])
    ];
    public static UnitCapabilityState Read(DraftOperation op) => JsonSerializer.Deserialize<UnitCapabilityState>(op.TargetRaw) is { Version: 1, Skills: not null, Specialties: not null, Tags: not null } s ? s : throw new InvalidDataException("能力草稿版本无效");
    public static UnitCapabilityState FromBody(string body) => new(1, ReadList(body, "TCapaciteModuleDescriptor", "DefaultSkillList", false), ReadList(body, "TUnitUIModuleDescriptor", "SpecialtiesList", true), ReadList(body, "TTagsModuleDescriptor", "TagSet", true));
    public static string[] ReadList(string body, string type, string field, bool strings)
    {
        var doc = new NdfSyntaxDocument(body); var modules = doc.FindConstructors(type);
        if (modules.Count > 1) throw new InvalidDataException("模块不唯一：" + type);
        if (modules.Count == 0) return [];
        var lists = doc.FindDirectAssignments(modules[0], field);
        if (lists.Count > 1) throw new InvalidDataException("字段不唯一：" + field);
        if (lists.Count == 0) return [];
        if (!doc.Raw(lists[0]).StartsWith('[') || !doc.Raw(lists[0]).EndsWith(']')) throw new InvalidDataException("只支持直接数组：" + field);
        var values = doc.ReadArrayElements(lists[0]).Select(doc.Raw).ToArray();
        if (strings && values.Any(v => v.Length < 2 || v[0] is not ('\'' or '"') || v[^1] != v[0])) throw new InvalidDataException("字符串数组结构不支持：" + field);
        return strings ? values.Select(NdfSyntaxDocument.Unquote).ToArray() : values;
    }
    public static IReadOnlyList<UnitCapabilityChoice> Choices(UnitProjectGraph graph, string unitFile)
    {
        return graph.Objects.Where(o => o.TypeName == "TCapaciteDescriptor").Select(o =>
        {
            string reference;
            try { reference = graph.ReferenceTo(unitFile, o); }
            catch (InvalidOperationException ex) { return new UnitCapabilityChoice(o.Name, o.Name, "", ex.Message); }
            string? error = null;
            try { ValidateReference(graph, unitFile, reference); } catch (Exception ex) when (ex is InvalidOperationException or IOException) { error = ex.Message; }
            var doc = new NdfSyntaxDocument(graph.Body(o));
            var top = doc.FindConstructors(o.TypeName).Single();
            var facts = doc.EnumerateDirectAssignments(top).Where(a => a.Name != "DescriptorId").Select(a => a.Name + " = " + doc.Raw(a.Value));
            return new UnitCapabilityChoice(reference, o.Name, string.Join("\n", facts), error);
        }).OrderBy(c => c.Name, StringComparer.Ordinal).ToArray();
    }
    public static void ValidateReference(UnitProjectGraph graph, string source, string reference)
    {
        var obj = graph.Resolve(source, reference);
        if (obj?.TypeName != "TCapaciteDescriptor") throw new TransactionValidationException("能力引用缺失、多义或类型不符：" + reference);
        var seen = new HashSet<string>();
        void Visit(NdfObjectInfo current)
        {
            if (!seen.Add(current.RelativeSourceFile + "|" + current.Name)) return;
            var document = new NdfSyntaxDocument(graph.Body(current)); var tokens = document.Tokens;
            var required = new HashSet<int>();
            string[] referenceFields = ["SelfEffect", "TargetEffect", "Conditions", "Condition", "EffectsDescriptors", "Effects", "Effect", "EffectDescriptor", "EffectsPackDescriptor"];
            foreach (var type in tokens.Where((t, i) => i + 1 < tokens.Count && tokens[i + 1].Text == "(").Select(t => t.Text).Distinct())
            foreach (var constructor in document.FindConstructors(type))
            foreach (var assignment in document.EnumerateDirectAssignments(constructor).Where(a => referenceFields.Contains(a.Name)))
            {
                var elements = document.Raw(assignment.Value).StartsWith('[') ? document.ReadArrayElements(assignment.Value) : [assignment.Value];
                foreach (var element in elements.Where(e => e.StartTokenIndex == e.EndTokenIndex))
                    if (document.Raw(element) is not ("nil" or "null")) required.Add(element.StartTokenIndex);
            }
            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (i + 1 < tokens.Count && tokens[i + 1].Text is "=" or "is" or "(") continue;
                if (token.Text[0] is '\'' or '"') continue;
                var leaf = NdfSyntaxDocument.Leaf(token.Text);
                // Effects and conditions are the assignable capacity's required chain;
                // engine enum/constants (stack policy, trigger, alliance) are preserved.
                if (!(required.Contains(i) || leaf.StartsWith("UnitEffect", StringComparison.Ordinal) || leaf.StartsWith("Condition", StringComparison.Ordinal))) continue;
                var dependency = graph.Resolve(current.RelativeSourceFile, token.Text) ?? throw new TransactionValidationException("能力依赖无法解析：" + current.Name + " → " + token.Text);
                Visit(dependency);
            }
        }
        Visit(obj);
    }
    public static string Status(UnitCapabilityState state, UnitTrait trait, UnitProjectGraph? graph = null, string? file = null)
    {
        var leaves = state.Skills.Select(s => graph is null ? NdfSyntaxDocument.Leaf(s) : graph.Resolve(file!, s)?.Name ?? "").ToHashSet(StringComparer.Ordinal);
        var required = trait.Key == "sigint" ? trait.Skills.Take(2) : trait.Skills;
        var any = required.Any(s => leaves.Contains("Capacite_" + s));
        var complete = required.All(s => leaves.Contains("Capacite_" + s)) && (trait.Tag is null || state.Tags.Contains(trait.Tag));
        var icon = state.Specialties.Contains(trait.Specialty);
        if (complete && Traits.Any(t => t.Key != trait.Key && t.Specialty == trait.Specialty && t.Skills.All(s => leaves.Contains("Capacite_" + s)))) return "自定义或无法判断";
        return complete ? icon ? "已配套" : "仅能力" : any ? "配置不完整" : icon ? "仅显示" : "未添加";
    }
    public static UnitCapabilityState Toggle(UnitCapabilityState state, UnitTrait trait, bool add, UnitProjectGraph graph, string file)
    {
        var skills = state.Skills.ToList(); var icons = state.Specialties.ToList(); var tags = state.Tags.ToList();
        var choices = Choices(graph, file);
        foreach (var name in trait.Skills.Select(s => "Capacite_" + s))
        {
            var existing = skills.Where(s => graph.Resolve(file, s)?.Name == name).ToArray();
            if (add)
            {
                var matches = choices.Where(c => c.Name == name).ToArray();
                if (matches.Length != 1 || matches[0].Error is not null) throw new TransactionValidationException("无法配套能力：" + name + " · " + matches.FirstOrDefault()?.Error);
                if (existing.Length == 0) skills.Add(matches[0].Reference);
            }
            else foreach (var item in existing) skills.Remove(item);
        }
        if (add) { if (!icons.Contains(trait.Specialty)) icons.Add(trait.Specialty); if (trait.Tag is not null && !tags.Contains(trait.Tag)) tags.Add(trait.Tag); }
        else
        {
            var shared = Traits.Where(t => t.Key != trait.Key && t.Specialty == trait.Specialty).Any(t => t.Skills.Any(s => skills.Any(r => NdfSyntaxDocument.Leaf(r) == "Capacite_" + s)));
            if (!shared) icons.RemoveAll(s => s == trait.Specialty);
            if (trait.Tag is not null) tags.RemoveAll(t => t == trait.Tag);
        }
        return state with { Skills = skills.ToArray(), Specialties = icons.ToArray(), Tags = tags.ToArray() };
    }
    public static DraftOperation Operation(UnitRecord unit, UnitCapabilityState state, string? baseline = null)
    {
        var raw = baseline ?? UnitCreation.Source(unit); var payload = JsonSerializer.Serialize(state);
        return new(DraftOperation.CreateId(DraftTargetKind.UnitCapabilities, unit.Source.RelativeSourceFile, unit.Name, "unit.capabilities"), "capabilities:" + unit.Name, DraftTargetKind.UnitCapabilities, "units", unit.Source.RelativeSourceFile, unit.Name, unit.Source.TypeName, "unit.capabilities", "DefaultSkillList / SpecialtiesList", "UnitCapabilities", raw, raw, payload, payload, "特性与实际能力 · " + unit.DisplayName, unit.NameToken, false, DateTimeOffset.UtcNow);
    }
    public static ResolvedDraftOperation Resolve(UnitWorkspaceData data, DraftOperation op)
    {
        try
        {
            _ = Read(op); var unit = data.Units.Single(u => u.Name == op.ObjectName);
            if (UnitCreation.Source(unit) != op.BaselineRaw) throw new InvalidDataException("能力编辑基线已变化");
            return new(op, DraftResolutionStatus.Active, "");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or JsonException) { return new(op, DraftResolutionStatus.Conflict, ex.Message); }
    }
    public static string Apply(string body, UnitCapabilityState baseline, UnitCapabilityState state)
    {
        if(!baseline.Tags.Where(t=>t.StartsWith("UNITE_",StringComparison.Ordinal)).SequenceEqual(state.Tags.Where(t=>t.StartsWith("UNITE_",StringComparison.Ordinal))))
            throw new TransactionValidationException("能力配套不能修改单位身份标签");
        body = UpdateList(body, "TCapaciteModuleDescriptor", "DefaultSkillList", baseline.Skills, state.Skills, false, true);
        body = UpdateList(body, "TUnitUIModuleDescriptor", "SpecialtiesList", baseline.Specialties, state.Specialties, true, false);
        return UpdateList(body, "TTagsModuleDescriptor", "TagSet", baseline.Tags, state.Tags, true, false);
    }
    public static void ValidateModuleScope(string body, UnitProjectGraph graph, string source)
    {
        var doc = new NdfSyntaxDocument(body);
        var top = doc.FindConstructors("TEntityDescriptor").Single();
        var arrays = doc.FindDirectAssignments(top, "ModulesDescriptors");
        if (arrays.Count != 1 || !doc.Raw(arrays[0]).StartsWith('[')) throw new TransactionValidationException("ModulesDescriptors不可唯一定位");
        foreach (var element in doc.ReadArrayElements(arrays[0]))
        {
            var raw = doc.Raw(element);
            if (raw.Contains('(')) continue;
            var target = graph.Resolve(source, raw);
            if (target is null && !raw.StartsWith("$/",StringComparison.Ordinal))
            {
                var existing = graph.FindObjects(NdfSyntaxDocument.Leaf(raw)).ToArray();
                if(existing.Length==1)target=existing[0];
            }
            if (target is null) throw new TransactionValidationException("共享模块引用无法确认：" + raw);
            if (target.TypeName == "TCapaciteModuleDescriptor" || new NdfSyntaxDocument(graph.Body(target)).FindConstructors("TCapaciteModuleDescriptor").Count > 0)
                throw new TransactionValidationException("能力模块来自共享引用，不能局部猜写：" + raw);
        }
    }
    public static string UpdateList(string body, string type, string field, string[] baseline, string[] desired, bool strings, bool allowModule)
    {
        if (baseline.SequenceEqual(desired)) return body;
        if (desired.Any(v => string.IsNullOrWhiteSpace(v) || v.Any(c => c is '\r' or '\n' or '"' or '\''))) throw new TransactionValidationException("列表值无效：" + field);
        var doc = new NdfSyntaxDocument(body); var modules = doc.FindConstructors(type);
        if (modules.Count > 1) throw new TransactionValidationException("模块多义：" + type);
        string Raw(string value) => strings ? "'" + value + "'" : value;
        var nl = body.Contains("\r\n") ? "\r\n" : "\n";
        if (modules.Count == 0)
        {
            if (!allowModule) throw new TransactionValidationException("不能定位配套模块：" + type);
            var top = doc.FindConstructors("TEntityDescriptor").Single();
            var array = doc.FindDirectAssignments(top, "ModulesDescriptors").Single();
            if (!doc.Raw(array).StartsWith('[')) throw new TransactionValidationException("ModulesDescriptors不是直接数组");
            var at = doc.Tokens[array.EndTokenIndex].Start;
            return body.Insert(at, (doc.NeedsArraySeparator(array) ? "," : "") + nl + "        " + type + "(" + field + " = [" + string.Join(", ", desired.Select(Raw)) + "])," + nl + "    ");
        }
        var lists = doc.FindDirectAssignments(modules[0], field);
        if (lists.Count == 0)
            return body.Insert(doc.Tokens[modules[0].CloseTokenIndex].Start, nl + "            " + field + " = [" + string.Join(", ", desired.Select(Raw)) + "]" + nl + "        ");
        if (lists.Count != 1 || !doc.Raw(lists[0]).StartsWith('[')) throw new TransactionValidationException("列表不可唯一写入：" + field);
        foreach (var value in baseline.Where(v => !desired.Contains(v)).Distinct())
        {
            while (true)
            {
                doc = new(body); var array = doc.FindDirectAssignments(doc.FindConstructors(type).Single(), field).Single();
                var element = doc.ReadArrayElements(array).FirstOrDefault(e => (strings ? NdfSyntaxDocument.Unquote(doc.Raw(e)) : doc.Raw(e)) == value);
                if (element is null) break;
                body = UnitProjectGraph.Patch(body, [UnitProjectGraph.RemoveElement(doc, array, element, field)]);
            }
        }
        var current = ReadList(body, type, field, strings);
        var additions = desired.Where(v => !baseline.Contains(v) && !current.Contains(v)).Distinct().ToArray();
        if (additions.Length > 0)
        {
            doc = new(body); var array = doc.FindDirectAssignments(doc.FindConstructors(type).Single(), field).Single();
            body = body.Insert(doc.Tokens[array.EndTokenIndex].Start, (doc.NeedsArraySeparator(array) ? ", " : " ") + string.Join(", ", additions.Select(Raw)) + ", ");
        }
        return body;
    }
    public static void Plan(string root, IReadOnlyList<DraftOperation> operations, List<PlannedFileChange> files)
    {
        foreach (var op in operations.Where(o => o.TargetKind == DraftTargetKind.UnitCapabilities))
        {
            var graph = new UnitProjectGraph(root, files); var obj = graph.RequireObject(op.RelativeSourceFile, op.ObjectName);
            var desired = Read(op); var baseline = FromBody(op.BaselineRaw);
            foreach(var field in operations.Where(o=>o.ObjectName==op.ObjectName && o.TargetKind is DraftTargetKind.NdfField or DraftTargetKind.OptionalUnitModule && o.FieldKey is "structure.specialties" or "structure.tags"))
            {
                var tags = field.FieldKey=="structure.tags";
                var target=tags?desired.Tags:desired.Specialties; var initial=tags?baseline.Tags:baseline.Specialties;
                if(initial.SequenceEqual(target))continue;
                var raw=new NdfSyntaxDocument("X is TObject(Value = "+field.TargetRaw+")");
                var array=raw.FindDirectAssignments(raw.FindConstructors("TObject").Single(),"Value").Single();
                var values=raw.ReadArrayElements(array).Select(e=>NdfSyntaxDocument.Unquote(raw.Raw(e))).ToArray();
                if(target.Except(initial).Any(t=>!values.Contains(t)) || initial.Except(target).Any(values.Contains))
                    throw new TransactionValidationException("配套能力与原始标签草稿冲突，请在特性与实际能力中合并："+field.FieldKey);
            }
            if (!desired.Skills.SequenceEqual(baseline.Skills)) ValidateModuleScope(graph.Body(obj), graph, op.RelativeSourceFile);
            foreach (var skill in desired.Skills.Where(s => !baseline.Skills.Contains(s))) ValidateReference(graph, op.RelativeSourceFile, skill);
            var file = graph.FileOf(obj); var body = graph.Body(obj); var changed = Apply(body, baseline, desired);
            UnitProjectGraph.Put(root, file.Path, file.Text.Remove(obj.CharacterOffset, obj.CharacterLength).Insert(obj.CharacterOffset, changed), files, op.Summary);
        }
    }
}
