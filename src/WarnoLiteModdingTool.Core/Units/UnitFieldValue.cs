namespace WarnoLiteModdingTool.Core.Units;

public sealed record UnitFieldValue(
    UnitFieldDefinition Definition,
    UnitFieldAvailability Availability,
    string DisplayValue,
    string RawValue,
    string Reason,
    UnitSourceLocation? Location,
    IReadOnlyList<UnitChoice> Choices)
{
    public bool CanEdit => Availability == UnitFieldAvailability.Editable;
}

public sealed record UnitSourceLocation(
    string RelativeSourceFile,
    int CharacterOffset,
    int CharacterLength,
    int LineNumber,
    string FieldPath);
