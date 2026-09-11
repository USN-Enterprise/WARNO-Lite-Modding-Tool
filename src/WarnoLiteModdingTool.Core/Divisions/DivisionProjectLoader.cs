using System.Globalization;
using WarnoLiteModdingTool.Core.Indexing;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Core.Divisions;

public sealed class DivisionProjectLoader
{
    private static readonly string[] RequiredFiles =
    [
        "Divisions.ndf",
        "DivisionRules.ndf",
        "DivisionCostMatrix.ndf",
        "DeckPacks.ndf",
        "Decks.ndf"
    ];

    public Task<DivisionWorkspaceData> LoadAsync(
        ModProjectContext context,
        ProjectIndexResult index,
        UnitWorkspaceData units,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Load(context, index, units, cancellationToken), cancellationToken);

    private static DivisionWorkspaceData Load(
        ModProjectContext context,
        ProjectIndexResult index,
        UnitWorkspaceData units,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<string>();
        var divisionCapability = index.Modules.FirstOrDefault(item => item.Key == "divisions");
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in divisionCapability?.SourceFiles ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                sources[path] = File.ReadAllText(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add($"无法读取 {Path.GetRelativePath(context.Layout.RootPath, path)}：{exception.Message}");
            }
        }

        var availableNames = sources.Keys.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var complete = RequiredFiles.All(availableNames.Contains) &&
            divisionCapability?.Availability != ModuleAvailability.ParseError;
        if (!complete)
        {
            diagnostics.Add("战术师编辑需要 Divisions、DivisionRules、DivisionCostMatrix、DeckPacks 和 Decks 五个兼容文件；当前仅保留可用模块的只读能力。");
        }

        var objects = index.Objects.Where(item => item.ModuleKey == "divisions").ToArray();
        var packs = ReadPacks(objects, sources, diagnostics, cancellationToken);
        var rules = objects.Where(item => item.TypeName == "TDeckDivisionRule")
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var decks = ReadDecks(objects, sources, diagnostics, cancellationToken);
        var matrices = ReadMatrices(sources, context.Layout.RootPath, diagnostics);

        var preliminaries = new List<PreliminaryDivision>();
        var skippedNonTactical = 0;
        foreach (var descriptor in objects.Where(item => item.TypeName == "TDeckDivisionDescriptor"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!sources.TryGetValue(descriptor.SourceFile, out var source))
            {
                continue;
            }

            try
            {
                var document = new NdfSyntaxDocument(source, descriptor.CharacterOffset, descriptor.CharacterLength);
                var constructor = SingleConstructor(document, descriptor);
                var required = new[]
                {
                    "CfgName", "InterfaceOrder", "DivisionCoalition", "DivisionTags", "MaxActivationPoints",
                    "DivisionRule", "CostMatrix", "TypeToken", "CountryId", "StandoutUnits"
                };
                if (required.Any(field => document.FindDirectAssignments(constructor, field).Count != 1))
                {
                    skippedNonTactical++;
                    continue;
                }

                var fields = new Dictionary<string, DivisionTextLocation>(StringComparer.Ordinal);
                var maxActivation = ReadInt(document, constructor, descriptor, "MaxActivationPoints", fields);
                var interfaceOrder = ReadDouble(document, constructor, descriptor, "InterfaceOrder", fields);
                var coalition = NdfSyntaxDocument.Leaf(ReadRaw(document, constructor, descriptor, "DivisionCoalition", fields));
                var tags = ReadStringArray(document, constructor, descriptor, "DivisionTags", fields);
                var country = NdfSyntaxDocument.Unquote(ReadRaw(document, constructor, descriptor, "CountryId", fields));
                var type = NdfSyntaxDocument.Unquote(ReadRaw(document, constructor, descriptor, "TypeToken", fields));
                var standout = ReadReferenceArray(document, constructor, descriptor, "StandoutUnits", fields);
                var ruleName = NdfSyntaxDocument.Leaf(ReadRaw(document, constructor, descriptor, "DivisionRule", fields));
                var matrixName = NdfSyntaxDocument.Leaf(ReadRaw(document, constructor, descriptor, "CostMatrix", fields));
                var cfgName = NdfSyntaxDocument.Unquote(ReadRaw(document, constructor, descriptor, "CfgName", null));
                var nameFields=document.FindDirectAssignments(constructor,"DivisionName");
                var token=nameFields.Count==1?NdfSyntaxDocument.Unquote(document.Raw(nameFields[0])):"";
                var display=units.Localisation.TryResolve(token,out var localName)?localName:WarnoLiteModdingTool.Core.Localisation.VanillaNames.Lookup("UNITS",token);
                preliminaries.Add(new PreliminaryDivision(
                    descriptor,
                    display ?? (cfgName.Length == 0 ? descriptor.DisplayName : NameHumanizer.Humanize(cfgName)),
                    maxActivation,
                    interfaceOrder,
                    coalition,
                    tags,
                    country,
                    type,
                    standout,
                    ruleName,
                    matrixName,
                    fields));
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add($"{descriptor.Name} 无法进入 P5 编辑：{exception.Message}");
            }
        }

