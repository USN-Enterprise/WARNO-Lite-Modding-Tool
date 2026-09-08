namespace WarnoLiteModdingTool.Core.Ndf;

public sealed record NdfScanResult(
    IReadOnlyList<NdfObjectInfo> Objects,
    IReadOnlyList<NdfDiagnostic> Diagnostics);

