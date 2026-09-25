using System.Diagnostics;
using WarnoLiteModdingTool.Core.Divisions;
using WarnoLiteModdingTool.Core.Indexing;
using WarnoLiteModdingTool.Core.Localisation;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Strategic;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.Core.Projects;

/// <summary>One consistent UI generation. Refresh builds a replacement before publishing it to editors.</summary>
public sealed record ProjectWorkspaceSnapshot(ModProjectContext Context, ProjectIndexResult Index, UnitWorkspaceData Units,
    WeaponWorkspaceData? Weapons, DivisionWorkspaceData? Divisions, StrategicWorkspace? Strategic,
    ProjectReadScope Reads, ProjectLoadCache Cache, IReadOnlyDictionary<string, long> Timings)
{
    public IReadOnlyList<Images.TextureChoice> PictureTextures { get; init; } = [];
    public string? PictureDiagnostic { get; init; }
    public static async Task<ProjectWorkspaceSnapshot> LoadRootAsync(string root, ProjectLoadCache cache,
        CancellationToken cancellation = default, ProjectWorkspaceSnapshot? previous = null, IReadOnlyList<PlannedFileChange>? committed = null)
    {
        var reads = new ProjectReadScope(previous?.Reads);
        foreach (var change in committed ?? []) if (change.Kind != FormalTextFileKind.Log) reads.Invalidate(change.FullPath);
        using var scope = reads.Enter();
        var context = new ModProjectDetector().Detect(root);
        return await LoadAsync(context, cache, cancellation, previous, committed, reads);
    }
    public static async Task<ProjectWorkspaceSnapshot> LoadAsync(ModProjectContext context, ProjectLoadCache cache,
        CancellationToken cancellation = default, ProjectWorkspaceSnapshot? previous = null, IReadOnlyList<PlannedFileChange>? committed = null, ProjectReadScope? sourceReads = null)
    {
        var timings = new Dictionary<string, long>(); var timer = Stopwatch.StartNew(); long last = 0;
        void Mark(string name) { var now = timer.ElapsedMilliseconds; timings[name] = now - last; last = now; }
        var reads = sourceReads ?? new ProjectReadScope(previous?.Reads);
        reads.DiskCache = cache;
        if (cache.FileSetChanged) reads.InvalidateDiscovery();
        using var scope = reads.Enter();
        if (committed is not null)
        {
            cache.InvalidateFiles(committed.Where(c => c.Kind != FormalTextFileKind.Log).Select(c => c.FullPath));
            foreach (var change in committed.Where(c => c.Kind != FormalTextFileKind.Log))
            {
                cancellation.ThrowIfCancellationRequested();
                if (sourceReads is null) reads.Invalidate(change.FullPath);
                if (change.Action == PlannedFileAction.Delete)
                { if (File.Exists(change.FullPath)) throw new IOException("应用后文件被重新创建：" + change.RelativePath); }
                else if (!ProjectReadScope.ReadAllBytes(change.FullPath).AsSpan().SequenceEqual(change.CandidateBytes))
                    throw new IOException("应用后文件又发生变化，请重新读取：" + change.RelativePath);
            }
        }
        Mark("changed-file-verification");
        var index = await new ProjectIndexer().IndexAsync(context, cancellationToken: cancellation, cache: cache);
        Mark("index");
        var units = await new UnitProjectLoader().LoadAsync(context, index, cancellation, cache);
        Mark("units-rules");
        WeaponWorkspaceData? weapons = null;
        if (index.Modules.Any(m => m.Key is "weapons" or "ammo" && m.CanScan))
            weapons = await new WeaponProjectLoader().LoadAsync(context, index, units, cancellation, cache);
        Mark("weapons");
        void TrackNames()
        {
            foreach (var file in context.LocalisationDictionaries.Concat(units.Localisation.UnitsCsvPaths)) ProjectReadScope.Track(file);
        }
        DivisionWorkspaceData? divisions = null;
        if (index.Modules.Any(m => m.Key == "divisions" && m.CanScan && m.Availability != ModuleAvailability.ParseError))
        {
            var loaded = await Task.Run(() => ProjectReadScope.Memo("divisions:" + VanillaNames.Revision, () =>
            { TrackNames(); return DivisionProjectLoader.Load(context, index, units, cancellation); }), cancellation);
            divisions = loaded with { Units = units };
        }
        Mark("divisions");
        StrategicWorkspace? strategic = null;
        if (index.Modules.Any(m => m.Key is "strategic" or "sp" && m.CanScan))
        {
            var loaded = await Task.Run(() => ProjectReadScope.Memo("strategic:" + VanillaNames.Revision, () =>
            { TrackNames(); return StrategicLoader.Load(context, index, units, cancellation); }), cancellation);
            strategic = loaded.WithUnits(units);
        }
        Mark("strategic");
        Images.TextureChoice[] textures = []; string? pictureDiagnostic = null;
        try
        {
            textures = await Task.Run(() => ProjectReadScope.Memo("unit-textures", () => Images.UnitPictures.Catalog(context.Layout.RootPath)
                .GroupBy(t => t.Key).Where(g => g.Count() == 1 && g.Single().Source.Length > 0)
                .Select(g => new Images.TextureChoice(g.Key, g.Single().Source)).ToArray()), cancellation);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { pictureDiagnostic = ex.Message; }
        Mark("textures");
        cancellation.ThrowIfCancellationRequested();
        if (!reads.IsStable() || !cache.SourcesUnchanged()) throw new IOException("读取期间项目文件发生变化，请重新读取。");
        return new(context, index, units, weapons, divisions, strategic, reads, cache, timings) { PictureTextures = textures, PictureDiagnostic = pictureDiagnostic };
    }
}
