using System.IO.Compression;

namespace WarnoLiteModdingTool.Core.Changes;

public static class ChangeCapture
{
    public static ChangePackage Capture(string sourceRoot, string baseline, NumericPolicy policy, IProgress<ChangeProgress>? progress = null, CancellationToken cancel = default)
    {
        using var source = new Inputs(sourceRoot, false);
        using var basis = new Inputs(baseline, File.Exists(baseline));
        var payloads = new Dictionary<string, byte[]>(); var files = new List<ChangeFile>(); var inventory = new List<InventoryFile>();
        var issues = new List<string>();
        var paths = source.Paths.Union(basis.Paths, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        long size = 0;
        for (var i = 0; i < paths.Length; i++)
        {
            cancel.ThrowIfCancellationRequested(); var path = paths[i]; progress?.Report(new("对比文件 / Comparing files", i, paths.Length, path));
            var after = source.Read(path); var before = basis.Read(path);
            var known = !basis.IsZip || before is not null || path.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase) &&
                (path.StartsWith("GameData/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("CommonData/", StringComparison.OrdinalIgnoreCase));
            var b = known ? ContentStamp.Of(before) : new ContentStamp(FilePresence.Unknown); var a = ContentStamp.Of(after);
            inventory.Add(new(path, b, a));
            if (known && ChangePaths.Equal(before, after)) continue;
            if (!known) issues.Add("基础未覆盖 / Baseline does not cover: " + path);
            string? Put(byte[]? bytes, string side)
            {
                if (bytes is null) return null;
                size += bytes.LongLength; if (size > ChangePackageStore.MaxTotalBytes) throw new IOException("修改内容超过 1 GiB 限制 / Changed payload exceeds 1 GiB");
                var key = $"payload/{files.Count:D6}-{side}"; payloads.Add(key, bytes); return key;
            }
            files.Add(new(path, b, a, Put(before, "before"), Put(after, "after")));
        }
        // Re-enumeration and content verification detect edits and additions during capture.
        source.Verify(cancel); basis.Verify(cancel);
        progress?.Report(new("分析完成 / Analysis complete", paths.Length, paths.Length, ""));
        return new(new("WLMTChanges", 1, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, policy,
            Path.GetFileName(Path.TrimEndingDirectorySeparator(sourceRoot)), Path.GetFileName(Path.TrimEndingDirectorySeparator(baseline)),
            issues.Count == 0, issues, source.ExcludedPaths.Concat(basis.ExcludedPaths).Distinct().Order().ToArray(), inventory, files), payloads);
    }

    internal sealed class Inputs : IDisposable
    {
        private readonly string _root;
        private readonly ZipArchive? _zip;
        private readonly Dictionary<string, ZipArchiveEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ContentStamp> _read = new(StringComparer.OrdinalIgnoreCase);
        public bool IsZip => _zip is not null;
        public string[] Paths { get; }
        public List<string> ExcludedPaths { get; } = [];
        public Inputs(string root, bool zip)
        {
            _root = Path.GetFullPath(root); ChangePaths.NoLinks(_root);
            if (zip)
            {
                _zip = ZipFile.OpenRead(_root);
                try
                {
                    if (_zip.Entries.Count > ChangePackageStore.MaxEntries) throw new InvalidDataException("ZIP 条目过多 / Too many ZIP entries");
                    long length = 0;
                    foreach (var entry in _zip.Entries)
                    {
                        if (entry.FullName.EndsWith('/')) continue;
                        var path = ChangePaths.Normalize(entry.FullName); length += entry.Length;
                        if (length > ChangePackageStore.MaxTotalBytes || entry.Length > ChangePackageStore.MaxFileBytes) throw new InvalidDataException("ZIP 超出读取限制 / ZIP exceeds read limits");
                        if (ChangePaths.Excluded(path)) { ExcludedPaths.Add(path); continue; }
                        if (!_entries.TryAdd(path, entry)) throw new InvalidDataException("ZIP 路径重名 / Duplicate ZIP path: " + path);
                    }
                    // A base.zip must actually carry the official NDF tree, not an arbitrary partial archive.
                    if (!_entries.Keys.Any(p => p.StartsWith("GameData/", StringComparison.OrdinalIgnoreCase)) ||
                        !_entries.Keys.Any(p => p.StartsWith("CommonData/", StringComparison.OrdinalIgnoreCase)) ||
                        _entries.Keys.Any(p => !p.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidDataException("请选择完整官方 base.zip 或基础 Mod 目录 / Select the complete official base.zip or a baseline Mod folder");
                    Paths = _entries.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
                }
                catch { _zip.Dispose(); throw; }
            }
            else
            {
                if (!Directory.Exists(_root)) throw new DirectoryNotFoundException(_root);
                if (!Directory.Exists(Path.Combine(_root, "GameData")) && !Directory.Exists(Path.Combine(_root, "CommonData")))
                    throw new InvalidDataException("请选择包含 GameData 或 CommonData 的 Mod 根目录 / Select a Mod root containing GameData or CommonData");
                Paths = Enumerate().ToArray();
            }
        }
        private IEnumerable<string> Enumerate()
        {
            var result = new List<string>(); var pending = new Stack<string>(); pending.Push(_root);
            while (pending.TryPop(out var directory))
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.OrdinalIgnoreCase))
                {
                    var path = ChangePaths.Normalize(Path.GetRelativePath(_root, entry));
                    if (ChangePaths.Excluded(path)) { ExcludedPaths.Add(path); continue; }
                    ChangePaths.NoLinks(entry);
                    if (Directory.Exists(entry)) pending.Push(entry); else result.Add(path);
                    if (result.Count > ChangePackageStore.MaxEntries) throw new IOException("文件过多 / Too many files");
                }
            return result.Order(StringComparer.OrdinalIgnoreCase);
        }
        public byte[]? Read(string path)
        {
            byte[]? bytes = null;
            if (_zip is not null)
            {
                if (_entries.TryGetValue(path, out var entry)) bytes = ChangePackageStore.ReadEntry(entry, ChangePackageStore.MaxFileBytes);
            }
            else
            {
                var full = ChangePaths.Resolve(_root, path);
                if (File.Exists(full))
                {
                    if (new FileInfo(full).Length > ChangePackageStore.MaxFileBytes) throw new IOException("文件超过 256 MiB / File exceeds 256 MiB: " + path);
                    bytes = File.ReadAllBytes(full);
                }
            }
            _read[path] = ContentStamp.Of(bytes); return bytes;
        }
        public void Verify(CancellationToken cancel)
        {
            if (_zip is not null) return; // Open archive retains its read handle without write sharing.
            if (!Paths.SequenceEqual(Enumerate(), StringComparer.OrdinalIgnoreCase)) throw new IOException("扫描期间文件清单变化 / File inventory changed during capture");
            foreach (var pair in _read.ToArray())
            {
                cancel.ThrowIfCancellationRequested();
                if (ContentStamp.Of(Read(pair.Key)) != pair.Value) throw new IOException("扫描期间文件变化 / File changed during capture: " + pair.Key);
            }
        }
        public void Dispose() => _zip?.Dispose();
    }
}
