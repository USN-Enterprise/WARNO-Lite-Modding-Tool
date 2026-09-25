using WarnoLiteModdingTool.Core.Ndf;

namespace WarnoLiteModdingTool.Core.Units;

public sealed class UnitCatalogBuilder(NdfFieldLocator? locator = null)
{
    private readonly NdfFieldLocator _locator = locator ?? new NdfFieldLocator();

    public IReadOnlyList<UnitRecord> Build(
        IReadOnlyList<NdfObjectInfo> unitObjects,
        IReadOnlyDictionary<string, string> sources,
        CancellationToken cancellationToken = default, bool applyChoices = true)
    {
        var units = new List<UnitRecord>(unitObjects.Count);
        foreach (var objectInfo in unitObjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!sources.TryGetValue(objectInfo.SourceFile, out var source))
            {
                continue;
            }

            var parsed = Projects.ProjectReadScope.ObjectValue("unit:" + objectInfo.SourceFile + ":" + objectInfo.Name, source,
                objectInfo.CharacterOffset, objectInfo.CharacterLength, () =>
                {
                    var document = new NdfSyntaxDocument(source, objectInfo.CharacterOffset, objectInfo.CharacterLength);
                    var fields = UnitFieldDefinitions.All.Select(definition => ReadField(definition, document, objectInfo, source)).ToArray();
                    var name = _locator.Locate(document, new NdfFieldSelector("TUnitUIModuleDescriptor", "NameToken"));
                    return new ParsedUnit(objectInfo.CharacterOffset, objectInfo.LineNumber, fields,
                        document.FindConstructors("TTransporterModuleDescriptor").Count == 1,
                        name.Values.Count == 1 ? NdfSyntaxDocument.Unquote(document.Raw(name.Values[0])) : null, name.Values.Count == 1);
                });
            var offsetDelta = objectInfo.CharacterOffset - parsed.Offset;
            var lineDelta = objectInfo.LineNumber - parsed.Line;
            var currentFields = parsed.Fields.Select(f => f.Location is not { } location ? f : f with
            { Location = location with { CharacterOffset = location.CharacterOffset + offsetDelta, LineNumber = location.LineNumber + lineDelta } }).ToArray();
            units.Add(new UnitRecord(objectInfo, currentFields, parsed.Transporter)
            { SourceSnapshot = source, NameToken = parsed.Token, HasUniqueNameField = parsed.UniqueName });
        }

