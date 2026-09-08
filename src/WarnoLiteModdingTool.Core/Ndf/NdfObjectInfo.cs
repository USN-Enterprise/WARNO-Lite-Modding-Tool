namespace WarnoLiteModdingTool.Core.Ndf;

public sealed record NdfObjectInfo(
    string ModuleKey,
    string Name,
    string DisplayName,
    string TypeName,
    string SourceFile,
    string RelativeSourceFile,
    int CharacterOffset,
    int CharacterLength,
    long ByteOffset,
    int ByteLength,
    int LineNumber);

