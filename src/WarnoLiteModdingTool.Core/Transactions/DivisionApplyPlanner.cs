using System.Globalization;
using System.Text;
using System.Text.Json;
using WarnoLiteModdingTool.Core.Divisions;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;

namespace WarnoLiteModdingTool.Core.Transactions;

public sealed record DivisionPlannedReplacement(string RelativePath, TextReplacement Replacement, string Summary);

public sealed record DivisionPlanTarget(
    DivisionRecord Division,
    DivisionEditState State,
    IReadOnlyList<string> PackNames);

public sealed record DivisionApplyPlan(
    IReadOnlyList<DivisionPlannedReplacement> Replacements,
    IReadOnlyDictionary<string, IReadOnlyList<string>> NewObjectsByFile,
    IReadOnlyList<string> ValidationMessages,
    IReadOnlyList<DivisionPlanTarget> Targets)
{
    public static DivisionApplyPlan Empty { get; } = new([], new Dictionary<string, IReadOnlyList<string>>(), [], []);
}

public sealed class DivisionApplyPlanner
{
    private readonly NdfTopLevelScanner _scanner = new();

    public DivisionApplyPlan Plan(
        string projectRoot,
        DivisionWorkspaceData? workspace,
        IReadOnlyList<DraftOperation> operations,
        Func<string, TextFileSnapshot> getSnapshot)
    {
        var divisionOperations = operations.Where(item => item.TargetKind == DraftTargetKind.DivisionPlan).ToArray();
        if (divisionOperations.Length == 0)
        {
            return DivisionApplyPlan.Empty;
        }

        if (workspace is null || !workspace.HasCompleteFileSet)
        {
            throw new TransactionValidationException("战术师文件集不完整，无法安全应用 P5 草稿。");
        }

        var replacements = new List<DivisionPlannedReplacement>();
        var targets = new List<DivisionPlanTarget>();
        var newObjects = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var newPackBlocks = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var createdByKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var existingNames = workspace.DeckPacks.Keys.ToHashSet(StringComparer.Ordinal);

        foreach (var operation in divisionOperations)
        {
            var division = workspace.Division(operation.ObjectName)
                ?? throw new TransactionValidationException($"目标师不存在：{operation.ObjectName}");
            if (!DivisionDraftCodec.TryDeserialize(operation.TargetRaw, out var state, out var error))
            {
                throw new TransactionValidationException($"战术师草稿无法解析：{operation.Summary}（{error}）");
            }

            var errors = DivisionStateValidator.Validate(workspace, division, state);
            if (errors.Count > 0)
            {
                throw new TransactionValidationException($"战术师候选无效：{operation.Summary}（{string.Join("；", errors.Take(5))}）");
            }

            PlanDivisionFields(division, state, replacements);
            if (!Same(division.Baseline.UnitRules, state.UnitRules))
            {
                Add(replacements, division.UnitRuleList, FormatRules(state.UnitRules), $"{division.DisplayName} · 更新单位池规则");
            }

            if (!Same(division.Baseline.CostCurves, state.CostCurves))
            {
                Add(replacements, division.CostMatrix, FormatCostMatrix(state.CostCurves), $"{division.DisplayName} · 更新激活费用曲线");
            }

            var packNames = new List<string>();
            foreach (var desired in state.DefaultDeck)
            {
                var packName = ResolvePackName(
                    desired,
                    workspace,
                    existingNames,
                    createdByKey,
                    division,
                    newObjects,
                    newPackBlocks);
                packNames.Add(packName);
            }

            var baselineNames = division.Baseline.DefaultDeck.Select(item => item.OriginalPackName ?? string.Empty).ToArray();
            if (!baselineNames.SequenceEqual(packNames, StringComparer.Ordinal))
            {
                Add(replacements, division.DeckPackList, FormatDeckPackList(packNames), $"{division.DisplayName} · 更新默认 Deck");
            }

            targets.Add(new DivisionPlanTarget(division, state, packNames));
        }

        foreach (var pair in newPackBlocks)
        {
            var snapshot = getSnapshot(pair.Key);
            var separator = snapshot.Text.Length == 0 || snapshot.Text.EndsWith('\n') || snapshot.Text.EndsWith('\r')
                ? snapshot.NewLine
                : snapshot.NewLine + snapshot.NewLine;
            var target = separator + string.Join(snapshot.NewLine + snapshot.NewLine, pair.Value) + snapshot.NewLine;
            target = target.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", snapshot.NewLine, StringComparison.Ordinal);
            replacements.Add(new DivisionPlannedReplacement(
                pair.Key,
                new TextReplacement(snapshot.Text.Length, 0, string.Empty, target, "追加 P5 DeckPackDescriptor"),
                $"新增 {pair.Value.Count} 个 DeckPackDescriptor"));
        }

        var validation = targets.Select(target =>
        {
            var points = DivisionStateValidator.CalculateActivationPoints(workspace, target.State);
            return $"{target.Division.DisplayName}：UnitRule 唯一，默认 Deck {target.PackNames.Count} 项 / {points} 激活点（上限 {target.State.MaxActivationPoints}）";
        }).ToArray();

        return new DivisionApplyPlan(
            replacements,
            newObjects.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase),
            validation,
            targets);
    }

    public void ValidateCandidates(
        DivisionWorkspaceData? workspace,
        DivisionApplyPlan plan,
        IReadOnlyList<PlannedFileChange> files)
    {
        if (plan.Targets.Count == 0)
        {
            return;
        }

        if (workspace is null)
        {
            throw new TransactionValidationException("P5 候选校验缺少战术师工作区。");
        }

        var candidates = files.Where(item => item.Kind == FormalTextFileKind.Ndf)
            .ToDictionary(item => Normalize(item.RelativePath), item => new UTF8Encoding(false, true).GetString(item.CandidateBytes), StringComparer.OrdinalIgnoreCase);
        var packNames = workspace.DeckPacks.Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var names in plan.NewObjectsByFile.Values)
        {
            packNames.UnionWith(names);
        }

        foreach (var target in plan.Targets)
        {
            var deckText = CandidateOrCurrent(target.Division.DeckPackList, candidates, workspace.ProjectRoot);
            var deck = FindObject(deckText, target.Division.DeckPackList.RelativeSourceFile, target.Division.DefaultDeckName, "TDeckDescriptor");
            var deckDocument = new NdfSyntaxDocument(deckText, deck.CharacterOffset, deck.CharacterLength);
            var deckConstructor = deckDocument.FindConstructors("TDeckDescriptor").Single();
            var deckList = deckDocument.FindDirectAssignments(deckConstructor, "DeckPackList").Single();
            var actualPacks = deckDocument.ReadArrayElements(deckList).Select(deckDocument.Raw).Select(NdfSyntaxDocument.Leaf).ToArray();
            if (!actualPacks.SequenceEqual(target.PackNames, StringComparer.Ordinal) || actualPacks.Any(name => !packNames.Contains(name)))
            {
                throw new TransactionValidationException($"候选默认 Deck 的 Pack 顺序或引用闭包无效：{target.Division.DisplayName}");
            }

            for (var index = 0; index < target.PackNames.Count; index++)
            {
                var name = target.PackNames[index];
                var desired = target.State.DefaultDeck[index];
                if (workspace.DeckPacks.TryGetValue(name, out var existing))
                {
                    if (!existing.Matches(desired))
                    {
                        throw new TransactionValidationException($"候选默认 Deck 的既有 Pack 语义不匹配：{name}");
                    }

                    continue;
                }

                var source = plan.NewObjectsByFile.Single(pair => pair.Value.Contains(name, StringComparer.Ordinal)).Key;
                var packText = candidates[Normalize(source)];
                if (!ReadCandidatePack(packText, source, name).Matches(desired))
                {
                    throw new TransactionValidationException($"候选新 DeckPack 的语义不匹配：{name}");
                }
            }

            var ruleText = CandidateOrCurrent(target.Division.UnitRuleList, candidates, workspace.ProjectRoot);
            var rule = FindObject(ruleText, target.Division.UnitRuleList.RelativeSourceFile, target.Division.DivisionRuleName, "TDeckDivisionRule");
            var ruleDocument = new NdfSyntaxDocument(ruleText, rule.CharacterOffset, rule.CharacterLength);
            var units = ruleDocument.FindConstructors("TDeckUniteRule")
                .Select(constructor => ruleDocument.FindDirectAssignments(constructor, "UnitDescriptor"))
                .Select(values => values.Count == 1 ? NdfSyntaxDocument.Leaf(ruleDocument.Raw(values[0])) : string.Empty)
                .ToArray();
            var expectedUnits = target.State.UnitRules.Select(item => item.Unit).ToArray();
            if (units.Any(string.IsNullOrWhiteSpace) || units.Distinct(StringComparer.Ordinal).Count() != units.Length ||
                !units.SequenceEqual(expectedUnits, StringComparer.Ordinal))
            {
                throw new TransactionValidationException($"候选 DivisionRule 的 Unit 唯一性或顺序无效：{target.Division.DisplayName}");
            }

            var matrixText = CandidateOrCurrent(target.Division.CostMatrix, candidates, workspace.ProjectRoot);
            var matrixDocument = new NdfSyntaxDocument(matrixText);
            if (matrixDocument.FindNamedMaps("MAP").Count(item => item.Name == target.Division.CostMatrixName) != 1)
            {
                throw new TransactionValidationException($"候选费用矩阵不存在或不唯一：{target.Division.CostMatrixName}");
            }
        }
    }

    private static void PlanDivisionFields(
        DivisionRecord division,
        DivisionEditState state,
        ICollection<DivisionPlannedReplacement> replacements)
    {
        var baseline = division.Baseline;
        ReplaceIfChanged("MaxActivationPoints", baseline.MaxActivationPoints, state.MaxActivationPoints, value => value.ToString(CultureInfo.InvariantCulture));
        ReplaceIfChanged("InterfaceOrder", baseline.InterfaceOrder, state.InterfaceOrder, FormatDecimal);
        ReplaceIfChanged("DivisionCoalition", baseline.Coalition, state.Coalition, value => $"TWargameCoalition/{value}");
        ReplaceIfChanged("DivisionTags", baseline.Tags, state.Tags, FormatStringList);
        ReplaceIfChanged("CountryId", baseline.CountryId, state.CountryId, value => QuoteLike(division.DivisionFields["CountryId"].RawValue, value));
        ReplaceIfChanged("TypeToken", baseline.TypeToken, state.TypeToken, value => QuoteLike(division.DivisionFields["TypeToken"].RawValue, value));
        ReplaceIfChanged("StandoutUnits", baseline.StandoutUnits, state.StandoutUnits, FormatUnitList);

        void ReplaceIfChanged<T>(string field, T oldValue, T newValue, Func<T, string> format)
        {
            if (Same(oldValue, newValue))
            {
                return;
            }

            Add(replacements, division.DivisionFields[field], format(newValue), $"{division.DisplayName} · {field}");
        }
    }

    private static string ResolvePackName(
        DivisionDeckPackState desired,
        DivisionWorkspaceData workspace,
        ISet<string> existingNames,
        IDictionary<string, string> createdByKey,
        DivisionRecord division,
        IDictionary<string, List<string>> newObjects,
        IDictionary<string, List<string>> newPackBlocks)
    {
        if (!string.IsNullOrWhiteSpace(desired.OriginalPackName) &&
            workspace.DeckPacks.TryGetValue(desired.OriginalPackName, out var original) && original.Matches(desired))
        {
            return original.Name;
        }

        var reusable = workspace.DeckPacks.Values
            .Where(item => item.Matches(desired))
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        if (reusable is not null)
        {
            return reusable.Name;
        }

        var key = $"{desired.Unit}|{desired.Transport}|{desired.Xp}|{desired.Number}";
        if (createdByKey.TryGetValue(key, out var created))
        {
            return created;
        }

        var relativePath = Normalize(workspace.DeckPacks.Values.FirstOrDefault()?.Source.RelativeSourceFile
            ?? Path.Combine(Path.GetDirectoryName(division.DeckPackList.RelativeSourceFile) ?? string.Empty, "DeckPacks.ndf"));
        var counter = 1;
        string name;
        do
        {
            name = $"Descriptor_Deck_Pack_WLMT_{counter:0000}";
            counter++;
        }
        while (existingNames.Contains(name));
        existingNames.Add(name);
        createdByKey[key] = name;
        if (!newObjects.TryGetValue(relativePath, out var names))
        {
            names = [];
            newObjects[relativePath] = names;
        }

        names.Add(name);
        if (!newPackBlocks.TryGetValue(relativePath, out var blocks))
        {
            blocks = [];
            newPackBlocks[relativePath] = blocks;
        }

        blocks.Add(FormatPack(name, desired));
        return name;
    }

    private static string FormatPack(string name, DivisionDeckPackState pack)
    {
        var lines = new List<string>
        {
            $"{name} is DeckPackDescriptor",
            "("
        };
        if (pack.Xp != 0)
        {
            lines.Add($"    Xp = {pack.Xp}");
        }

        lines.Add($"    Unit = $/GFX/Unit/{pack.Unit}");
        if (!string.IsNullOrWhiteSpace(pack.Transport))
        {
            lines.Add($"    Transport = $/GFX/Unit/{pack.Transport}");
        }

        lines.Add($"    Number = {pack.Number}");
        lines.Add(")");
        return string.Join("\n", lines);
    }

    public static string FormatRules(IReadOnlyList<DivisionUnitRuleState> rules)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[");
        foreach (var rule in rules)
        {
            builder.AppendLine("        TDeckUniteRule");
            builder.AppendLine("        (");
            builder.AppendLine($"            UnitDescriptor = $/GFX/Unit/{rule.Unit}");
            builder.AppendLine($"            AvailableWithoutTransport = {(rule.AvailableWithoutTransport ? "True" : "False")}");
            if (rule.AvailableTransports.Count > 0)
            {
                builder.AppendLine($"            AvailableTransportList = [ {string.Join(", ", rule.AvailableTransports.Select(item => $"$/GFX/Unit/{item}"))} ]");
            }

            builder.AppendLine($"            MaxPackNumber = {rule.MaxPackNumber}");
            builder.AppendLine($"            NumberOfUnitInPack = {rule.NumberOfUnitInPack}");
            builder.AppendLine($"            NumberOfUnitInPackXPMultiplier = [{string.Join(", ", rule.XpMultipliers.Select(FormatDecimal))}]");
            builder.AppendLine("        ),");
        }

        builder.Append("    ]");
        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string FormatDeckPackList(IReadOnlyList<string> names)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[");
        foreach (var name in names)
        {
            builder.AppendLine($"        ~/{name},");
        }

        builder.Append("    ]");
        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string FormatCostMatrix(IReadOnlyList<DivisionCostCurveState> curves)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[");
        foreach (var curve in curves)
        {
            builder.AppendLine($"    (Factory/{NdfSyntaxDocument.Leaf(curve.Category)}, [{string.Join(", ", curve.Costs)}]),");
        }

        builder.Append(']');
        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string FormatStringList(IReadOnlyList<string> values) =>
        $"[{string.Join(", ", values.Select(value => $"'{value.Replace("'", "\\'", StringComparison.Ordinal)}'"))}]";

    private static string FormatUnitList(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return "[]";
        }

        return "[\n" + string.Join("\n", values.Select(value => $"        $/GFX/Unit/{value},")) + "\n    ]";
    }

    private static string QuoteLike(string raw, string value)
    {
        var quote = raw.Length >= 2 && raw[0] is '\'' or '"' ? raw[0] : '"';
        return $"{quote}{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(quote.ToString(), $"\\{quote}", StringComparison.Ordinal)}{quote}";
    }

    private static string FormatDecimal(double value)
    {
        var formatted = value.ToString("0.##########", CultureInfo.InvariantCulture);
        return formatted.Contains('.') ? formatted : formatted + ".0";
    }

    private static void Add(
        ICollection<DivisionPlannedReplacement> replacements,
        DivisionTextLocation location,
        string target,
        string summary)
    {
        var normalizedTarget = NormalizeNewLines(target, location.RawValue);
        replacements.Add(new DivisionPlannedReplacement(
            Normalize(location.RelativeSourceFile),
            new TextReplacement(location.CharacterOffset, location.CharacterLength, location.RawValue, normalizedTarget, summary),
            summary));
    }

    private static string NormalizeNewLines(string target, string baseline)
    {
        var newLine = baseline.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return target.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", newLine, StringComparison.Ordinal);
    }

    private NdfObjectInfo FindObject(string text, string relativePath, string name, string type)
    {
        var scan = _scanner.Scan(text, relativePath, "divisions");
        if (scan.Diagnostics.Any(item => item.Severity == NdfDiagnosticSeverity.Error))
        {
            throw new TransactionValidationException($"候选 NDF 结构无效：{relativePath}");
        }

        return scan.Objects.Single(item => item.Name == name && item.TypeName == type);
    }

    private DeckPackRecord ReadCandidatePack(string text, string relativePath, string name)
    {
        var descriptor = FindObject(text, relativePath, name, "DeckPackDescriptor");
        var document = new NdfSyntaxDocument(text, descriptor.CharacterOffset, descriptor.CharacterLength);
        var constructor = document.FindConstructors("DeckPackDescriptor").Single();
        var unit = NdfSyntaxDocument.Leaf(document.Raw(document.FindDirectAssignments(constructor, "Unit").Single()));
        var transportFields = document.FindDirectAssignments(constructor, "Transport");
        var xpFields = document.FindDirectAssignments(constructor, "Xp");
        var number = int.Parse(document.Raw(document.FindDirectAssignments(constructor, "Number").Single()), CultureInfo.InvariantCulture);
        var xp = xpFields.Count == 0 ? 0 : int.Parse(document.Raw(xpFields.Single()), CultureInfo.InvariantCulture);
        var transport = transportFields.Count == 0 ? null : NdfSyntaxDocument.Leaf(document.Raw(transportFields.Single()));
        return new DeckPackRecord(descriptor, xp, unit, transport, number);
    }

    private static string CandidateOrCurrent(
        DivisionTextLocation location,
        IReadOnlyDictionary<string, string> candidates,
        string projectRoot)
    {
        var relative = Normalize(location.RelativeSourceFile);
        return candidates.TryGetValue(relative, out var candidate)
            ? candidate
            : File.ReadAllText(TextFileSnapshot.ResolveInsideRoot(projectRoot, relative));
    }

    private static bool Same<T>(T left, T right) =>
        string.Equals(JsonSerializer.Serialize(left), JsonSerializer.Serialize(right), StringComparison.Ordinal);

    private static string Normalize(string path) => path.Replace('\\', '/');
}
