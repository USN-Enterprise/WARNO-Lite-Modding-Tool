namespace WarnoLiteModdingTool.Core.Ndf;

public sealed record NdfDiagnostic(
    string SourceFile,
    int CharacterOffset,
    int LineNumber,
    NdfDiagnosticSeverity Severity,
    string Message);

