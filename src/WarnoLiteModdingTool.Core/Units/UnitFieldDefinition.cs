using WarnoLiteModdingTool.Core.Ndf;

namespace WarnoLiteModdingTool.Core.Units;

public sealed record UnitFieldDefinition(
    string Key,
    string Group,
    string Label,
    string ShortHint,
    NdfFieldSelector Selector,
    UnitValueKind ValueKind,
    UnitEditorKind EditorKind,
    bool NonNegative = false,
    string? UnitSuffix = null,
    string? ConversionSource = null,
    string Section = "其他",
    bool CanInsertWhenMissing = false);

public enum UnitValueKind
{
    Integer,
    Decimal,
    EcmPercent,
    Choice,
    QuotedString,
    StringList,
    PathList,
    UnitReference
}

public enum UnitEditorKind
{
    Text,
    Choice
}
