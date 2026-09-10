using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Projects;

namespace WarnoLiteModdingTool.Core.Indexing;

public sealed class ProjectIndexer(NdfTopLevelScanner? scanner = null)
{
    private readonly NdfTopLevelScanner _scanner = scanner ?? new NdfTopLevelScanner();

    public Task<ProjectIndexResult> IndexAsync(
        ModProjectContext context,
        IProgress<IndexProgress>? progress = null,
        CancellationToken cancellationToken = default,
        ProjectLoadCache? cache = null) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cache?.Index is { } saved) return saved;
            var result = Index(context, progress, cancellationToken);
            cache?.SetIndex(result);
            return result;
        }, cancellationToken);

    private ProjectIndexResult Index(
        ModProjectContext context,
        IProgress<IndexProgress>? progress,
        CancellationToken cancellationToken)
    {
        var objects = new List<NdfObjectInfo>();
        var diagnostics = new List<NdfDiagnostic>();
        var modules = new List<ModuleCapability>();
        var filesToScan = context.Modules.Where(module => module.CanScan).Sum(module => module.SourceFiles.Count);
        var completedFiles = 0;

        foreach (var module in context.Modules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!module.CanScan)
            {
                modules.Add(module);
                continue;
            }

            var moduleObjectCount = 0;
            var moduleHasErrors = false;
            foreach (var sourceFile in module.SourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new IndexProgress(
                    completedFiles,
                    filesToScan,
                    sourceFile,
                    $"正在读取 {Path.GetFileName(sourceFile)}"));

                try
                {
                    var source = File.ReadAllText(sourceFile);
                    var scan = _scanner.Scan(
                        source,
                        sourceFile,
                        module.Key,
                        context.Layout.RootPath,
                        cancellationToken);
                    objects.AddRange(scan.Objects);
                    diagnostics.AddRange(scan.Diagnostics);
                    moduleObjectCount += scan.Objects.Count;
                    moduleHasErrors |= scan.Diagnostics.Any(item => item.Severity == NdfDiagnosticSeverity.Error);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    moduleHasErrors = true;
                    diagnostics.Add(new NdfDiagnostic(
                        sourceFile,
                        0,
                        1,
                        NdfDiagnosticSeverity.Error,
                        $"读取失败：{exception.Message}"));
                }

                completedFiles++;
                progress?.Report(new IndexProgress(
                    completedFiles,
                    filesToScan,
                    sourceFile,
                    $"已扫描 {completedFiles}/{filesToScan} 个文件"));
            }

            var availability = moduleHasErrors
                ? ModuleAvailability.ParseError
                : module.Availability;
            var summary = moduleHasErrors
                ? $"解析异常 · 已识别 {moduleObjectCount:N0} 个对象"
                : $"{module.Summary} · {moduleObjectCount:N0} 个对象";
            modules.Add(module with { Availability = availability, Summary = summary });
        }

        return new ProjectIndexResult(objects, diagnostics, modules);
    }
}
