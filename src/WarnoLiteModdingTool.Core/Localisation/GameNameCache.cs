using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WarnoLiteModdingTool.Core.Localisation;

/// <summary>Reads only display dictionaries; never extracts archive paths onto disk.</summary>
public sealed class GameNameCache(string cachePath)
{
    public sealed record Source(string Path, long Size, long Modified);
    public sealed record Snapshot(int Version, string Root, Source[] Sources, Dictionary<string, Dictionary<string, string>> Names);
    public static readonly string[] Keys = ["US/UNITS", "US/COMPANIES", "US/PLATOONS", "SC/UNITS", "SC/COMPANIES", "SC/PLATOONS"];

    public Snapshot Load(string? gameRoot, bool force = false, CancellationToken cancellation = default)
    {
        Snapshot? cached = null;
        try
        {
            cached = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(cachePath));
            if (cached is null || cached.Version != 1 || !Complete(cached.Names) || cached.Sources is null) cached = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        if (string.IsNullOrWhiteSpace(gameRoot))
            return cached ?? throw new InvalidDataException("请在设置中选择 WARNO 游戏目录以加载原版名称。");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot));
        var dataRoot = Path.Combine(root, "Data", "PC");
        if (!Directory.Exists(dataRoot))
        {
            if (!force && cached is not null && string.Equals(cached.Root, root, StringComparison.OrdinalIgnoreCase)) return cached;
            throw new InvalidDataException("所选目录缺少 WARNO Data/PC。");
        }
        var sources = Directory.EnumerateFiles(dataRoot, "ZZ_1.dat", SearchOption.AllDirectories)
            .Select(p => new FileInfo(p)).Select(f => new Source(Path.GetRelativePath(dataRoot, f.FullName), f.Length, f.LastWriteTimeUtc.Ticks))
            .OrderBy(s => Revision(s.Path), StringComparer.Ordinal).ThenBy(s => s.Path, StringComparer.Ordinal).ToArray();
        cancellation.ThrowIfCancellationRequested();
        if (!force && cached is not null && string.Equals(cached.Root, root, StringComparison.OrdinalIgnoreCase) && cached.Sources.SequenceEqual(sources)) return cached;
        var names = new Dictionary<string, Dictionary<string, string>>();
        foreach (var source in sources)
        {
            cancellation.ThrowIfCancellationRequested();
            ReadArchive(Path.Combine(dataRoot, source.Path), names, cancellation);
        }
        if (!Complete(names)) throw new InvalidDataException("游戏数据缺少完整的中英文名称词典。");
        // Do not publish a cache assembled while Steam was replacing its sources.
        if (sources.Any(s => { var f = new FileInfo(Path.Combine(dataRoot, s.Path)); return !f.Exists || f.Length != s.Size || f.LastWriteTimeUtc.Ticks != s.Modified; }))
            throw new IOException("游戏数据正在更新，请稍后刷新名称。");
        var result = new Snapshot(1, root, sources, names);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(cachePath))!);
        var temporary = cachePath + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(result), new UTF8Encoding(false));
            cancellation.ThrowIfCancellationRequested();
            File.Move(temporary, cachePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return result;
    }

    private static string Revision(string path) => string.Join("/", path.Replace('\\', '/').Split('/').Where(s => ulong.TryParse(s, out _)).Select(s => ulong.Parse(s).ToString("D20", System.Globalization.CultureInfo.InvariantCulture)));
    private static bool Complete(Dictionary<string, Dictionary<string, string>>? names) => names is not null && Keys.All(k => names.TryGetValue(k, out var rows) && rows is not null && rows.Count > 0 && rows.All(p => ulong.TryParse(p.Key, out _) && p.Value is not null));

    public static void ReadArchive(string path, Dictionary<string, Dictionary<string, string>> result, CancellationToken cancellation = default)
    {
        using var file = File.OpenRead(path);
        var header = new byte[80]; file.ReadExactly(header);
        var version = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
        if (!header.AsSpan(0, 4).SequenceEqual("edat"u8) || version is not (2 or 3)) throw new InvalidDataException("不支持的 EDat 版本。");
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(version == 2 ? 25 : 8));
        var size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(version == 2 ? 29 : 12));
        var baseOffset = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(version == 2 ? 33 : 16));
        if (size == 0 && offset <= file.Length) return;
        if (size < 9 || size > 20_000_000 || (ulong)offset + size > (ulong)file.Length) throw new InvalidDataException("EDat 目录越界。");
        var directory = new byte[(int)size]; file.Position = offset; file.ReadExactly(directory);
        if (!directory.AsSpan(0, 9).SequenceEqual(new byte[] { 9,0,0,0,0,0,0,0,0 })) throw new InvalidDataException("不支持的 EDat 目录格式。");
        Walk(9, directory.Length, "", 0);
        void Walk(int start, int end, string prefix, int depth)
        {
            if (depth > 64) throw new InvalidDataException("EDat 目录嵌套过深。");
            while (start < end)
            {
                cancellation.ThrowIfCancellationRequested();
                if (end - start < 8) throw new InvalidDataException("EDat 节点截断。");
                var first = BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(start));
                var length = BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(start + 4));
                var stopLong = length == 0 ? end : (long)start + length;
                if (stopLong <= start || stopLong > end) throw new InvalidDataException("EDat 节点越界。");
                var stop = (int)stopLong;
                var nameStart = start + (first != 0 ? 8 : 40);
                if (nameStart >= stop) throw new InvalidDataException("EDat 文件名截断。");
                var nul = Array.IndexOf(directory, (byte)0, nameStart, stop - nameStart);
                if (nul < 0) throw new InvalidDataException("EDat 文件名未结束。");
                var name = prefix + new UTF8Encoding(false, true).GetString(directory, nameStart, nul - nameStart);
                if (first != 0)
                {
                    var child = (long)start + first;
                    if (child <= nul || child >= stop) throw new InvalidDataException("EDat 子目录越界。");
                    Walk((int)child, stop, name, depth + 1);
                }
                else
                {
                    var parts = name.Replace('\\', '/').ToUpperInvariant().Split('/');
                    var language = parts.FirstOrDefault(p => p is "US" or "SC");
                    var kind = parts[^1].EndsWith(".DIC", StringComparison.Ordinal) ? parts[^1][..^4] : "";
                    if (version == 3 && (kind.EndsWith("-US", StringComparison.Ordinal) || kind.EndsWith("-SC", StringComparison.Ordinal)))
                    { language = kind[^2..]; kind = kind[..^3]; }
                    if (language is not null && kind is "UNITS" or "COMPANIES" or "PLATOONS")
                    {
                        var position = BinaryPrimitives.ReadUInt64LittleEndian(directory.AsSpan(start + 8));
                        var bytes = BinaryPrimitives.ReadUInt64LittleEndian(directory.AsSpan(start + 16));
                        if (bytes > 32_000_000 || position > (ulong)file.Length || (ulong)baseOffset + position > (ulong)file.Length || bytes > (ulong)file.Length - baseOffset - position) throw new InvalidDataException("EDat 词典载荷越界。");
                        var payload = new byte[(int)bytes]; file.Position = (long)(baseOffset + position); file.ReadExactly(payload);
                        if (version == 2 ? !MD5.HashData(payload).AsSpan().SequenceEqual(directory.AsSpan(start + 24, 16)) : Crc32(payload) != BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(start + 24))) throw new InvalidDataException("EDat 词典校验失败。");
                        result[language + "/" + kind] = ReadTrad(payload);
                    }
                }
                start = stop;
            }
        }
    }

    public static uint Crc32(byte[] bytes)
    {
        uint crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0);
        }
        return ~crc;
    }

    public static Dictionary<string, string> ReadTrad(byte[] data)
    {
        if (data.Length < 8 || !data.AsSpan(0, 4).SequenceEqual("TRAD"u8)) throw new InvalidDataException("不是 TRAD 名称词典。");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        var tableEnd = 8UL + count * 16UL;
        if (tableEnd > (ulong)data.Length) throw new InvalidDataException("TRAD 索引越界。");
        var rows = new Dictionary<string, string>();
        for (var i = 0; i < count; i++)
        {
            var record = data.AsSpan(8 + i * 16, 16);
            var key = BinaryPrimitives.ReadUInt64LittleEndian(record);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(record[8..]);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
            if (offset < tableEnd || offset + length * 2UL > (ulong)data.Length) throw new InvalidDataException("TRAD 文本越界。");
            var text = new UnicodeEncoding(false, false, true).GetString(data, (int)offset, checked((int)length * 2));
            if (!rows.TryAdd(key.ToString(System.Globalization.CultureInfo.InvariantCulture), text)) throw new InvalidDataException("TRAD 名称键重复。");
        }
        return rows;
    }
}
