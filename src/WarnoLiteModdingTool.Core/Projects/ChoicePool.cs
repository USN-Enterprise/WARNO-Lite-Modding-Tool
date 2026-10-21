namespace WarnoLiteModdingTool.Core.Projects;

/// <summary>Project-local immutable candidate lists, compared by their complete ordered values.</summary>
internal sealed class ChoicePool
{
    private sealed class ValuesComparer : IEqualityComparer<IReadOnlyList<string>>
    {
        public bool Equals(IReadOnlyList<string>? a, IReadOnlyList<string>? b) =>
            ReferenceEquals(a, b) || a is not null && b is not null && a.SequenceEqual(b, StringComparer.Ordinal);
        public int GetHashCode(IReadOnlyList<string> values)
        {
            var hash = new HashCode();
            foreach (var value in values) hash.Add(value, StringComparer.Ordinal);
            return hash.ToHashCode();
        }
    }
    private readonly Dictionary<IReadOnlyList<string>, int> _ids = new(new ValuesComparer());
    public List<IReadOnlyList<string>> Values { get; } = [];
    public int Add(IReadOnlyList<string> values)
    {
        if (_ids.TryGetValue(values, out var id)) return id;
        id = Values.Count; var snapshot = values.ToArray(); Values.Add(snapshot); _ids.Add(snapshot, id); return id;
    }
    public IReadOnlyList<string> Intern(IReadOnlyList<string> values) => Values[Add(values)];
}
