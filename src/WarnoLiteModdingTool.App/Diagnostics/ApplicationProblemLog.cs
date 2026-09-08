using System.Text;
using System.Text.Json;
using System.IO;

namespace WarnoLiteModdingTool.App.Diagnostics;

public enum ProblemSource
{
    Mod,
    Tool
}

public enum ProblemSeverity
{
    Information,
    Warning,
    Error,
    Fatal
}

public sealed record ProblemReport(
    string Id,
    DateTimeOffset OccurredAt,
    ProblemSource Source,
    ProblemSeverity Severity,
    string Title,
    string Summary,
    string Details,
    string? Location);

public sealed class ApplicationProblemLog
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };
    private readonly object _gate = new();
    private readonly List<ProblemReport> _reports = [];
    private readonly string _logRoot;
    private readonly string _fatalMarker;

    public ApplicationProblemLog(string? logRoot = null)
    {
        _logRoot = logRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WarnoLiteModdingTool",
            "logs");
        _fatalMarker = Path.Combine(_logRoot, "last-fatal.json");
        TryTrimOldLogs();
    }

    public static ApplicationProblemLog Current { get; } = new();

    public event Action<ProblemReport>? Reported;

    public string LogRoot => _logRoot;

    public IReadOnlyList<ProblemReport> Reports
    {
        get
        {
            lock (_gate)
            {
                return _reports.ToArray();
            }
        }
    }

    public ProblemReport Record(
        ProblemSource source,
        ProblemSeverity severity,
        string title,
        string summary,
        Exception? exception = null,
        string? location = null,
        string? detailsOverride = null)
    {
        var now = DateTimeOffset.Now;
        var id = $"{now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..24].ToUpperInvariant();
        var details = detailsOverride ?? (exception is null
            ? summary
            : $"{exception.GetType().FullName}: {exception.Message}{Environment.NewLine}{exception.StackTrace}");
        var report = new ProblemReport(id, now, source, severity, title, summary, details, location);
        lock (_gate)
        {
            _reports.Add(report);
        }

        TryAppend(report);
        if (severity == ProblemSeverity.Fatal)
        {
            TryWriteFatalMarker(report);
        }

        Reported?.Invoke(report);
        return report;
    }

    public ProblemReport? ConsumeLastFatal()
    {
        try
        {
            if (!File.Exists(_fatalMarker))
            {
                return null;
            }

            var report = JsonSerializer.Deserialize<ProblemReport>(File.ReadAllText(_fatalMarker), SerializerOptions);
            File.Delete(_fatalMarker);
            return report;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static string Format(ProblemReport report) =>
        $"错误编号：{report.Id}{Environment.NewLine}" +
        $"时间：{report.OccurredAt:yyyy-MM-dd HH:mm:ss zzz}{Environment.NewLine}" +
        $"来源：{(report.Source == ProblemSource.Mod ? "Mod" : "工具")}{Environment.NewLine}" +
        $"级别：{report.Severity}{Environment.NewLine}" +
        $"标题：{report.Title}{Environment.NewLine}" +
        (string.IsNullOrWhiteSpace(report.Location) ? string.Empty : $"位置：{report.Location}{Environment.NewLine}") +
        $"摘要：{report.Summary}{Environment.NewLine}{Environment.NewLine}{report.Details}";

    private void TryAppend(ProblemReport report)
    {
        try
        {
            Directory.CreateDirectory(_logRoot);
            var path = Path.Combine(_logRoot, $"problems-{DateTime.Now:yyyy-MM-dd}.jsonl");
            File.AppendAllText(path, JsonSerializer.Serialize(report, SerializerOptions) + Environment.NewLine, new UTF8Encoding(false));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void TryWriteFatalMarker(ProblemReport report)
    {
        try
        {
            Directory.CreateDirectory(_logRoot);
            File.WriteAllText(_fatalMarker, JsonSerializer.Serialize(report, SerializerOptions), new UTF8Encoding(false));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void TryTrimOldLogs()
    {
        try
        {
            if (!Directory.Exists(_logRoot))
            {
                return;
            }

            foreach (var path in Directory.EnumerateFiles(_logRoot, "problems-*.jsonl")
                         .OrderByDescending(File.GetLastWriteTimeUtc)
                         .Skip(10))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
