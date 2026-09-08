using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Projects;

namespace WarnoLiteModdingTool.Core.Indexing;

public sealed record ProjectIndexResult(
    IReadOnlyList<NdfObjectInfo> Objects,
    IReadOnlyList<NdfDiagnostic> Diagnostics,
    IReadOnlyList<ModuleCapability> Modules);

