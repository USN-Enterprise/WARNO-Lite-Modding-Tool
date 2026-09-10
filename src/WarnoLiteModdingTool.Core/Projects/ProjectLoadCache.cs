using System.IO.Compression;
using System.Text.Json;
using WarnoLiteModdingTool.Core.Indexing;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.Core.Projects;

// Only the explicit UI-open path supplies this cache. Transaction planners never do.
public sealed class ProjectLoadCache
{
    private const int Format = 2;
    private static readonly object PublishGate = new();
    private static long _clearVersion;
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WarnoLiteModdingTool", "project-cache", "last-project.json.gz");
    private readonly string _path;
    private readonly string _root;
    private readonly Stamp[] _stamps;
    private readonly string _culture = System.Globalization.CultureInfo.CurrentCulture.Name;
    private Snapshot? _snapshot;
    private readonly long _openedVersion = Interlocked.Read(ref _clearVersion);
    public bool IsHit { get; private set; }
    public int RestoredUnits { get; private set; }
    public int RestoredWeapons { get; private set; }

    private ProjectLoadCache(string root, string path, Stamp[] stamps)
    { _root = root; _path = path; _stamps = stamps; }

    public static ProjectLoadCache? Open(string root, bool enabled = true, string? path = null)
    {
        if (!enabled) return null;
        try
        {
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            if (!Directory.Exists(Path.Combine(root, "GameData"))) return null;
            var cache = new ProjectLoadCache(root, path ?? DefaultPath, Inspect(root));
            try
            {
                using var file = File.OpenRead(cache._path);
                using var zip = new GZipStream(file, CompressionMode.Decompress);
                var data = JsonSerializer.Deserialize<Snapshot>(zip);
                if (data is not null && data.Format == Format && data.Build == typeof(ProjectLoadCache).Assembly.ManifestModule.ModuleVersionId &&
                    string.Equals(data.Root, root, StringComparison.OrdinalIgnoreCase) && data.Culture == cache._culture &&
                    data.Stamps is not null && data.Stamps.SequenceEqual(cache._stamps) && data.Index is not null &&
                    data.Index.Objects is not null && data.Index.Modules is not null && data.Index.Diagnostics is not null)
                { cache._snapshot = data; cache.IsHit = true; }
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException) { }
            return cache;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    public ProjectIndexResult? Index => _snapshot?.Index;
    public void SetIndex(ProjectIndexResult index) => _snapshot = new(Format, typeof(ProjectLoadCache).Assembly.ManifestModule.ModuleVersionId,
        _root, _culture, _stamps, index, null, null);

    internal UnitRecord[]? RestoreUnits(IReadOnlyDictionary<string, string> sources)
    {
        if (_snapshot?.Units is not { } saved) return null;
        try
        {
            var definitions = UnitFieldDefinitions.All.ToDictionary(f => f.Key, StringComparer.Ordinal);
            var units = saved.Select(u => new UnitRecord(u.Source,
                u.Fields.Select(f => new UnitFieldValue(definitions[f.Key], f.Availability, f.DisplayValue, f.RawValue,
                    f.Reason, f.Location, [])).ToArray(), u.HasTransporter)
            {
                SourceSnapshot = sources[u.Source.SourceFile], Weapons = u.Weapons, Ammunition = u.Ammunition,
                Divisions = u.Divisions, PresentationReferences = u.PresentationReferences
            }).ToArray();
            RestoredUnits = units.Length;
            return units;
        }
        catch (Exception e) when (e is ArgumentException or KeyNotFoundException or NullReferenceException) { return null; }
    }

    internal void SetUnits(IReadOnlyList<UnitRecord> units)
    {
        if (_snapshot is null) return;
        _snapshot = _snapshot with { Units = units.Select(u => new SavedUnit(u.Source,
            u.Fields.Select(f => new SavedField(f.Definition.Key, f.Availability, f.DisplayValue, f.RawValue, f.Reason, f.Location)).ToArray(),
            u.HasUniqueTransporterModule, u.Weapons, u.Ammunition, u.Divisions, u.PresentationReferences)).ToArray() };
    }

    internal WeaponWorkspaceData? RestoreWeapons(UnitWorkspaceData units)
    {
        if (_snapshot?.Weapons is not { } saved) return null;
        try
        {
            var ammo = saved.Ammo.Select(a => new AmmoRecord(a.Source, a.Fields)
            { NameToken = a.Token, NameRaw = a.Raw, NameLocation = a.Location, CanEditName = a.CanEditName }).ToArray();
            foreach (var a in ammo)
            {
                var local = units.Localisation.TryResolve(a.NameToken, out var custom);
                a.DisplayName = local ? custom : Localisation.VanillaNames.Lookup("UNITS", a.NameToken) ?? a.Name;
                a.ChineseName = local ? custom : Localisation.VanillaNames.Lookup("UNITS", a.NameToken, "SC") ?? a.DisplayName;
                if (string.IsNullOrWhiteSpace(a.DisplayName)) a.DisplayName = a.Name;
                if (string.IsNullOrWhiteSpace(a.ChineseName)) a.ChineseName = a.DisplayName;
            }
            units.Localisation.AddKnownTokens(ammo.Select(a => a.NameToken));
            var ammoChoices = ammo.Select(a => a.Name).Order(StringComparer.Ordinal).ToArray();
            var records = saved.Records.Select(w => new WeaponRecord(w.Source, w.Fields,
                w.Mounts.Select(m => m with { Fields = m.Fields.Select(f => IsAmmoChoice(f) ? f with { Choices = ammoChoices } : f).ToArray() }).ToArray())).ToArray();
            RestoredWeapons = records.Length;
            return new(records, ammo, units.Units, saved.References, saved.Diagnostics);
        }
        catch (Exception e) when (e is ArgumentException or NullReferenceException) { return null; }
    }

    internal void SetWeapons(WeaponWorkspaceData data)
    {
        if (_snapshot is null) return;
        _snapshot = _snapshot with { Weapons = new(data.Weapons.Select(w => new WeaponRecord(w.Source, w.Fields,
            w.Mounts.Select(m => m with { Fields = m.Fields.Select(f => IsAmmoChoice(f) ? f with { Choices = [] } : f).ToArray() }).ToArray())).ToArray(), data.Ammunition.Select(a => new SavedAmmo(a.Source, a.Fields,
            a.NameToken, a.NameRaw, a.NameLocation, a.CanEditName)).ToArray(), data.References, data.Diagnostics) };
    }

    private static bool IsAmmoChoice(WeaponFieldValue field) =>
        field.Definition.Owner == WeaponFieldOwner.MountedWeapon && field.Definition.FieldName == "Ammunition";

    public bool Save(CancellationToken cancellationToken = default)
    {
        if (_snapshot is null) return false;
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Inspect(_root).SequenceEqual(_stamps)) return false;
            if (IsHit) return true;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            using (var file = File.Create(temporary))
            using (var zip = new GZipStream(file, CompressionLevel.Fastest))
                JsonSerializer.Serialize(zip, _snapshot);
            cancellationToken.ThrowIfCancellationRequested();
            if (!Inspect(_root).SequenceEqual(_stamps)) return false;
            lock (PublishGate)
            {
                // Settings can be opened while a scan is running. Clearing must also
                // discard that scan's pending publication, not just the previous file.
                if (_openedVersion != _clearVersion) return false;
                File.Move(temporary, _path, true);
            }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return false; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    public static void Clear(string? path = null)
    {
        lock (PublishGate)
        {
            Interlocked.Increment(ref _clearVersion);
            File.Delete(path ?? DefaultPath);
        }
    }

    private static Stamp[] Inspect(string root)
    {
        var files = new List<Stamp>();
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            foreach (var sub in Directory.EnumerateDirectories(directory))
                if (Path.GetFileName(sub) is not ".warno-editor" and not ".git") pending.Push(sub);
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (!Path.GetExtension(file).Equals(".ndf", StringComparison.OrdinalIgnoreCase) &&
                    !Path.GetExtension(file).Equals(".csv", StringComparison.OrdinalIgnoreCase)) continue;
                var info = new FileInfo(file);
                files.Add(new(Path.GetRelativePath(root, file), info.Length, info.LastWriteTimeUtc.Ticks));
            }
        }
        return files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public sealed record Stamp(string Path, long Length, long Modified);
    public sealed record Snapshot(int Format, Guid Build, string Root, string Culture, Stamp[] Stamps,
        ProjectIndexResult Index, SavedUnit[]? Units, SavedWeapons? Weapons);
    public sealed record SavedField(string Key, UnitFieldAvailability Availability, string DisplayValue, string RawValue,
        string Reason, UnitSourceLocation? Location);
    public sealed record SavedUnit(NdfObjectInfo Source, SavedField[] Fields, bool HasTransporter, IReadOnlyList<string> Weapons,
        IReadOnlyList<string> Ammunition, IReadOnlyList<string> Divisions, IReadOnlyList<string> PresentationReferences);
    public sealed record SavedAmmo(NdfObjectInfo Source, IReadOnlyList<WeaponFieldValue> Fields, string Token, string Raw,
        WeaponFieldLocation? Location, bool CanEditName);
    public sealed record SavedWeapons(IReadOnlyList<WeaponRecord> Records, SavedAmmo[] Ammo, WeaponReferenceIndex References,
        IReadOnlyList<string> Diagnostics);
}
