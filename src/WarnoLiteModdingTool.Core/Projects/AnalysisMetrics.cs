namespace WarnoLiteModdingTool.Core.Projects;

/// <summary>Optional operation-local counters. Never used to decide whether a write is safe.</summary>
public sealed class AnalysisMetrics : IDisposable
{
    private static readonly AsyncLocal<AnalysisMetrics?> Ambient = new();
    private readonly AnalysisMetrics? _previous;
    private long _syntaxDocuments, _syntaxCharacters, _graphs, _graphFiles, _draftPasses;
    public static AnalysisMetrics? Current => Ambient.Value;
    public AnalysisMetrics() { _previous = Ambient.Value; Ambient.Value = this; }
    public long SyntaxDocuments => Interlocked.Read(ref _syntaxDocuments);
    public long SyntaxCharacters => Interlocked.Read(ref _syntaxCharacters);
    public long Graphs => Interlocked.Read(ref _graphs);
    public long GraphFiles => Interlocked.Read(ref _graphFiles);
    public long DraftPasses => Interlocked.Read(ref _draftPasses);
    internal void Syntax(int length) { Interlocked.Increment(ref _syntaxDocuments); Interlocked.Add(ref _syntaxCharacters, length); }
    internal void Graph() => Interlocked.Increment(ref _graphs);
    internal void GraphFile() => Interlocked.Increment(ref _graphFiles);
    internal void DraftPass() => Interlocked.Increment(ref _draftPasses);
    public void Dispose() => Ambient.Value = _previous;
}
