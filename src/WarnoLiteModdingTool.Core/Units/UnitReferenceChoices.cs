using System.Collections;

namespace WarnoLiteModdingTool.Core.Units;

// Every unit shares the same array. Excluding its own entry costs O(1) storage.
internal sealed class UnitReferenceChoices(IReadOnlyList<UnitChoice> choices, int excluded) : IReadOnlyList<UnitChoice>
{
    public int Count => choices.Count - (excluded >= 0 ? 1 : 0);
    public UnitChoice this[int index] => index < 0 || index >= Count ? throw new ArgumentOutOfRangeException(nameof(index))
        : choices[index + (excluded >= 0 && index >= excluded ? 1 : 0)];
    public IEnumerator<UnitChoice> GetEnumerator() { for (var i = 0; i < Count; i++) yield return this[i]; }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
