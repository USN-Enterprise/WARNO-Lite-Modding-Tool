namespace WarnoLiteModdingTool.Core.Ndf;

public sealed class NdfFieldLocator
{
    public NdfFieldMatch Locate(NdfSyntaxDocument document, NdfFieldSelector selector)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selector);

        var values = document.FindConstructors(selector.ModuleType)
            .SelectMany(module => document.FindDirectAssignments(module, selector.FieldName))
            .ToList();

        if (selector.MapKey is not null)
        {
            values = values
                .SelectMany(document.ReadMapEntries)
                .Where(entry => KeyMatches(document.Raw(entry.Key), selector.MapKey))
                .Select(entry => entry.Value)
                .ToList();
        }

        if (selector.NestedType is not null && selector.NestedField is not null)
        {
            values = values
                .SelectMany(value => document.FindConstructors(selector.NestedType, value))
                .SelectMany(constructor => document.FindDirectAssignments(constructor, selector.NestedField))
                .ToList();
        }

        if (selector.ArgumentName is not null)
        {
            values = values
                .Select(value => document.FindNamedArgument(value, selector.ArgumentName))
                .Where(value => value is not null)
                .Cast<NdfValueSpan>()
                .ToList();
        }

        return new NdfFieldMatch(selector, values);
    }

    private static bool KeyMatches(string rawKey, string expected)
    {
        var value = NdfSyntaxDocument.Unquote(rawKey);
        return string.Equals(value, expected, StringComparison.Ordinal) ||
               string.Equals(NdfSyntaxDocument.Leaf(value), NdfSyntaxDocument.Leaf(expected), StringComparison.Ordinal);
    }
}

public sealed record NdfFieldSelector(
    string ModuleType,
    string FieldName,
    string? MapKey = null,
    string? NestedType = null,
    string? NestedField = null,
    string? ArgumentName = null)
{
    public string DisplayPath
    {
        get
        {
            var path = $"{ModuleType}.{FieldName}";
            if (MapKey is not null)
            {
                path += $"[{MapKey}]";
            }

            if (NestedType is not null && NestedField is not null)
            {
                path += $".{NestedType}.{NestedField}";
            }

            if (ArgumentName is not null)
            {
                path += $".{ArgumentName}";
            }

            return path;
        }
    }
}

public sealed record NdfFieldMatch(
    NdfFieldSelector Selector,
    IReadOnlyList<NdfValueSpan> Values);
