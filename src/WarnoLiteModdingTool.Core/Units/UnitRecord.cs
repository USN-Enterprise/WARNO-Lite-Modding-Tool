using WarnoLiteModdingTool.Core.Ndf;

namespace WarnoLiteModdingTool.Core.Units;

public sealed class UnitRecord
{
    private readonly Dictionary<string, UnitFieldValue> _fields;

    public UnitRecord(NdfObjectInfo source, IReadOnlyList<UnitFieldValue> fields, bool hasUniqueTransporterModule)
    {
        Source = source;
        _fields = fields.ToDictionary(item => item.Definition.Key, StringComparer.Ordinal);
        Fields = fields;
        HasUniqueTransporterModule = hasUniqueTransporterModule;
        DisplayName = source.DisplayName;
    }

    public NdfObjectInfo Source { get; }
    internal string? SourceSnapshot {get;init;}

    public string Name => Source.Name;

    public string DisplayName { get; internal set; }

    public string? NameToken { get; internal set; }

    public bool NameTokenRequiresReplacement { get; internal set; }

    public string NameSource { get; internal set; } = "内部描述符";

    public bool CanEditName { get; internal set; }

    public string NameEditReason { get; internal set; } = string.Empty;

    public string? UnitsCsvRelativePath { get; internal set; }

    public IReadOnlyList<UnitFieldValue> Fields { get; internal set; }

    public IReadOnlyList<string> Weapons { get; internal set; } = [];

    public IReadOnlyList<string> Ammunition { get; internal set; } = [];

    public IReadOnlyList<string> Divisions { get; internal set; } = [];

    public IReadOnlyList<string> PresentationReferences { get; internal set; } = [];

    public bool HasUniqueTransporterModule { get; }

    public string Coalition => Value("structure.coalition");

    public string Country => Value("structure.country");

    public string Category => Value("structure.category");

    public string Factory => Value("structure.factory");

    public string Role => Value("structure.role");

    public UnitFieldValue? Field(string key) =>
        _fields.TryGetValue(key, out var value) ? value : null;

    internal void ReplaceFields(IReadOnlyList<UnitFieldValue> fields)
    {
        _fields.Clear();
        foreach (var field in fields)
        {
            _fields[field.Definition.Key] = field;
        }

        Fields = fields;
    }

    private string Value(string key) => Field(key)?.DisplayValue ?? string.Empty;
}
