using System.Globalization;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
using static WarnoLiteModdingTool.Core.Strategic.StrategicSyntax;

namespace WarnoLiteModdingTool.Core.Strategic;

public sealed class StrategicPlanner
{
    public static ResolvedDraftOperation Resolve(StrategicWorkspace? workspace, DraftOperation operation)
    {
        try
        {
            var record = workspace?.Records.SingleOrDefault(r => r.Id == operation.ObjectName);
            if (workspace is null || record is null) throw new InvalidDataException("战略目标不可用");
            if (operation.Module != "strategic" || operation.FieldKey != "strategic.plan" ||
                operation.RelativeSourceFile != record.Deck.Info.RelativeSourceFile || operation.ObjectType != record.Deck.Info.TypeName ||
                operation.Id != StrategicCodec.Operation(record, record.Baseline).Id ||
                operation.BaselineValue != StrategicCodec.Serialize(record.Baseline) || operation.BaselineRaw != operation.BaselineValue ||
                operation.TargetRaw != operation.TargetValue)
                throw new InvalidDataException("战略草稿身份或基线已变化，请重新编辑");
            Validate(workspace, record, StrategicCodec.Deserialize(operation.TargetValue));
            return new(operation, DraftResolutionStatus.Active, "");
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
        { return new(operation, DraftResolutionStatus.Conflict, ex.Message); }
    }

    public static void Validate(StrategicWorkspace workspace, StrategicRecord record, StrategicState state)
    {
        var rosterChanged = System.Text.Json.JsonSerializer.Serialize(state.Companies) != System.Text.Json.JsonSerializer.Serialize(record.Baseline.Companies);
        if (record.Error is not null && rosterChanged) throw new InvalidDataException(record.Error);
        var slots = state.Companies.SelectMany(c=>c.Groups).SelectMany(g=>g.Slots).ToArray();
        if (slots.Any(s=>s.Start.HasValue))
        {
            var size = slots.Sum(s=>(long)s.Count); if(size < 0 || size > 1000000) throw new InvalidDataException("索引数量无效");
            var occupied = new bool[(int)size]; var cursor = 0;
            foreach(var slot in slots) { var start=slot.Start ?? cursor; if(start<0 || slot.Count<=0 || (long)start+slot.Count>size)throw new InvalidDataException("PackIndex 越界"); for(var i=start;i<start+slot.Count;i++){if(occupied[i])throw new InvalidDataException("PackIndex 重叠"); occupied[i]=true;} cursor=start+slot.Count; }
            if(occupied.Any(x=>!x))throw new InvalidDataException("存在未编组槽位");
        }
        var units = workspace.Units.Units.ToDictionary(u => u.Name, StringComparer.Ordinal);
        var baselineSlots = record.Baseline.Companies.SelectMany(c => c.Groups).SelectMany(g => g.Slots).ToDictionary(s => s.Id);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        foreach (var company in state.Companies)
        {
            CheckId(company.Id); CheckName(company.Name);
            foreach (var group in company.Groups)
            {
                CheckId(group.Id); CheckName(group.Name);
                foreach (var slot in group.Slots)
                {
                    CheckId(slot.Id);
                    if (rosterChanged && (!units.ContainsKey(slot.Unit) || slot.Xp < 0 || slot.Xp > 3 || slot.Count <= 0))
                        throw new InvalidDataException("单位必须存在，老练度为 0–3，数量必须大于 0");
                    if (rosterChanged && slot.Transport.Length > 0 && (!units.TryGetValue(slot.Transport, out var transport) ||
                        (!transport.HasUniqueTransporterModule && baselineSlots.GetValueOrDefault(slot.Id)?.Transport != slot.Transport)))
                        throw new InvalidDataException("新增运输必须具备可识别运输模块");
                    if (slot.Pack.Length > 0 && !workspace.Packs.ContainsKey(slot.Pack)) throw new InvalidDataException("原 Pack 不存在");
                    total += slot.Count;
                }
            }
        }
        if (total > int.MaxValue) throw new InvalidDataException("编制数量超出索引范围");
        CheckName(state.Name);
        if (!state.PawnValues.Keys.Order().SequenceEqual(record.Baseline.PawnValues.Keys.Order()))
            throw new InvalidDataException("棋子字段集已变化");
        foreach (var pair in state.PawnValues)
        {
            var field = StrategicFields.All.Single(f => f.Key == pair.Key);
            if (pair.Value == record.Baseline.PawnValues[pair.Key]) continue;
            if (field.Kind == "choice" && workspace.Choices.GetValueOrDefault(field.Key)?.Contains(pair.Value) != true)
                throw new InvalidDataException($"{field.Label} 必须选择当前 Mod 已有值");
            if (field.Kind == "bool" && pair.Value is not ("True" or "False")) throw new InvalidDataException("布尔值无效");
            if (field.Kind is "number" or "radius" &&
                (!double.TryParse(pair.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || value < (field.Kind == "radius" ? -1 : 0)))
                throw new InvalidDataException($"{field.Label} 数值无效");
        }
        void CheckId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) throw new InvalidDataException("编组身份为空或重复");
        }
        static void CheckName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Any(c => char.IsControl(c))) throw new InvalidDataException("名称不能为空或含控制字符");
        }
    }

    public IReadOnlyList<PlannedFileChange> Plan(StrategicWorkspace workspace, IReadOnlyList<DraftOperation> operations,
        IReadOnlyList<PlannedFileChange> existing)
    {
        var snapshots = new Dictionary<string, TextFileSnapshot>(StringComparer.OrdinalIgnoreCase);
        var patches = new Dictionary<string, List<TextReplacement>>(StringComparer.OrdinalIgnoreCase);
        var additions = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var csvTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var objectNames = workspace.Packs.Keys.Concat(workspace.Decks.Keys).Concat(workspace.CombatGroups.Keys).ToHashSet(StringComparer.Ordinal);
        var generatedPacks = new Dictionary<string, string>(StringComparer.Ordinal);
        var usedTokens = workspace.Names.Values.SelectMany(n => n.Keys).ToHashSet(StringComparer.Ordinal);
        var summaries = new List<string>();
        foreach (var operation in operations.Where(o => o.TargetKind == DraftTargetKind.StrategicPlan))
        {
            var resolution = Resolve(workspace, operation);
            if (resolution.Status != DraftResolutionStatus.Active) throw new TransactionValidationException(resolution.Reason);
            var record = workspace.Records.Single(r => r.Id == operation.ObjectName);
            var state = StrategicCodec.Deserialize(operation.TargetValue);
            summaries.Add(operation.Summary);
            var deckText = record.Deck.Text;
            var pawnText = record.Pawn?.Text;
            var rosterChanged = System.Text.Json.JsonSerializer.Serialize(state.Companies) != System.Text.Json.JsonSerializer.Serialize(record.Baseline.Companies);
            if (rosterChanged)
            {
                var deckPacks = Enumerable.Repeat("", state.Companies.Sum(c=>c.Groups.Sum(g=>g.Slots.Sum(s=>s.Count)))).ToList();
                var cursor = 0;
                var combatGroups = new List<string>();
                var groupFile = StrategicLoader.DirectoryPath + "StrategicCombatGroups.ndf";
                var nl = Snapshot(groupFile).NewLine;
                foreach (var company in state.Companies)
                {
                    var originalCompany = record.Baseline.Companies.FirstOrDefault(c => c.Id == company.Id);
                    var text = record.Templates.GetValueOrDefault(company.Id) ?? $"TDeckCombatGroupDescriptor{nl}({nl}    SmartGroupList = []{nl})";
                    var groups = new List<string>();
                    foreach (var group in company.Groups)
                    {
                        var originalGroup = record.Baseline.Companies.SelectMany(c => c.Groups).FirstOrDefault(g => g.Id == group.Id);
                        var smart = record.Templates.GetValueOrDefault(group.Id) ?? $"TDeckSmartGroupDescriptor{nl}({nl}    PackIndexUnitNumberList = []{nl})";
                        var tuples = new List<string>();
                        foreach (var slot in group.Slots)
                        {
                            var packName = PackName(slot);
                            var start = slot.Start ?? cursor;
                            tuples.Add($"({start},{slot.Count})");
                            for(var i = start; i < start + slot.Count; i++) deckPacks[i] = "~/" + packName;
                            cursor = start + slot.Count;
                        }
                        // Keep the original tuple bytes if values did not change.
                        smart = SetArrayIfChanged(smart, "TDeckSmartGroupDescriptor", "PackIndexUnitNumberList", tuples, nl);
                        if (originalGroup is null || originalGroup.IsHQ != group.IsHQ) smart = Set(smart, "TDeckSmartGroupDescriptor", "IsHQ", group.IsHQ ? "True" : "False", true, nl);
                        if (originalGroup is null || originalGroup.Name != group.Name) smart = Set(smart, "TDeckSmartGroupDescriptor", "Name", Quote(AddName("PLATOONS", group.Name)), true, nl);
                        groups.Add(smart);
                    }
                    text = SetArrayIfChanged(text, "TDeckCombatGroupDescriptor", "SmartGroupList", groups, nl);
                    if (originalCompany is null || originalCompany.IsHQ != company.IsHQ) text = Set(text, "TDeckCombatGroupDescriptor", "IsHQ", company.IsHQ ? "True" : "False", true, nl);
                    if (originalCompany is null || originalCompany.Name != company.Name) text = Set(text, "TDeckCombatGroupDescriptor", "Name", Quote(AddName("COMPANIES", company.Name)), true, nl);
                    string name;
                    if (originalCompany is null)
                    {
                        name = NewObject("Descriptor_CombatGroup_WLMT_");
                        Append(groupFile, name + " is " + text);
                    }
                    else if (text != record.Templates[company.Id] &&
                        (workspace.GroupUsers.GetValueOrDefault(company.Id) > 1 || workspace.DeckUsers.GetValueOrDefault(record.Deck.Info.Name) > 1))
                    {
                        name = NewObject("Descriptor_CombatGroup_WLMT_");
                        Append(groupFile, Rename(text, company.Id, name));
                    }
                    else
                    {
                        name = company.Id;
                        if (text != record.Templates[company.Id]) ReplaceObject(workspace.CombatGroups[company.Id], text);
                    }
                    combatGroups.Add("~/" + name);
                }
                deckText = SetArrayIfChanged(deckText, "TDeckDescriptor", "DeckPackList", deckPacks, Snapshot(record.Deck.Info.RelativeSourceFile).NewLine);
                deckText = SetArrayIfChanged(deckText, "TDeckDescriptor", "DeckCombatGroupList", combatGroups, Snapshot(record.Deck.Info.RelativeSourceFile).NewLine);
                if (workspace.DeckUsers.GetValueOrDefault(record.Deck.Info.Name) > 1)
                {
                    if (pawnText is null) throw new TransactionValidationException("共享 Deck 缺少可隔离棋子");
                    var name = NewObject("Descriptor_Deck_WLMT_");
                    var identifier = "WLMT_" + Guid.NewGuid().ToString("N");
                    deckText = Set(Rename(deckText, record.Deck.Info.Name, name), "TDeckDescriptor", "DeckIdentifier", Quote(identifier));
                    pawnText = Set(pawnText, "TDeckModuleDescriptor", "DeckIdentifier", Quote(identifier));
                    Append(record.Deck.Info.RelativeSourceFile, deckText);
                }
                else if (deckText != record.Deck.Text) ReplaceObject(record.Deck, deckText);
            }
            if (pawnText is not null)
            {
                foreach (var pair in state.PawnValues.Where(p => p.Value != record.Baseline.PawnValues[p.Key]))
                {
                    var field = StrategicFields.All.Single(f => f.Key == pair.Key);
                    if (field.Key == "Movement")
                    {
                        var doc = new NdfSyntaxDocument(pawnText);
                        var span = doc.FindReferences("StrategicMovementDescriptor_").Single().Span;
                        pawnText = SemicolonCsvDocument.ApplyReplacements(pawnText, [new(doc.StartOffset(span), doc.Length(span), doc.Raw(span), pair.Value, "Movement")]);
                    }
                    else pawnText = Set(pawnText, field.Constructor, field.Field, pair.Value);
                }
                if (state.Name != record.Baseline.Name) pawnText = Set(pawnText, "StrategicUIModuleDescriptor", "NameToken", Quote(AddName("UNITS", state.Name)));
                if (pawnText != record.Pawn!.Text) ReplaceObject(record.Pawn, pawnText);
            }
            else if (state.Name != record.Baseline.Name) throw new TransactionValidationException("无战略棋子，无法修改棋子名称");
        }

        var changes = new List<PlannedFileChange>();
        foreach (var path in patches.Keys.Concat(additions.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var snapshot = Snapshot(path);
            if (existing.Any(f => f.RelativePath.Equals(path, StringComparison.OrdinalIgnoreCase))) throw new TransactionValidationException("战略文件与另一模块补丁重叠");
            var candidate = SemicolonCsvDocument.ApplyReplacements(snapshot.Text, patches.GetValueOrDefault(path) ?? []);
            if (additions.TryGetValue(path, out var appended)) candidate += snapshot.NewLine + string.Join(snapshot.NewLine + snapshot.NewLine, appended) + snapshot.NewLine;
            var scan = new NdfTopLevelScanner().Scan(candidate, snapshot.FullPath, "strategic", workspace.Root);
            if (scan.Diagnostics.Any(d => d.Severity == NdfDiagnosticSeverity.Error) || scan.Objects.GroupBy(o => o.Name).Any(g => g.Count() > 1))
                throw new TransactionValidationException("战略候选结构或对象唯一性校验失败");
            changes.Add(UnitApplyPlanner.ToWriteChange(snapshot, candidate, summaries));
        }
        foreach (var pair in csvTexts) changes.Add(UnitApplyPlanner.ToWriteChange(Snapshot(pair.Key, FormalTextFileKind.Csv), pair.Value, summaries));
        VerifyCandidates(workspace, operations, changes);
        return changes;

        TextFileSnapshot Snapshot(string path, FormalTextFileKind kind = FormalTextFileKind.Ndf)
        {
            if (!snapshots.TryGetValue(path, out var snapshot)) snapshots[path] = snapshot = TextFileSnapshot.Load(workspace.Root, path, kind, kind == FormalTextFileKind.Csv);
            return snapshot;
        }
        void ReplaceObject(StrategicSource source, string text)
        {
            var path = source.Info.RelativeSourceFile;
            if (!patches.TryGetValue(path, out var list)) patches[path] = list = [];
            list.Add(new(source.Info.CharacterOffset, source.Info.CharacterLength, source.Text, text, source.Info.Name));
        }
        void Append(string path, string text)
        {
            if (!additions.TryGetValue(path, out var list)) additions[path] = list = [];
            list.Add(text);
        }
        string NewObject(string prefix)
        {
            string name;
            do { name = prefix + Guid.NewGuid().ToString("N"); } while (!objectNames.Add(name));
            return name;
        }
        string AddName(string kind, string name)
        {
            Localisation.VanillaNames.RequireAvailable();
            if (!workspace.CsvPaths.TryGetValue(kind, out var path)) throw new TransactionValidationException($"{kind}.csv 声明缺失或不唯一");
            var snapshot = Snapshot(path, FormalTextFileKind.Csv);
            if (!csvTexts.TryGetValue(path, out var text))
            {
                var prior = existing.FirstOrDefault(f => f.RelativePath.Equals(path, StringComparison.OrdinalIgnoreCase));
                text = prior is null ? snapshot.Text : Decode(prior.CandidateBytes);
            }
            string token;
            do { token = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(); }
            while (!usedTokens.Add(token) || text.Contains(token, StringComparison.Ordinal) || Localisation.VanillaNames.Lookup(kind, token) is not null);
            if (text.Length == 0) text = "\"TOKEN\";\"REFTEXT\"" + snapshot.NewLine;
            if (!text.EndsWith('\n') && !text.EndsWith('\r')) text += snapshot.NewLine;
            csvTexts[path] = text + SemicolonCsvDocument.Quote(token) + ";" + SemicolonCsvDocument.Quote(name) + snapshot.NewLine;
            return token;
        }
        string PackName(StrategicSlot slot)
        {
            if (workspace.Packs.TryGetValue(slot.Pack, out var original) && original.Unit == slot.Unit && original.Transport == slot.Transport && original.Xp == slot.Xp) return slot.Pack;
            // Preserve unknown pack fields when cloning an existing slot.
            var signature = slot.Pack + "|" + slot.Unit + "|" + slot.Transport + "|" + slot.Xp;
            if (generatedPacks.TryGetValue(signature, out var cached)) return cached;
            var nl = Snapshot(StrategicLoader.DirectoryPath + "StrategicPacks.ndf").NewLine;
            var text = original?.Source.Text ?? $"DeckPackDescriptor{nl}({nl}    Unit = $/GFX/Unit/{slot.Unit}{nl})";
            text = Set(text, "DeckPackDescriptor", "Unit", "$/GFX/Unit/" + slot.Unit);
            text = Set(text, "DeckPackDescriptor", "Xp", slot.Xp.ToString(CultureInfo.InvariantCulture), true, nl);
            if (slot.Transport.Length > 0) text = Set(text, "DeckPackDescriptor", "Transport", "$/GFX/Unit/" + slot.Transport, true, nl);
            else if (original?.Transport.Length > 0) text = RemoveAssignment(text, "DeckPackDescriptor", "Transport");
            var name = NewObject("Descriptor_StrategicPack_WLMT_");
            text = original is null ? name + " is " + text : Rename(text, original.Source.Info.Name, name);
            Append(StrategicLoader.DirectoryPath + "StrategicPacks.ndf", text);
            generatedPacks[signature] = name;
            return name;
        }
    }

    private static string Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true);
        return reader.ReadToEnd();
    }
    private static void VerifyCandidates(StrategicWorkspace baseline, IReadOnlyList<DraftOperation> operations, IReadOnlyList<PlannedFileChange> changes)
    {
        var context = new WarnoLiteModdingTool.Core.Projects.ModProjectDetector().Detect(baseline.Root);
        var module = context.Modules.Single(m => m.Key == "strategic");
        var objects = new List<NdfObjectInfo>();
        var overrides = changes.Where(c => c.Kind == FormalTextFileKind.Ndf).ToDictionary(c => c.RelativePath, c => Decode(c.CandidateBytes), StringComparer.OrdinalIgnoreCase);
        foreach (var file in module.SourceFiles)
        {
            var relative = Path.GetRelativePath(baseline.Root, file).Replace('\\', '/');
            var text = overrides.GetValueOrDefault(relative) ?? File.ReadAllText(file);
            objects.AddRange(new NdfTopLevelScanner().Scan(text, file, "strategic", baseline.Root).Objects);
        }
        var candidate = StrategicLoader.Load(context, new(objects, [], context.Modules), baseline.Units, CancellationToken.None, overrides);
        var edits = operations.Where(o => o.TargetKind == DraftTargetKind.StrategicPlan).ToDictionary(o => o.ObjectName);
        foreach (var original in baseline.Records)
        {
            var after = candidate.Records.SingleOrDefault(r => r.Id == original.Id) ?? throw new TransactionValidationException("战略目标在候选中丢失");
            if (!edits.TryGetValue(original.Id, out var operation))
            {
                if (StrategicCodec.Serialize(original.Baseline) != StrategicCodec.Serialize(after.Baseline))
                    throw new TransactionValidationException("未选择的战略目标受到影响：" + original.Id);
                continue;
            }
            var target = StrategicCodec.Deserialize(operation.TargetValue);
            var rosterChanged = Shape(target) != Shape(original.Baseline);
            if (rosterChanged && after.Error is not null) throw new TransactionValidationException("候选编制闭包失败：" + after.Error);
            var expectedSlots = target.Companies.SelectMany(c=>c.Groups).SelectMany(g=>g.Slots).ToArray();
            if (expectedSlots.Any(s=>s.Start.HasValue))
            {
                var actualStarts = new List<int>();
                foreach(var group in after.Baseline.Companies.SelectMany(c=>c.Groups)) { var doc = new NdfSyntaxDocument(after.Templates[group.Id]); var tuples=Field(doc,"TDeckSmartGroupDescriptor","PackIndexUnitNumberList")!; actualStarts.AddRange(doc.ReadMapEntries(tuples).Select(t=>int.Parse(doc.Raw(t.Key),CultureInfo.InvariantCulture))); }
                var cursor=0;
                for(var i=0;i<expectedSlots.Length;i++){var start=expectedSlots[i].Start??cursor;if(actualStarts[i]!=start)throw new TransactionValidationException("PackIndex 回读不一致");cursor=start+expectedSlots[i].Count;}
            }
            if (Shape(target) != Shape(after.Baseline) || !target.PawnValues.OrderBy(p => p.Key).SequenceEqual(after.Baseline.PawnValues.OrderBy(p => p.Key)))
                throw new TransactionValidationException("战略候选回读与目标不一致：" + original.Id + "\n目标=" + Shape(target) + "\n候选=" + Shape(after.Baseline) + "\n" + System.Text.Json.JsonSerializer.Serialize(after.Baseline.PawnValues));
        }
        static string Shape(StrategicState state) => System.Text.Json.JsonSerializer.Serialize(state.Companies.Select(c => new {
            c.IsHQ, Groups = c.Groups.Select(g => new { g.IsHQ, Slots = g.Slots.Select(s => new { s.Unit, s.Transport, s.Xp, s.Count }) }) }));
    }
    private static string Rename(string text, string oldName, string newName)
    {
        var offset = text.IndexOf(oldName, StringComparison.Ordinal);
        if (offset < 0) throw new InvalidDataException("对象名称不匹配");
        return text[..offset] + newName + text[(offset + oldName.Length)..];
    }
    private static string SetArrayIfChanged(string text, string type, string field, IEnumerable<string> values, string nl)
    {
        var desired = values.ToArray();
        var doc = new NdfSyntaxDocument(text);
        var span = Field(doc, type, field) ?? throw new InvalidDataException("缺少 " + field);
        var current = doc.ReadArrayElements(span).Select(doc.Raw).ToArray();
        return current.SequenceEqual(desired, StringComparer.Ordinal) ? text : Set(text, type, field, Array(desired, nl));
    }
    private static string RemoveAssignment(string text, string type, string field)
    {
        var doc = new NdfSyntaxDocument(text);
        var span = Field(doc, type, field) ?? throw new InvalidDataException("缺少 " + field);
        var valueStart = doc.StartOffset(span);
        var start = text.LastIndexOf(field, valueStart, StringComparison.Ordinal);
        if (start < 0 || text[(start + field.Length)..valueStart].Trim() != "=") throw new InvalidDataException("赋值边界不明确");
        return text.Remove(start, valueStart + doc.Length(span) - start);
    }
}
