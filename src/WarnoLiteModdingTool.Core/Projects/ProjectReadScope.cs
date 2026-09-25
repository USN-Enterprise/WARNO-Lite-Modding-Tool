using System.Text;

namespace WarnoLiteModdingTool.Core.Projects;

/// <summary>A read-only snapshot for one load, explicitly entered by the caller. Never used by commit checks.</summary>
public sealed class ProjectReadScope
{
    private static readonly AsyncLocal<ProjectReadScope?> Ambient = new();
    private readonly Dictionary<string, Source> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Projection> _projections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ObjectProjection> _objects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Stamp> _observed = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string>? _dependencies;
    private readonly object _gate = new();
    public static ProjectReadScope? Current => Ambient.Value;
    public long ReadBytes { get; private set; }
    public int ReadFiles { get; private set; }
    public int ReusedProjections { get; private set; }
    public ProjectLoadCache? DiskCache { get; set; }
    public HashSet<string> ChangedFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ProjectReadScope(ProjectReadScope? previous = null)
    {
        if (previous is null) return;
        foreach (var pair in previous._sources)
        {
            var stamp = Observe(pair.Key);
            if (stamp == pair.Value.Stamp) _sources[pair.Key] = pair.Value;
            else ChangedFiles.Add(pair.Key);
        }
        foreach (var pair in previous._projections) _projections[pair.Key] = pair.Value;
        foreach (var pair in previous._objects) _objects[pair.Key] = pair.Value;
    }
    public IDisposable Enter()
    {
        var previous = Ambient.Value;
        Ambient.Value = this;
        return new Exit(() => Ambient.Value = previous);
    }
    public void Invalidate(string path)
    {
        path = Path.GetFullPath(path);
        _sources.Remove(path); _observed.Remove(path); ChangedFiles.Add(path);
    }
    public static byte[] ReadAllBytes(string path) => Current?.Read(path).Bytes ?? File.ReadAllBytes(path);
    public static string ReadAllText(string path) => Current?.Read(path).Text ?? File.ReadAllText(path);
    public static void Track(string path)
    {
        if (Current is not { } scope) return;
        path = Path.GetFullPath(path);
        scope._dependencies?.Add(path);
        _ = scope.Observe(path);
    }
    public static T Memo<T>(string key, Func<T> create) where T : class => Current is { } scope ? scope.Get(key, create) : create();
    public static T FileValue<T>(string key, string file, string source, bool allowDisk, Func<T> create) where T : class
    {
        Track(file);
        var cache = allowDisk ? Current?.DiskCache : null;
        var value = cache?.ReadProjection<T>(key, file) ?? ObjectValue(key, source, 0, source.Length, create);
        cache?.SetProjection(key, file, value);
        return value;
    }
    public static T ObjectValue<T>(string key, string source, int offset, int length, Func<T> create) where T : class
    {
        if (Current is not { } scope) return create();
        if (scope._objects.TryGetValue(key, out var old) && old.Value is T value &&
            source.AsSpan(offset, length).SequenceEqual(old.Source.AsSpan(old.Offset, old.Length)))
        {
            scope._objects[key] = new(source, offset, length, value); scope.ReusedProjections++; return value;
        }
        var result = create(); scope._objects[key] = new(source, offset, length, result); return result;
    }
    private T Get<T>(string key, Func<T> create) where T : class
    {
        lock (_gate)
        {
            if (_projections.TryGetValue(key, out var saved) && saved.Value is T value &&
                saved.Inputs.All(p => !ChangedFiles.Contains(p.Key) && Observe(p.Key) == p.Value))
            {
                _dependencies?.UnionWith(saved.Inputs.Keys);
                ReusedProjections++;
                return value;
            }
            var parent = _dependencies;
            _dependencies = new(StringComparer.OrdinalIgnoreCase);
            try
            {
                var result = create();
                var inputs = _dependencies.ToDictionary(p => p, Observe, StringComparer.OrdinalIgnoreCase);
                _projections[key] = new(result, inputs);
                return result;
            }
            finally { parent?.UnionWith(_dependencies); _dependencies = parent; }
        }
    }
    // Call once after discovery, before reusing projections. A newly added file may introduce a new dependency.
    public void InvalidateDiscovery() => _projections.Clear();
    public bool IsStable() => _observed.All(p => Inspect(p.Key) == p.Value);
    private Stamp Observe(string path)
    {
        if (!_observed.TryGetValue(path, out var stamp)) _observed[path] = stamp = Inspect(path);
        return stamp;
    }
    private Source Read(string path)
    {
        lock (_gate)
        {
            path = Path.GetFullPath(path);
            _dependencies?.Add(path);
            if (_sources.TryGetValue(path, out var source)) return source;
            var stamp = Observe(path);
            var bytes = File.ReadAllBytes(path);
            if (Inspect(path) != stamp) throw new IOException("读取期间源文件发生变化：" + path);
            using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true);
            source = new(stamp, bytes, reader.ReadToEnd());
            _sources[path] = source; ReadFiles++; ReadBytes += bytes.LongLength;
            return source;
        }
    }
    private static Stamp Inspect(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? new(true, file.Length, file.LastWriteTimeUtc.Ticks) : new(false, 0, 0);
    }
    private sealed record Stamp(bool Exists, long Length, long Modified);
    private sealed record Source(Stamp Stamp, byte[] Bytes, string Text);
    private sealed record Projection(object Value, Dictionary<string, Stamp> Inputs);
    private sealed record ObjectProjection(string Source, int Offset, int Length, object Value);
    private sealed class Exit(Action action) : IDisposable { public void Dispose() => action(); }
}
