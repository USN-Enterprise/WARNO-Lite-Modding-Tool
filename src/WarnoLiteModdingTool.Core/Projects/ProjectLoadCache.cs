using System.IO.Compression;
using System.Text.Json;
using WarnoLiteModdingTool.Core.Indexing;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.Core.Projects;

// Explicitly supplied for UI reads only. Formal transaction planners never receive this cache.
public sealed class ProjectLoadCache
{
    private const int Format = 5;
    private static readonly object PublishGate = new();
    private static long _version;
    private static readonly Dictionary<string, long> Publications = new(StringComparer.OrdinalIgnoreCase);
    private readonly long _openedVersion;
    private readonly long _publication;
    private readonly string _root;
    private readonly string? _path;
    private readonly Stamp[] _stamps;
    private readonly Dictionary<string, Stamp> _current;
    private Dictionary<string, Stamp> _previous = new(StringComparer.OrdinalIgnoreCase);
    private byte[]? _archive;
    private Header? _header;
    private ProjectIndexResult? _oldIndex;
    private ProjectIndexResult? _index;
    private Dictionary<string, SavedUnit[]> _oldUnits = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, SavedUnit[]> _units = new(StringComparer.OrdinalIgnoreCase);
    private SavedWeapons? _oldWeapons;
    private SavedWeapons? _weapons;
    private Dictionary<string, (string File, object Value)> _oldProjections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string File, object Value)> _projections = new(StringComparer.Ordinal);
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WarnoLiteModdingTool", "project-cache", "last-project.json.gz");
    public bool IsHit { get; private set; }
    public int RestoredUnits { get; private set; }
    public int RestoredWeapons { get; private set; }
    public int RestoredIndexFiles { get; private set; }
    public string MissReason { get; private set; } = "missing";
    public IReadOnlyList<string> ChangedPaths => _current.Keys.Union(_previous.Keys, StringComparer.OrdinalIgnoreCase)
        .Where(p => !_current.TryGetValue(p, out var now) || !_previous.TryGetValue(p, out var before) || now != before).ToArray();
    public bool FileSetChanged => !_current.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(_previous.Keys);
    public bool SourcesUnchanged() => Inspect(_root).SequenceEqual(_stamps);
    private ProjectLoadCache(string root, string? path)
    {
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)); _path = path;
        _stamps = Inspect(_root); _current = _stamps.ToDictionary(s => s.Path, StringComparer.OrdinalIgnoreCase);
        lock (PublishGate)
        {
            _openedVersion = _version;
            if (_path is not null) Publications[_path] = _publication = Publications.GetValueOrDefault(_path) + 1;
        }
    }
    public static ProjectLoadCache CreateSession(string root) => new(root, null);
    public ProjectLoadCache Next()
    {
        var next = new ProjectLoadCache(_root, _path) { _oldIndex = _index ?? _oldIndex,
            _oldUnits = new(_units, StringComparer.OrdinalIgnoreCase), _oldWeapons = _weapons ?? _oldWeapons,
            _previous = new(_current, StringComparer.OrdinalIgnoreCase), _oldProjections = new(_projections, StringComparer.Ordinal) };
        next.IsHit = next._stamps.SequenceEqual(_stamps); next.MissReason = next.IsHit ? "none" : "source-changed";
        return next;
    }
    public static ProjectLoadCache? Open(string root, bool enabled = true, string? path = null)
    {
        if (!enabled) return null;
        try
        {
            if (!Directory.Exists(Path.Combine(root, "GameData"))) return null;
            var result = new ProjectLoadCache(root, path ?? DefaultPath);
            try
            {
                using var file = File.OpenRead(result._path!);
                using var zip = new ZipArchive(file, ZipArchiveMode.Read);
                using var entry = zip.GetEntry("manifest.json")!.Open();
                var header = JsonSerializer.Deserialize<Header>(entry);
                if (header is null || header.Format != Format || header.Build != typeof(ProjectLoadCache).Assembly.ManifestModule.ModuleVersionId ||
                    !string.Equals(header.Root, result._root, StringComparison.OrdinalIgnoreCase) || header.Culture != System.Globalization.CultureInfo.CurrentCulture.Name)
                { result.MissReason = "incompatible"; return result; }
                result._header = header; result._previous = header.Stamps.ToDictionary(s => s.Path, StringComparer.OrdinalIgnoreCase);
                // Pin this compressed generation so background publication cannot mix partitions.
                file.Position = 0; using var bytes = new MemoryStream(); file.CopyTo(bytes); result._archive = bytes.ToArray();
                result.IsHit = header.Stamps.SequenceEqual(result._stamps); result.MissReason = result.IsHit ? "none" : "source-changed";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException or NullReferenceException)
            { result._archive = null; result._header = null; result.IsHit = false; result.MissReason = "unavailable-or-corrupt"; }
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
    private T? Read<T>(string name) where T : class
    {
        if (_archive is null) return null;
        try
        {
            using var zip = new ZipArchive(new MemoryStream(_archive), ZipArchiveMode.Read);
            var entry = zip.GetEntry(name); if (entry is null) return null;
            using var stream = entry.Open(); return JsonSerializer.Deserialize<T>(stream);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ArgumentException)
        { IsHit = false; MissReason = "corrupt-partition"; return null; }
    }
    private ProjectIndexResult? OldIndex => _oldIndex ??= Read<ProjectIndexResult>("index.json");
    private SavedUnit[]? ReadUnits(string name)
    {
        if (_archive is null) return null;
        try
        {
            using var zip = new ZipArchive(new MemoryStream(_archive), ZipArchiveMode.Read);
            using var stream = zip.GetEntry(name)?.Open();
            if (stream is null) return null;
            using var payload = new MemoryStream(); stream.CopyTo(payload); payload.Position = 0;
            return UnitCacheCodec.Read(payload);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or FormatException)
        { IsHit = false; MissReason = "corrupt-unit-partition"; return null; }
    }
    public ProjectIndexResult? Index => IsHit ? OldIndex : null;
    public void SetIndex(ProjectIndexResult index) => _index = index;
    public void InvalidateFiles(IEnumerable<string> files)
    {
        foreach (var file in files)
            if (_previous.TryGetValue(Relative(file), out var stamp)) _previous[Relative(file)] = stamp with { Modified = long.MinValue };
        IsHit = false;
    }
    private string Relative(string path) => Path.GetRelativePath(_root, Path.GetFullPath(path)).Replace('\\', '/');
    private bool Unchanged(string relative) => _current.TryGetValue(relative, out var now) && _previous.TryGetValue(relative, out var old) && now == old;
    internal T? ReadProjection<T>(string key, string file) where T : class
    {
        var relative = Relative(file);
        if (!Unchanged(relative)) return null;
        if (_oldProjections.TryGetValue(key, out var saved) && saved.File == relative) return saved.Value as T;
        var entry = _header?.Projections.GetValueOrDefault(key);
        return entry?.File == relative ? Read<T>(entry.Entry) : null;
    }
    internal void SetProjection(string key, string file, object value) => _projections[key] = (Relative(file), value);
    public bool RestoreIndexFile(string file, string module, out NdfObjectInfo[] objects, out NdfDiagnostic[] diagnostics)
    {
        objects = []; diagnostics = [];
        if (!Unchanged(Relative(file)) || OldIndex is not { } index || !index.Modules.Any(m => m.Key == module && m.SourceFiles.Contains(file, StringComparer.OrdinalIgnoreCase))) return false;
        objects = index.Objects.Where(o => o.ModuleKey == module && string.Equals(o.SourceFile, file, StringComparison.OrdinalIgnoreCase)).ToArray();
        diagnostics = index.Diagnostics.Where(d => string.Equals(d.SourceFile, file, StringComparison.OrdinalIgnoreCase)).ToArray();
        RestoredIndexFiles++; return true;
    }
    public bool RelationshipsUnchanged => !FileSetChanged && OldIndex is { } index && index.Modules.Where(m => m.Key is "units" or "weapons" or "ammo" or "divisions")
        .SelectMany(m => m.SourceFiles).All(p => Unchanged(Relative(p)));
    internal UnitRecord[] RestoreUnits(IReadOnlyDictionary<string, string> sources)
    {
        var restored = new List<UnitRecord>(); var definitions = UnitFieldDefinitions.All.ToDictionary(f => f.Key, StringComparer.Ordinal);
        foreach (var file in (OldIndex?.Objects ?? []).Where(o => o.ModuleKey == "units").Select(o => o.SourceFile).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var relative = Relative(file);
            if (!Unchanged(relative) || !sources.TryGetValue(file, out var source)) continue;
            if (!_oldUnits.TryGetValue(relative, out var saved))
            { var entry = _header?.Units.GetValueOrDefault(relative); saved = entry is null ? null : ReadUnits(entry); }
            if (saved is null) continue;
            try
            {
                var partition = saved.Select(u => new UnitRecord(u.Source,
                    u.Fields.Select(f => new UnitFieldValue(definitions[f.Key], f.Availability, f.DisplayValue, f.RawValue, f.Reason, f.Location, [])).ToArray(), u.HasTransporter)
                { SourceSnapshot = source, Weapons = u.Weapons, Ammunition = u.Ammunition, Divisions = u.Divisions,
                    PresentationReferences = u.PresentationReferences, NameToken = u.NameToken, HasUniqueNameField = u.UniqueName }).ToArray();
                restored.AddRange(partition);
            }
            catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException or NullReferenceException) { IsHit = false; }
        }
        RestoredUnits = restored.Count; return restored.ToArray();
    }
    internal void SetUnits(IReadOnlyList<UnitRecord> units) => _units = units.GroupBy(u => Relative(u.Source.SourceFile), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.Select(u => new SavedUnit(u.Source, u.Fields.Select(f => new SavedField(f.Definition.Key, f.Availability,
            f.DisplayValue, f.RawValue, f.Reason, f.Location)).ToArray(), u.HasUniqueTransporterModule, u.Weapons, u.Ammunition, u.Divisions,
            u.PresentationReferences, u.NameToken, u.HasUniqueNameField == true)).ToArray(), StringComparer.OrdinalIgnoreCase);
    internal WeaponWorkspaceData? RestoreWeapons(UnitWorkspaceData units)
    {
        if (OldIndex is not { } index || FileSetChanged || !index.Modules.Where(m => m.Key is "weapons" or "ammo").SelectMany(m => m.SourceFiles).All(p => Unchanged(Relative(p))) ||
            ChangedPaths.Any(p => p.EndsWith("/DamageResistance.ndf", StringComparison.OrdinalIgnoreCase))) return null;
        var saved = _oldWeapons ??= Read<SavedWeapons>("weapons.json"); if (saved is null) return null;
        try
        {
            var ammo = saved.Ammo.Select(a => new AmmoRecord(a.Source, a.Fields) { NameToken = a.Token, NameRaw = a.Raw, NameLocation = a.Location, CanEditName = a.CanEditName }).ToArray();
            foreach (var a in ammo)
            {
                var local = units.Localisation.TryResolve(a.NameToken, out var custom);
                a.DisplayName = local ? custom : Localisation.VanillaNames.Lookup("UNITS", a.NameToken) ?? a.Name;
                a.ChineseName = local ? custom : Localisation.VanillaNames.Lookup("UNITS", a.NameToken, "SC") ?? a.DisplayName;
                if (string.IsNullOrWhiteSpace(a.DisplayName)) a.DisplayName = a.Name;
                if (string.IsNullOrWhiteSpace(a.ChineseName)) a.ChineseName = a.DisplayName;
            }
            units.Localisation.AddKnownTokens(ammo.Select(a => a.NameToken));
            var choices = ammo.Select(a => a.Name).Order(StringComparer.Ordinal).ToArray();
            var weapons = saved.Records.Select(w => new WeaponRecord(w.Source, w.Fields, w.Mounts.Select(m => m with
            { Fields = m.Fields.Select(f => IsAmmoChoice(f) ? f with { Choices = choices } : f).ToArray() }).ToArray())).ToArray();
            RestoredWeapons = weapons.Length;
            return new(weapons, ammo, units.Units, WeaponProjectLoader.BuildReferences(weapons, ammo, units.Units), saved.Diagnostics);
        }
        catch (Exception ex) when (ex is ArgumentException or NullReferenceException) { IsHit = false; return null; }
    }
    internal void SetWeapons(WeaponWorkspaceData data) => _weapons = new(data.Weapons.Select(w => new WeaponRecord(w.Source, w.Fields,
        w.Mounts.Select(m => m with { Fields = m.Fields.Select(f => IsAmmoChoice(f) ? f with { Choices = [] } : f).ToArray() }).ToArray())).ToArray(),
        data.Ammunition.Select(a => new SavedAmmo(a.Source, a.Fields, a.NameToken, a.NameRaw, a.NameLocation, a.CanEditName)).ToArray(), data.Diagnostics);
    private static bool IsAmmoChoice(WeaponFieldValue f) => f.Definition.Owner == WeaponFieldOwner.MountedWeapon && f.Definition.FieldName == "Ammunition";
    public bool Save(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_path is null || (_index ?? OldIndex) is not { } index) return false;
        var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (!SourcesUnchanged()) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            using (var file = File.Create(temp)) using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                void Write<T>(string name, T data) { cancellationToken.ThrowIfCancellationRequested(); using var stream = zip.CreateEntry(name, CompressionLevel.Fastest).Open(); JsonSerializer.Serialize(stream, data); }
                var entries = _units.Keys.Select((p, i) => (p, entry: $"units/{i}.bin")).ToDictionary(p => p.p, p => p.entry, StringComparer.OrdinalIgnoreCase);
                var projections = _projections.Select((p, i) => (p.Key, Entry: new ProjectionEntry(p.Value.File, $"projections/{i}.json"))).ToDictionary(p => p.Key, p => p.Entry);
                Write("manifest.json", new Header(Format, typeof(ProjectLoadCache).Assembly.ManifestModule.ModuleVersionId, _root, System.Globalization.CultureInfo.CurrentCulture.Name, _stamps, entries, projections));
                foreach (var pair in _projections) Write(projections[pair.Key].Entry, pair.Value.Value);
                Write("index.json", index);
                foreach (var pair in _units)
                { cancellationToken.ThrowIfCancellationRequested(); using var stream = zip.CreateEntry(entries[pair.Key], CompressionLevel.Fastest).Open(); using var buffered = new BufferedStream(stream, 64 * 1024); UnitCacheCodec.Write(buffered, pair.Value); }
                if (_weapons is not null) Write("weapons.json", _weapons);
            }
            if (!Inspect(_root).SequenceEqual(_stamps)) return false;
            lock (PublishGate) { if (_version != _openedVersion || Publications.GetValueOrDefault(_path) != _publication) return false; File.Move(temp, _path, true); }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return false; }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    public static void CancelPendingSave() { lock (PublishGate) _version++; }
    public static void Clear(string? path = null) { lock (PublishGate) { _version++; File.Delete(path ?? DefaultPath); } }
    private static Stamp[] Inspect(string root)
    {
        var files = new List<Stamp>(); var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            foreach (var sub in Directory.EnumerateDirectories(directory)) if (Path.GetFileName(sub) is not ".warno-editor" and not ".git") pending.Push(sub);
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (!Path.GetExtension(file).Equals(".ndf", StringComparison.OrdinalIgnoreCase) && !Path.GetExtension(file).Equals(".csv", StringComparison.OrdinalIgnoreCase)) continue;
                var info = new FileInfo(file); files.Add(new(Path.GetRelativePath(root, file).Replace('\\', '/'), info.Length, info.LastWriteTimeUtc.Ticks));
            }
        }
        return files.OrderBy(s => s.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public sealed record Stamp(string Path, long Length, long Modified);
    public sealed record Header(int Format, Guid Build, string Root, string Culture, Stamp[] Stamps, Dictionary<string, string> Units, Dictionary<string, ProjectionEntry> Projections);
    public sealed record ProjectionEntry(string File, string Entry);
    public sealed record SavedField(string Key, UnitFieldAvailability Availability, string DisplayValue, string RawValue, string Reason, UnitSourceLocation? Location);
    public sealed record SavedUnit(NdfObjectInfo Source, SavedField[] Fields, bool HasTransporter, IReadOnlyList<string> Weapons, IReadOnlyList<string> Ammunition, IReadOnlyList<string> Divisions, IReadOnlyList<string> PresentationReferences, string? NameToken, bool UniqueName);
    public sealed record SavedAmmo(NdfObjectInfo Source, IReadOnlyList<WeaponFieldValue> Fields, string Token, string Raw, WeaponFieldLocation? Location, bool CanEditName);
    public sealed record SavedWeapons(IReadOnlyList<WeaponRecord> Records, SavedAmmo[] Ammo, IReadOnlyList<string> Diagnostics);
}
