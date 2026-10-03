using System.IO.Compression;
using System.Text.Json;

namespace WarnoLiteModdingTool.Core.Changes;

public static class ChangePackageStore
{
    public const long MaxTotalBytes = 1024L * 1024 * 1024;
    public const int MaxFileBytes = 256 * 1024 * 1024;
    public const int MaxEntries = 100_000;
    private const int MaxManifestBytes = 32 * 1024 * 1024;
    public static void Save(ChangePackage package, string path, CancellationToken cancel = default)
    {
        Validate(package); path = Path.GetFullPath(path); ChangePaths.NoLinks(path);
        if (File.Exists(path)) throw new IOException("文件已存在，请另存 / File exists; choose a new name");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
                {
                    using (var output = zip.CreateEntry("manifest.json").Open()) JsonSerializer.Serialize(output, package.Manifest, ChangeJson.Options);
                    foreach (var payload in package.Payloads.OrderBy(p => p.Key, StringComparer.Ordinal))
                    { cancel.ThrowIfCancellationRequested(); using var output = zip.CreateEntry(payload.Key, CompressionLevel.Optimal).Open(); output.Write(payload.Value); }
                }
                stream.Flush(true);
            }
            var reread = Load(temporary, cancel);
            if (reread.Identity != package.Identity) throw new IOException("导出回读不一致 / Export verification failed");
            cancel.ThrowIfCancellationRequested(); File.Move(temporary, path, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static ChangePackage Load(string path, CancellationToken cancel = default)
    {
        ChangePaths.NoLinks(path);
        using var zip = ZipFile.OpenRead(path);
        if (zip.Entries.Count > MaxEntries) throw new InvalidDataException("记录条目过多 / Too many package entries");
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase); long size = 0;
        foreach (var entry in zip.Entries)
        {
            cancel.ThrowIfCancellationRequested(); var key = ChangePaths.Normalize(entry.FullName);
            size += entry.Length;
            if (size > MaxTotalBytes + MaxManifestBytes || entry.Length > MaxFileBytes || !entries.TryAdd(key, entry))
                throw new InvalidDataException("记录条目重复或超出大小限制 / Duplicate or oversized package entries");
        }
        if (!entries.TryGetValue("manifest.json", out var manifestEntry)) throw new InvalidDataException("缺少记录清单 / Missing manifest");
        var manifest = JsonSerializer.Deserialize<ChangeManifest>(ReadEntry(manifestEntry, MaxManifestBytes), ChangeJson.Options)
            ?? throw new InvalidDataException("记录清单为空 / Empty manifest");
        if (manifest.Format != "WLMTChanges" || manifest.Version != 1) throw new InvalidDataException("不支持的记录版本，原文件保留 / Unsupported package version; original retained");
        var payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in entries.Where(e => e.Key != "manifest.json"))
        {
            cancel.ThrowIfCancellationRequested();
            if (!entry.Key.StartsWith("payload/", StringComparison.Ordinal)) throw new InvalidDataException("未知必需条目 / Unknown package entry: " + entry.Key);
            payloads.Add(entry.Key, ReadEntry(entry.Value, MaxFileBytes));
        }
        var package = new ChangePackage(manifest, payloads); Validate(package); return package;
    }
    internal static byte[] ReadEntry(ZipArchiveEntry entry, int limit)
    {
        if (entry.Length > limit) throw new InvalidDataException("压缩内容超限 / Compressed content exceeds limit");
        using var input = entry.Open(); using var output = new MemoryStream(); var buffer = new byte[81920]; int count;
        while ((count = input.Read(buffer)) > 0)
        { if (output.Length + count > limit || output.Length + count > entry.Length) throw new InvalidDataException("压缩内容长度异常 / Invalid expanded length"); output.Write(buffer, 0, count); }
        if (output.Length != entry.Length) throw new InvalidDataException("载荷不完整 / Truncated payload");
        return output.ToArray();
    }
    public static void Validate(ChangePackage package)
    {
        var m = package.Manifest;
        if (m.Format != "WLMTChanges" || m.Version != 1 || !Guid.TryParseExact(m.Id, "N", out _) || !Enum.IsDefined(m.NumericPolicy) ||
            m.Inventory is null || m.Files is null || m.Issues is null || m.Excluded is null || m.Inventory.Count > MaxEntries || m.Files.Count > MaxEntries)
            throw new InvalidDataException("记录格式无效 / Invalid package manifest");
        var inventory = new Dictionary<string, InventoryFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in m.Inventory)
        {
            if (item is null || !inventory.TryAdd(ChangePaths.Normalize(item.Path), item) || ChangePaths.Excluded(item.Path)) throw new InvalidDataException("文件清单重复或范围无效 / Invalid inventory");
            CheckStamp(item.Before); CheckStamp(item.After);
            if (item.After.Presence == FilePresence.Unknown) throw new InvalidDataException("修改后内容未知 / Unknown source state");
        }
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in m.Files)
        {
            if (f is null || !keys.Add(ChangePaths.Normalize(f.Path)) || !inventory.TryGetValue(f.Path, out var item) || item.Before != f.Before || item.After != f.After || f.Before == f.After)
                throw new InvalidDataException("修改与文件清单不一致 / Changes do not match inventory");
            Payload(f.Before, f.BeforePayload); Payload(f.After, f.AfterPayload);
        }
        if (inventory.Values.Any(i => (i.Before != i.After) != keys.Contains(i.Path)) || used.Count != package.Payloads.Count ||
            package.Payloads.Values.Sum(b => b.LongLength) > MaxTotalBytes || m.Complete != (m.Issues.Count == 0 && inventory.Values.All(i => i.Before.Presence != FilePresence.Unknown)))
            throw new InvalidDataException("修改记录不完整或完整性标志无效 / Inconsistent package coverage");
        void Payload(ContentStamp stamp, string? key)
        {
            if (stamp.Presence != FilePresence.Present) { if (key is not null) throw new InvalidDataException("不存在的内容带有载荷 / Unexpected payload"); return; }
            if (key is null || !key.StartsWith("payload/", StringComparison.Ordinal) || ChangePaths.Normalize(key) != key || !used.Add(key) ||
                !package.Payloads.TryGetValue(key, out var bytes) || bytes.Length > MaxFileBytes || ContentStamp.Of(bytes) != stamp)
                throw new InvalidDataException("载荷丢失或校验失败 / Missing or corrupted payload");
        }
        static void CheckStamp(ContentStamp stamp)
        {
            if (stamp is null || !Enum.IsDefined(stamp.Presence) || stamp.Length < 0 || stamp.Length > MaxFileBytes ||
                (stamp.Presence == FilePresence.Present ? stamp.Sha256 is null || !System.Text.RegularExpressions.Regex.IsMatch(stamp.Sha256, "^[0-9A-F]{64}$") : stamp.Length != 0 || stamp.Sha256 is not null))
                throw new InvalidDataException("文件内容身份无效 / Invalid content stamp");
        }
    }
}
