using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Core.Weapons;

/// <summary>Explicit adapter for anchored instant/continuous vehicle fire. Other presentation families remain diagnosed.</summary>
public static class WeaponStructurePresentation
{
    public sealed record Registration(string File, string Body, string Type, int Start, int Length, string Key);
    private static readonly string[] SupportedOperators = ["DepictionOperator_WeaponInstantFire", "DepictionOperator_WeaponContinuousFire"];
    public static string? Key(string unitBody)
    {
        var doc = new NdfSyntaxDocument(unitBody);
        var values = doc.Tokens.Select(t => t.Text).Distinct().Where(t => t.Contains("ApparenceModuleDescriptor", StringComparison.Ordinal))
            .SelectMany(type => doc.FindConstructors(type)).SelectMany(c => doc.FindDirectAssignments(c, "BlackHoleKey")).ToArray();
        return values.Length switch { 0 => null, 1 => NdfSyntaxDocument.Unquote(doc.Raw(values[0])), _ => throw WeaponStructureSyntax.Error("单位表现键不唯一") };
    }
    public static Registration? Find(UnitProjectGraph graph, string unitBody)
    {
        var key = Key(unitBody); if (key is null) return null;
        var found = new List<Registration>();
        foreach (var file in graph.Files.Values)
        {
            var tokens = file.Syntax.Tokens;
            for (var i = 0; i + 2 < tokens.Count; i++)
            {
                if (tokens[i].Text != "unnamed" || tokens[i + 2].Text != "(") continue;
                var nodes = file.Syntax.FindConstructors(tokens[i + 1].Text, i + 1, tokens.Count - 1);
                if (nodes.Count == 0 || nodes[0].TypeTokenIndex != i + 1) continue;
                var node = nodes[0]; var keys = file.Syntax.FindDirectAssignments(node, "BlackHoleKey");
                if (keys.Count != 1 || NdfSyntaxDocument.Unquote(file.Syntax.Raw(keys[0])) != key) continue;
                if (file.Syntax.FindDirectAssignments(node, "Operators").Count != 1) continue; // e.g. a showroom registration is a separate family.
                var end = tokens[node.CloseTokenIndex].End;
                found.Add(new(file.Path, file.Text[tokens[i].Start..end], node.TypeName, tokens[i].Start, end - tokens[i].Start, key));
            }
        }
        return found.Count switch { 0 => throw WeaponStructureSyntax.Error("未找到该单位的表现注册：" + key), 1 => found[0], _ => throw WeaponStructureSyntax.Error("表现注册不唯一：" + key) };
    }
    private static NdfObjectInfo Operator(UnitProjectGraph graph, Registration registration, string mountBody)
    {
        var keys = WeaponStructureSyntax.Values(mountBody, "TMountedWeaponDescriptor", ["WeaponShootDataPropertyName"]);
        var needles = keys.Values.SelectMany(raw => new NdfSyntaxDocument(raw).Tokens).Where(t => t.Text[0] is '\'' or '"').Select(t => NdfSyntaxDocument.Unquote(t.Text)).ToHashSet();
        var doc = new NdfSyntaxDocument(registration.Body); var root = doc.FindConstructors(registration.Type).Single();
        var operators = WeaponStructureSyntax.Array(doc, WeaponStructureSyntax.Assignment(doc, root, "Operators"));
        var found = new List<NdfObjectInfo>();
        foreach (var item in operators)
        {
            var raw = doc.Raw(item); var target = graph.Resolve(registration.File, raw);
            if (target is null) continue;
            var body = graph.Body(target);
            if (WeaponStructureSyntax.Values(body, target.TypeName, ["WeaponShootDataPropertyName"]).Values
                .SelectMany(s => new NdfSyntaxDocument(s).Tokens).Any(t => needles.Contains(NdfSyntaxDocument.Unquote(t.Text)))) found.Add(target);
        }
        if (found.Count != 1 || !SupportedOperators.Contains(found[0].TypeName)) throw WeaponStructureSyntax.Error("暂不支持此表现操作器；需要可唯一定位的直接挂点瞬发/连续开火配置");
        return found[0];
    }
    public static string RebindReferences(UnitProjectGraph graph, string from, string to, string body)
    {
        var doc = new NdfSyntaxDocument(body); var edits = new List<TextReplacement>();
        foreach (var token in doc.Tokens)
        {
            if (token.Text[0] is '\'' or '"') continue;
            var target = graph.Resolve(from, token.Text);
            if (target is null)
            {
                if (token.Text.StartsWith("$/", StringComparison.Ordinal) || token.Text.StartsWith("~/", StringComparison.Ordinal))
                    throw WeaponStructureSyntax.Error("移植引用无法解析：" + token.Text);
                continue;
            }
            // Declaration identifiers are not uses.
            var index = doc.Tokens.ToList().IndexOf(token);
            if (index + 1 < doc.Tokens.Count && doc.Tokens[index + 1].Text == "is") continue;
            var raw = graph.ReferenceTo(to, target);
            if (raw != token.Text) edits.Add(new(token.Start, token.Text.Length, token.Text, raw, "移植引用"));
        }
        return UnitProjectGraph.Patch(body, edits);
    }
    public static void Validate(UnitProjectGraph graph, WeaponStructureState state)
    {
        if (state.Added.Count == 0 && state.Removed.Count == 0) return;
        var targetReg = Find(graph, state.UnitBaseline);
        var targetSyntax = new WeaponStructureSyntax(state.Weapon.Body);
        if (targetReg is not null)
        {
            var td = new NdfSyntaxDocument(targetReg.Body); var tn = td.FindConstructors(targetReg.Type).Single();
            foreach (var span in WeaponStructureSyntax.Array(td, WeaponStructureSyntax.Assignment(td, tn, "Operators")))
            {
                var op = graph.Resolve(targetReg.File, td.Raw(span));
                if (op is not null && new NdfSyntaxDocument(graph.Body(op)).FindAssignmentsAnywhere("WeaponIndex").Count > 0)
                    throw WeaponStructureSyntax.Error("目标表现包含武器下标依赖，暂不支持增删槽位");
            }
        }
        foreach (var id in state.Removed.Where(_ => !state.Initialize))
        {
            var removed = targetSyntax.Mounts.Single(m => m.Id == id);
            if (targetReg is null)
            {
                if (WeaponStructureSyntax.Values(removed.Body, "TMountedWeaponDescriptor", WeaponStructure.PresentationFields).Count > 0)
                    throw WeaponStructureSyntax.Error("挂载带表现字段，但目标单位没有可适配的表现注册");
                continue;
            }
            _ = Operator(graph, targetReg, removed.Body);
            if (targetSyntax.Turrets.Single(t => t.Id == removed.TurretId).Type == "TTurretInfanterieDescriptor")
                throw WeaponStructureSyntax.Error("步兵手持替代关系暂不支持移植");
        }
        foreach (var add in state.Added)
        {
            var donorMount = new WeaponStructureSyntax(add.Source.Body).Mounts.Single(m => m.Id == add.SourceMount);
            var visual = new WeaponStructureSyntax(state.Weapon.Body).Mounts.Single(m => m.Id == add.VisualMount);
            if (targetReg is null)
            {
                // Synthetic/custom data with no visual contract needs no fabricated animation keys.
                if (WeaponStructureSyntax.Values(donorMount.Body, "TMountedWeaponDescriptor", WeaponStructure.PresentationFields).Count > 0)
                    throw WeaponStructureSyntax.Error("挂载带表现字段，但目标单位没有可适配的表现注册");
                continue;
            }
            var sourceUnit = graph.FindObjects(add.SourceUnit).SingleOrDefault() ?? throw WeaponStructureSyntax.Error("来源单位不存在");
            var donorReg = Find(graph, graph.Body(sourceUnit)) ?? throw WeaponStructureSyntax.Error("来源单位没有表现注册");
            var donorOp = Operator(graph, donorReg, donorMount.Body); var targetOp = Operator(graph, targetReg, visual.Body);
            var anchors = WeaponStructureSyntax.Values(graph.Body(targetOp), targetOp.TypeName, ["Anchors"]);
            if (anchors.Count != 1 || !WeaponStructureSyntax.Values(graph.Body(donorOp), donorOp.TypeName, ["Anchors"]).ContainsKey("Anchors")) throw WeaponStructureSyntax.Error("无法映射现有开火挂点");
            // Handheld switches are not just a rename; the infantry adapter must know their consumers.
            if (new WeaponStructureSyntax(add.Source.Body).Turrets.Single(t => t.Id == donorMount.TurretId).Type == "TTurretInfanterieDescriptor")
                throw WeaponStructureSyntax.Error("步兵手持替代关系暂不支持移植");
            _ = RebindReferences(graph, donorReg.File, targetReg.File, graph.Body(donorOp));
        }
    }
    public static void Capture(UnitProjectGraph graph, WeaponStructureState state)
    {
        ValidateBaselines(graph, state);
        if (state.Added.Count == 0 && state.Removed.Count == 0) return;
        var target = Find(graph, state.UnitBaseline); if (target is null) return;
        void Keep(string file, string name, string body)
        {
            if (!state.PresentationBaselines.Any(b => b.File == file && b.Name == name)) state.PresentationBaselines.Add(new(file, name, body));
        }
        Keep(target.File, target.Key, target.Body);
        foreach (var id in state.Removed.Where(_ => !state.Initialize))
        {
            var mount = new WeaponStructureSyntax(state.Weapon.Body).Mounts.Single(m => m.Id == id);
            var op = Operator(graph, target, mount.Body); Keep(op.RelativeSourceFile, op.Name, graph.Body(op));
        }
        foreach (var add in state.Added)
        {
            var sourceUnit = graph.FindObjects(add.SourceUnit).Single(); Keep(sourceUnit.RelativeSourceFile, sourceUnit.Name, graph.Body(sourceUnit));
            var donor = Find(graph, graph.Body(sourceUnit)) ?? throw WeaponStructureSyntax.Error("来源单位没有表现注册");
            Keep(donor.File, donor.Key, donor.Body);
            var mount = new WeaponStructureSyntax(add.Source.Body).Mounts.Single(m => m.Id == add.SourceMount);
            var visual = new WeaponStructureSyntax(state.Weapon.Body).Mounts.Single(m => m.Id == add.VisualMount);
            foreach (var op in new[] { Operator(graph, donor, mount.Body), Operator(graph, target, visual.Body) }) Keep(op.RelativeSourceFile, op.Name, graph.Body(op));
        }
    }
    public static void ValidateBaselines(UnitProjectGraph graph, WeaponStructureState state)
    {
        foreach (var baseline in state.PresentationBaselines)
            if (!graph.Files.TryGetValue(UnitProjectGraph.Normalize(baseline.File), out var file) || !file.Text.Contains(baseline.Body, StringComparison.Ordinal))
                throw WeaponStructureSyntax.Error("表现来源基线已变化：" + baseline.Name);
    }
    public static void Plan(string root, UnitProjectGraph graph, WeaponStructureState state, NdfObjectInfo unit, List<PlannedFileChange> files)
    {
        if (state.Added.Count == 0) return;
        Validate(graph, state);
        var registration = Find(graph, state.UnitBaseline); if (registration is null) return;
        var regBody = registration.Body; var appended = new List<string>(); var operators = new List<string>(); var actions = new List<string>();
        foreach (var add in state.Added)
        {
            var sourceUnit = graph.FindObjects(add.SourceUnit).Single(); var donorReg = Find(graph, graph.Body(sourceUnit))!;
            var mount = new WeaponStructureSyntax(add.Source.Body).Mounts.Single(m => m.Id == add.SourceMount);
            var targetMount = new WeaponStructureSyntax(state.Weapon.Body).Mounts.Single(m => m.Id == add.VisualMount);
            var donorOp = Operator(graph, donorReg, mount.Body); var targetOp = Operator(graph, registration, targetMount.Body);
            var name = donorOp.Name + "_WLMT_" + state.Id[..10] + "_" + add.Id[4..14] + "_" + unit.Name;
            if (graph.FindObjects(name).Count > 0) throw WeaponStructureSyntax.Error("表现副本名称已占用");
            var originalBody = graph.Body(donorOp); var effect = WeaponStructureSyntax.Values(originalBody, donorOp.TypeName, ["FireEffectTag"])["FireEffectTag"];
            var newEffect = NdfSyntaxDocument.Unquote(effect) + "_WLMT_" + add.Id[4..14];
            var body = RebindReferences(graph, donorReg.File, registration.File, originalBody);
            body = WeaponStructureSyntax.Rename(WeaponStructure.ReplaceStrings(body, add.PropertyMap), name);
            var fields = WeaponStructureSyntax.Values(graph.Body(targetOp), targetOp.TypeName, ["Anchors"]); fields["FireEffectTag"] = "\"" + newEffect + "\"";
            body = WeaponStructureSyntax.Set(body, donorOp.TypeName, fields); appended.Add(body); operators.Add(name);
            var dd = new NdfSyntaxDocument(donorReg.Body); var dn = dd.FindConstructors(donorReg.Type).Single();
            var actionValue = WeaponStructureSyntax.Assignment(dd, dn, "Actions");
            var matches = dd.ReadMapEntries(actionValue).Where(e => NdfSyntaxDocument.Unquote(dd.Raw(e.Key)) == NdfSyntaxDocument.Unquote(effect)).ToArray();
            if (matches.Length != 1) throw WeaponStructureSyntax.Error("开火动作映射无法唯一定位");
            var action = RebindReferences(graph, donorReg.File, registration.File, dd.Raw(matches[0].Value));
            actions.Add("(\"" + newEffect + "\", " + action + ")");
        }
        var key = registration.Key + "_WLMT_" + state.Id[..10] + "_" + unit.Name;
        var rd = new NdfSyntaxDocument(regBody); var rn = rd.FindConstructors(registration.Type).Single();
        regBody = WeaponStructureSyntax.Set(regBody, registration.Type, new Dictionary<string, string> { ["BlackHoleKey"] = "'" + key + "'" });
        rd = new(regBody); rn = rd.FindConstructors(registration.Type).Single();
        regBody = WeaponStructureSyntax.EditList(regBody, WeaponStructureSyntax.Assignment(rd, rn, "Operators"), new HashSet<int>(), new Dictionary<int, string>(), operators);
        rd = new(regBody); rn = rd.FindConstructors(registration.Type).Single(); var av = WeaponStructureSyntax.Assignment(rd, rn, "Actions");
        var open = av.StartTokenIndex;
        if (rd.Tokens[open].Text != "MAP" || rd.Tokens[open + 1].Text != "[") throw WeaponStructureSyntax.Error("暂不支持此表现动作表结构");
        // Locate the matching map bracket without interpreting the trailing + action maps.
        var depth = 0; var close = open + 1;
        for (; close <= av.EndTokenIndex; close++) { if (rd.Tokens[close].Text == "[") depth++; else if (rd.Tokens[close].Text == "]" && --depth == 0) break; }
        regBody = WeaponStructureSyntax.EditList(regBody, new(open + 1, close), new HashSet<int>(), new Dictionary<int, string>(), actions);
        var file = new CandidateTextFiles(root, files); var text = file.Get(registration.File); var nl = file.NewLine(registration.File);
        file.Set(registration.File, text + nl + string.Join(nl + nl, appended) + nl + regBody + nl);
        var unitText = file.Get(unit.RelativeSourceFile); var scan = new NdfTopLevelScanner().Scan(unitText, Path.Combine(root, unit.RelativeSourceFile), "units", root);
        var actual = scan.Objects.Single(o => o.Name == unit.Name); var ub = unitText.Substring(actual.CharacterOffset, actual.CharacterLength); var ud = new NdfSyntaxDocument(ub);
        var spans = ud.Tokens.Select(t => t.Text).Distinct().Where(t => t.Contains("ApparenceModuleDescriptor", StringComparison.Ordinal))
            .SelectMany(t => ud.FindConstructors(t)).SelectMany(c => ud.FindDirectAssignments(c, "BlackHoleKey")).ToArray();
        if (spans.Length != 1) throw WeaponStructureSyntax.Error("单位表现键无法定位");
        var sp = spans[0]; ub = UnitProjectGraph.Patch(ub, [new(ud.StartOffset(sp), ud.Length(sp), ud.Raw(sp), "\"" + key + "\"", "独立武器表现")]);
        file.Set(unit.RelativeSourceFile, unitText[..actual.CharacterOffset] + ub + unitText[(actual.CharacterOffset + actual.CharacterLength)..]);
        file.Complete("新增武器表现配套与局部隔离");
    }
}
