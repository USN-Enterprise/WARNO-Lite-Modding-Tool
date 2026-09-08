namespace WarnoLiteModdingTool.Core.Indexing;

public sealed record IndexProgress(
    int CompletedFiles,
    int TotalFiles,
    string CurrentFile,
    string Message)
{
    public double Percent => TotalFiles == 0 ? 0 : (double)CompletedFiles / TotalFiles * 100;
}