        var ruleUses = preliminaries.GroupBy(item => item.RuleName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var matrixUses = preliminaries.GroupBy(item => item.MatrixName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var divisions = new List<DivisionRecord>();
        foreach (var preliminary in preliminaries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reasons = new List<string>();
            if (!complete)
            {
                reasons.Add("战术师文件集不完整或存在解析错误");
            }

            if (!rules.TryGetValue(preliminary.RuleName, out var ruleMatches) || ruleMatches.Length != 1)
            {
                reasons.Add($"DivisionRule 引用不是唯一对象：{preliminary.RuleName}");
            }

            var divisionDecks = decks.Where(item => item.DivisionName == preliminary.Source.Name).ToArray();
            var expectedDeckName = preliminary.Source.Name.Replace("Descriptor_Deck_Division_", "Descriptor_Deck_", StringComparison.Ordinal);
            var deck = divisionDecks.FirstOrDefault(item => item.Source.Name == expectedDeckName)
                ?? (divisionDecks.Length == 1 ? divisionDecks[0] : null);
            if (deck is null)
            {
                reasons.Add($"无法唯一确定现有默认 Deck（候选 {divisionDecks.Length} 个）");
            }

            if (!matrices.TryGetValue(preliminary.MatrixName, out var matrix))
            {
                reasons.Add($"费用矩阵不存在：{preliminary.MatrixName}");
            }

            if (ruleUses.GetValueOrDefault(preliminary.RuleName) > 1)
            {
                reasons.Add("DivisionRule 被多个师共享，P5 不做隐式全局修改");
            }

            if (matrixUses.GetValueOrDefault(preliminary.MatrixName) > 1)
            {
                reasons.Add("费用矩阵被多个师共享，P5 不做隐式全局修改");
            }

            if (ruleMatches is null || ruleMatches.Length != 1 || deck is null || matrix is null)
            {
                continue;
            }

            try
            {
                var (unitRules, ruleLocation) = ReadRules(ruleMatches[0], sources[ruleMatches[0].SourceFile]);
                var packStates = deck.PackNames.Select(name =>
                {
                    if (!packs.TryGetValue(name, out var pack))
                    {
                        reasons.Add($"默认 Deck 引用不存在的 Pack：{name}");
                        return new DivisionDeckPackState(name, string.Empty, null, 0, 0);
                    }

                    return new DivisionDeckPackState(pack.Name, pack.Unit, pack.Transport, pack.Xp, pack.Number);
                }).ToArray();
                var state = new DivisionEditState(
                    preliminary.MaxActivationPoints,
                    preliminary.InterfaceOrder,
                    preliminary.Coalition,
                    preliminary.Tags,
                    preliminary.CountryId,
                    preliminary.TypeToken,
                    preliminary.StandoutUnits,
                    unitRules,
                    packStates,
                    matrix.Curves);
                var canEdit = reasons.Count == 0;
                divisions.Add(new DivisionRecord(
                    preliminary.Source,
                    preliminary.DisplayName,
                    preliminary.RuleName,
                    preliminary.MatrixName,
                    deck.Source.Name,
                    state,
                    preliminary.Fields,
                    ruleLocation,
                    deck.PackListLocation,
                    matrix.Location,
                    canEdit,
                    string.Join("；", reasons)));
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add($"{preliminary.Source.Name} 无法进入 P5 编辑：{exception.Message}");
            }
        }

        return new DivisionWorkspaceData(
            context.Layout.RootPath,
            divisions.OrderBy(item => item.Baseline.InterfaceOrder).ThenBy(item => item.DisplayName).ToArray(),
            packs,
            units,
            complete,
            diagnostics)
        {
            SkippedNonTacticalDescriptorCount = skippedNonTactical
        };
    }

    private static IReadOnlyDictionary<string, DeckPackRecord> ReadPacks(
        IReadOnlyList<NdfObjectInfo> objects,
        IReadOnlyDictionary<string, string> sources,
        ICollection<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, DeckPackRecord>(StringComparer.Ordinal);
        foreach (var descriptor in objects.Where(item => item.TypeName == "DeckPackDescriptor"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!sources.TryGetValue(descriptor.SourceFile, out var source))
            {
                continue;
            }

            try
            {
                var document = new NdfSyntaxDocument(source, descriptor.CharacterOffset, descriptor.CharacterLength);
                var constructor = SingleConstructor(document, descriptor);
                var unit = NdfSyntaxDocument.Leaf(ReadRaw(document, constructor, descriptor, "Unit", null));
                var transportRaw = TryReadRaw(document, constructor, "Transport");
                var xpRaw = TryReadRaw(document, constructor, "Xp");
                var numberRaw = TryReadRaw(document, constructor, "Number") ?? "1";
                var xp = xpRaw is null ? 0 : ParseInt(xpRaw, $"{descriptor.Name}.Xp");
                var number = ParseInt(numberRaw, $"{descriptor.Name}.Number");
                var pack = new DeckPackRecord(
                    descriptor,
                    xp,
                    unit,
                    transportRaw is null ? null : NdfSyntaxDocument.Leaf(transportRaw),
                    number);
                if (!result.TryAdd(pack.Name, pack))
                {
                    diagnostics.Add($"DeckPack 名称重复：{pack.Name}");
                }
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add($"{descriptor.Name} Pack 解析失败：{exception.Message}");
            }
        }

        return result;
    }

    private static IReadOnlyList<DeckRecord> ReadDecks(
        IReadOnlyList<NdfObjectInfo> objects,
        IReadOnlyDictionary<string, string> sources,
        ICollection<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var result = new List<DeckRecord>();
        foreach (var descriptor in objects.Where(item => item.TypeName == "TDeckDescriptor"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!sources.TryGetValue(descriptor.SourceFile, out var source))
            {
                continue;
            }

            try
            {
                var document = new NdfSyntaxDocument(source, descriptor.CharacterOffset, descriptor.CharacterLength);
                var constructor = SingleConstructor(document, descriptor);
                var divisionName = NdfSyntaxDocument.Leaf(ReadRaw(document, constructor, descriptor, "DeckDivision", null));
                var list = SingleAssignment(document, constructor, descriptor, "DeckPackList");
                var names = document.ReadArrayElements(list).Select(document.Raw).Select(NdfSyntaxDocument.Leaf).ToArray();
                result.Add(new DeckRecord(
                    descriptor,
                    divisionName,
                    names,
                    Location(document, list, descriptor, "DeckPackList")));
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add($"{descriptor.Name} Deck 解析失败：{exception.Message}");
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, MatrixRecord> ReadMatrices(
        IReadOnlyDictionary<string, string> sources,
        string projectRoot,
        ICollection<string> diagnostics)
    {
        var result = new Dictionary<string, MatrixRecord>(StringComparer.Ordinal);
        foreach (var pair in sources.Where(item => Path.GetFileName(item.Key).Equals("DivisionCostMatrix.ndf", StringComparison.OrdinalIgnoreCase)))
        {
            var document = new NdfSyntaxDocument(pair.Value);
            foreach (var map in document.FindNamedMaps("MAP"))
            {
                var curves = new List<DivisionCostCurveState>();
                foreach (var entry in document.ReadMapEntries(map.Value))
                {
                    var category = NdfSyntaxDocument.Leaf(document.Raw(entry.Key));
                    var costs = document.ReadArrayElements(entry.Value)
                        .Select(span => ParseInt(document.Raw(span), $"{map.Name}.{category}"))
                        .ToArray();
                    curves.Add(new DivisionCostCurveState(category, costs));
                }

                var location = new DivisionTextLocation(
                    Path.GetRelativePath(projectRoot, pair.Key),
                    document.StartOffset(map.Value),
                    document.Length(map.Value),
                    document.Raw(map.Value),
                    $"{map.Name}.MAP");
                if (!result.TryAdd(map.Name, new MatrixRecord(map.Name, curves, location)))
                {
                    diagnostics.Add($"费用矩阵名称重复：{map.Name}");
                }
            }
        }

        return result;
    }

    private static (IReadOnlyList<DivisionUnitRuleState> Rules, DivisionTextLocation Location) ReadRules(
        NdfObjectInfo descriptor,
        string source)
    {
        var document = new NdfSyntaxDocument(source, descriptor.CharacterOffset, descriptor.CharacterLength);
        var constructor = SingleConstructor(document, descriptor);
        var list = SingleAssignment(document, constructor, descriptor, "UnitRuleList");
        var rules = new List<DivisionUnitRuleState>();
        foreach (var element in document.ReadArrayElements(list))
        {
            var nested = document.FindConstructors("TDeckUniteRule", element);
            if (nested.Count != 1)
            {
                throw new InvalidDataException("UnitRuleList 中存在无法识别的记录");
            }

            var rule = nested[0];
            var unit = NdfSyntaxDocument.Leaf(ReadRaw(document, rule, descriptor, "UnitDescriptor", null));
            var without = ParseBoolean(ReadRaw(document, rule, descriptor, "AvailableWithoutTransport", null), $"{unit}.AvailableWithoutTransport");
            var transportsSpan = TrySingleAssignment(document, rule, "AvailableTransportList");
            var transports = transportsSpan is null
                ? []
                : document.ReadArrayElements(transportsSpan).Select(document.Raw).Select(NdfSyntaxDocument.Leaf).ToArray();
            var max = ParseInt(ReadRaw(document, rule, descriptor, "MaxPackNumber", null), $"{unit}.MaxPackNumber");
            var units = ParseInt(ReadRaw(document, rule, descriptor, "NumberOfUnitInPack", null), $"{unit}.NumberOfUnitInPack");
            var xpSpan = SingleAssignment(document, rule, descriptor, "NumberOfUnitInPackXPMultiplier");
            var xp = document.ReadArrayElements(xpSpan)
                .Select(span => ParseDouble(document.Raw(span), $"{unit}.NumberOfUnitInPackXPMultiplier"))
                .ToArray();
            rules.Add(new DivisionUnitRuleState(unit, without, transports, max, units, xp));
        }

        return (rules, Location(document, list, descriptor, "UnitRuleList"));
    }

    private static NdfConstructorSpan SingleConstructor(NdfSyntaxDocument document, NdfObjectInfo descriptor)
    {
        var matches = document.FindConstructors(descriptor.TypeName);
        return matches.Count == 1
            ? matches[0]
            : throw new InvalidDataException($"{descriptor.TypeName} 构造器数量为 {matches.Count}");
    }

    private static string ReadRaw(
        NdfSyntaxDocument document,
        NdfConstructorSpan constructor,
        NdfObjectInfo descriptor,
        string field,
        IDictionary<string, DivisionTextLocation>? locations)
    {
        var span = SingleAssignment(document, constructor, descriptor, field);
        if (locations is not null)
        {
            locations[field] = Location(document, span, descriptor, field);
        }

        return document.Raw(span);
    }

    private static string? TryReadRaw(NdfSyntaxDocument document, NdfConstructorSpan constructor, string field)
    {
        var span = TrySingleAssignment(document, constructor, field);
        return span is null ? null : document.Raw(span);
    }

    private static NdfValueSpan SingleAssignment(
        NdfSyntaxDocument document,
        NdfConstructorSpan constructor,
        NdfObjectInfo descriptor,
        string field)
    {
        var matches = document.FindDirectAssignments(constructor, field);
        return matches.Count == 1
            ? matches[0]
            : throw new InvalidDataException($"{descriptor.Name}.{field} 数量为 {matches.Count}");
    }

    private static NdfValueSpan? TrySingleAssignment(
        NdfSyntaxDocument document,
        NdfConstructorSpan constructor,
        string field)
    {
        var matches = document.FindDirectAssignments(constructor, field);
        return matches.Count == 1 ? matches[0] : null;
    }

    private static DivisionTextLocation Location(
        NdfSyntaxDocument document,
        NdfValueSpan span,
        NdfObjectInfo descriptor,
        string field) =>
        new(
            descriptor.RelativeSourceFile,
            document.StartOffset(span),
            document.Length(span),
            document.Raw(span),
            $"{descriptor.Name}.{field}");

    private static int ReadInt(
        NdfSyntaxDocument document,
        NdfConstructorSpan constructor,
        NdfObjectInfo descriptor,
        string field,
        IDictionary<string, DivisionTextLocation> locations) =>
        ParseInt(ReadRaw(document, constructor, descriptor, field, locations), $"{descriptor.Name}.{field}");

    private static double ReadDouble(
        NdfSyntaxDocument document,
        NdfConstructorSpan constructor,
        NdfObjectInfo descriptor,
        string field,
        IDictionary<string, DivisionTextLocation> locations) =>
        ParseDouble(ReadRaw(document, constructor, descriptor, field, locations), $"{descriptor.Name}.{field}");

    private static IReadOnlyList<string> ReadStringArray(
        NdfSyntaxDocument document,
        NdfConstructorSpan constructor,
        NdfObjectInfo descriptor,
        string field,
        IDictionary<string, DivisionTextLocation> locations)
    {
        _ = ReadRaw(document, constructor, descriptor, field, locations);
        var span = SingleAssignment(document, constructor, descriptor, field);
        return document.ReadArrayElements(span).Select(document.Raw).Select(NdfSyntaxDocument.Unquote).ToArray();
    }

    private static IReadOnlyList<string> ReadReferenceArray(
        NdfSyntaxDocument document,
        NdfConstructorSpan constructor,
        NdfObjectInfo descriptor,
        string field,
        IDictionary<string, DivisionTextLocation> locations)
    {
        _ = ReadRaw(document, constructor, descriptor, field, locations);
        var span = SingleAssignment(document, constructor, descriptor, field);
        return document.ReadArrayElements(span).Select(document.Raw).Select(NdfSyntaxDocument.Leaf).ToArray();
    }

    private static int ParseInt(string raw, string path) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidDataException($"{path} 不是整数");

    private static double ParseDouble(string raw, string path) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? value
            : throw new InvalidDataException($"{path} 不是有限数字");

    private static bool ParseBoolean(string raw, string path) => raw switch
    {
        "True" => true,
        "False" => false,
        _ => throw new InvalidDataException($"{path} 不是 True/False")
    };

    private sealed record PreliminaryDivision(
        NdfObjectInfo Source,
        string DisplayName,
        int MaxActivationPoints,
        double InterfaceOrder,
        string Coalition,
        IReadOnlyList<string> Tags,
        string CountryId,
        string TypeToken,
        IReadOnlyList<string> StandoutUnits,
        string RuleName,
        string MatrixName,
        IReadOnlyDictionary<string, DivisionTextLocation> Fields);

    private sealed record DeckRecord(
        NdfObjectInfo Source,
        string DivisionName,
        IReadOnlyList<string> PackNames,
        DivisionTextLocation PackListLocation);

    private sealed record MatrixRecord(
        string Name,
        IReadOnlyList<DivisionCostCurveState> Curves,
        DivisionTextLocation Location);
}
