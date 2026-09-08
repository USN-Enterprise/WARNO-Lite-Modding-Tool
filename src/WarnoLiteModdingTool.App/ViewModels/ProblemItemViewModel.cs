using WarnoLiteModdingTool.App.Diagnostics;

namespace WarnoLiteModdingTool.App.ViewModels;

public sealed record ProblemItemViewModel(ProblemReport Report)
{
    public string Id => Report.Id;
    public string Title => Report.Title;
    public string Summary => Report.Summary;
    public string Details => Report.Details;
    public string Location => Report.Location ?? "—";
    public string TimeText => Report.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss");
    public string SeverityText => Report.Severity switch
    {
        ProblemSeverity.Information => "信息",
        ProblemSeverity.Warning => "警告",
        ProblemSeverity.Error => "错误",
        ProblemSeverity.Fatal => "致命",
        _ => Report.Severity.ToString()
    };

    public string DiagnosticText => ApplicationProblemLog.Format(Report);
}
