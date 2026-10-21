using System.Runtime.CompilerServices;
using WarnoLiteModdingTool.Core.Divisions;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Strategic;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.Core.Drafts;

/// <summary>Display analysis for one immutable workspace generation. Transactions always revalidate fresh inputs.</summary>
public sealed class DraftAnalysis(UnitWorkspaceData units, WeaponWorkspaceData? weapons, DivisionWorkspaceData? divisions, StrategicWorkspace? strategic)
{
    private sealed record Result(IReadOnlyList<ResolvedDraftOperation> Operations);
    private readonly ConditionalWeakTable<IReadOnlyList<DraftOperation>, Result> _results = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyDictionary<string, ResolvedDraftOperation>? _previous;
    public async Task PrepareAsync(IReadOnlyList<DraftOperation> operations, CancellationToken cancellation = default)
    {
        if (operations.Count == 0) { _previous = null; return; }
        if (_results.TryGetValue(operations, out _)) return;
        await _gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (_results.TryGetValue(operations, out _)) return;
            var resolved = await Task.Run(() =>
            {
                cancellation.ThrowIfCancellationRequested();
                using var scope = new ProjectReadScope(cancellation: cancellation).Enter();
                var result = DraftResolver.Resolve(units, weapons, divisions, operations, strategic, cancellation, _previous);
                cancellation.ThrowIfCancellationRequested();
                return result;
            }, cancellation).ConfigureAwait(false);
            _results.Add(operations, new(resolved));
            _previous = resolved.ToDictionary(r => r.Operation.Id, StringComparer.Ordinal);
        }
        finally { _gate.Release(); }
    }
    public IReadOnlyList<ResolvedDraftOperation> Get(IReadOnlyList<DraftOperation> operations) =>
        operations.Count == 0 ? [] : _results.TryGetValue(operations, out var result) ? result.Operations
            : throw new InvalidOperationException("草稿分析尚未准备 / Draft analysis is not ready");
}