        if (applyChoices) ApplyChoices(units);
        return units;
    }
    private sealed record ParsedUnit(int Offset, int Line, UnitFieldValue[] Fields, bool Transporter, string? Token, bool UniqueName);
    internal static void Remember(UnitRecord unit)
    {
        if (unit.SourceSnapshot is not { } source) return;
        _ = Projects.ProjectReadScope.ObjectValue("unit:" + unit.Source.SourceFile + ":" + unit.Name, source,
            unit.Source.CharacterOffset, unit.Source.CharacterLength, () => new ParsedUnit(unit.Source.CharacterOffset, unit.Source.LineNumber,
                unit.Fields.Select(f => f with { Choices = [] }).ToArray(), unit.HasUniqueTransporterModule, unit.NameToken, unit.HasUniqueNameField == true));
    }

    private UnitFieldValue ReadField(
        UnitFieldDefinition definition,
        NdfSyntaxDocument document,
        NdfObjectInfo objectInfo,
        string source)
    {
        var match = _locator.Locate(document, definition.Selector);
        if (match.Values.Count == 0)
        {
            if (definition.Key == "structure.specialties" && document.FindConstructors("TUnitUIModuleDescriptor").Count != 1)
                return new(definition, UnitFieldAvailability.Ambiguous, "", "", "缺少唯一 UI 模块，不能安全补建特性", null, []);
            return new UnitFieldValue(
                definition,
                UnitFieldAvailability.Missing,
                string.Empty,
                string.Empty,
                "目标 Unit 中不存在唯一对应结构",
                null,
                []);
        }

        if (match.Values.Count > 1)
        {
            return new UnitFieldValue(
                definition,
                UnitFieldAvailability.Ambiguous,
                string.Empty,
                string.Empty,
                $"找到 {match.Values.Count} 个候选字段，不能安全判断目标",
                null,
                []);
        }

        var value = match.Values[0];
        var raw = document.Raw(value);
        var offset = document.StartOffset(value);
        var line = objectInfo.LineNumber + CountNewLines(source, objectInfo.CharacterOffset, offset);
        var location = new UnitSourceLocation(
            objectInfo.RelativeSourceFile,
            offset,
            document.Length(value),
            line,
            definition.Selector.DisplayPath);

        if (!UnitValueConverter.TryReadDisplay(definition, document, value, out var display, out var error))
        {
            return new UnitFieldValue(
                definition,
                UnitFieldAvailability.UnsupportedValue,
                raw,
                raw,
                error,
                location,
                []);
        }

        return new UnitFieldValue(
            definition,
            UnitFieldAvailability.Editable,
            display,
            raw,
            string.Empty,
            location,
            []);
    }

    public static void ApplyChoices(IReadOnlyList<UnitRecord> units, DamageResistanceCatalog? damageResistance = null)
    {
        var choicesByField = UnitFieldDefinitions.All
            .Where(item => (item.EditorKind == UnitEditorKind.Choice || item.ValueKind == UnitValueKind.StringList) && item.ValueKind != UnitValueKind.UnitReference)
            .ToDictionary(
                item => item.Key,
                item => (IReadOnlyList<UnitChoice>)(item.ValueKind is UnitValueKind.PathList or UnitValueKind.StringList
                    ? units.SelectMany(unit => AtomicListChoices(unit.Field(item.Key), item.ValueKind == UnitValueKind.PathList))
                    : units
                    .Select(unit => unit.Field(item.Key))
                    .Where(field => field?.CanEdit == true)
                    .Select(field => new UnitChoice(field!.DisplayValue, field.RawValue)))
                    .DistinctBy(choice => choice.Display, StringComparer.CurrentCultureIgnoreCase)
                    .OrderBy(choice => choice.Display, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray(),
                StringComparer.Ordinal);

        if (damageResistance is { ResistanceFamilies.Count: > 0 })
        {
            var armorChoices = damageResistance.ResistanceFamilies
                .Select(item => new UnitChoice(item.Name, item.Name))
                .ToArray();
            foreach (var key in UnitFieldDefinitions.All.Where(item => item.Key.EndsWith(".family", StringComparison.Ordinal)).Select(item => item.Key))
            {
                choicesByField[key] = armorChoices;
            }
        }

        var upgradeChoices = units
            .Select(unit => new UnitChoice(unit.Name, unit.Name))
            .DistinctBy(choice => choice.RawValue, StringComparer.Ordinal)
            .OrderBy(choice => choice.Display, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var upgradeIndexes = upgradeChoices.Select((choice, i) => (choice.RawValue, i)).ToDictionary(p => p.RawValue, p => p.i, StringComparer.Ordinal);

        foreach (var unit in units)
        {
            var updated = unit.Fields.Select(field =>
            {
                if (field.Definition.ValueKind == UnitValueKind.UnitReference)
                {
                    return field with { Choices = new UnitReferenceChoices(upgradeChoices, upgradeIndexes.GetValueOrDefault(unit.Name, -1)) };
                }

                return choicesByField.TryGetValue(field.Definition.Key, out var choices)
                    ? field with { Choices = choices }
                    : field;
            }).ToArray();
            unit.ReplaceFields(updated);
        }
    }

    private static IEnumerable<UnitChoice> AtomicListChoices(UnitFieldValue? field, bool pathList)
    {
        if (field?.CanEdit != true)
        {
            yield break;
        }

        var document = new NdfSyntaxDocument(field.RawValue);
        foreach (var element in document.ReadArrayElements(new NdfValueSpan(0, int.MaxValue)))
        {
            var raw = document.Raw(element);
            var unquoted = NdfSyntaxDocument.Unquote(raw);
            var display = pathList ? NdfSyntaxDocument.Leaf(unquoted) : unquoted;
            if (!pathList || display.StartsWith("TAcknowUnitType_", StringComparison.Ordinal))
            {
                yield return new UnitChoice(display, raw);
            }
        }
    }

    private static int CountNewLines(string source, int start, int end)
    {
        var count = 0;
        for (var index = start; index < end && index < source.Length; index++)
        {
            if (source[index] == '\n')
            {
                count++;
            }
        }

        return count;
    }
}
