using System.Text;
using WarnoLiteModdingTool.Core.Changes;

namespace WarnoLiteModdingTool.Core.SourceWorkspace;

public sealed record SourceFileEntry(string Path, long Length);
public sealed record SourceCatalog(IReadOnlyList<SourceFileEntry> Files, IReadOnlyList<string> Errors);
public sealed record SourceFileDraft(byte[]? Baseline, string Text, int CodePage = 65001, bool Bom = false, bool ConvertEncoding = false);

public static class SourceFiles
{
    static SourceFiles() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    public static bool IsText(string path) => Path.GetExtension(path).Equals(".ndf", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase);
    public static bool IsNdf(string path) => Path.GetExtension(path).Equals(".ndf", StringComparison.OrdinalIgnoreCase);
    public static string Resolve(string root, string relative)
    {
        relative = ChangePaths.Normalize(relative);
        if (!IsText(relative)) throw new InvalidDataException("仅编辑 NDF/CSV / Only NDF and CSV can be edited");
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var current = root;
        foreach (var part in relative.Split('/'))
        {
            current = Path.Combine(current, part);
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
                current = info.ResolveLinkTarget(true)?.FullName ?? throw new IOException("无法解析链接 / Unresolved link: " + relative);
                if (!current.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("链接越出项目 / Link outside project: " + relative);
            }
        }
        return current;
    }
    public static SourceCatalog Discover(string root, CancellationToken token = default)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)); var files = new List<SourceFileEntry>(); var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var stack = new Stack<string>(); stack.Push(root);
        var fileSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (stack.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var info = new DirectoryInfo(directory);
                var actual = info.Attributes.HasFlag(FileAttributes.ReparsePoint) ? info.ResolveLinkTarget(true)?.FullName : info.FullName;
                if (actual is null || !(actual.Equals(root, StringComparison.OrdinalIgnoreCase) || actual.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))) { errors.Add("项目外链接 / External link: " + directory); continue; }
                if (!seen.Add(actual)) continue;
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        if (Directory.Exists(entry)) { stack.Push(entry); continue; }
                        if (!IsText(entry)) continue;
                        var relative = Path.GetRelativePath(root, entry).Replace('\\', '/'); var resolved = Resolve(root, relative);
                        if (fileSeen.Add(resolved)) files.Add(new(relative, new FileInfo(resolved).Length));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors.Add(entry + ": " + ex.Message); }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors.Add(directory + ": " + ex.Message); }
        }
        return new(files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToArray(), errors);
    }
    public static byte[]? Read(string root, string path) { var full = Resolve(root, path); return File.Exists(full) ? File.ReadAllBytes(full) : null; }
    public static SourceFileDraft Open(string root, string path, int? codePage = null)
    {
        var bytes = Read(root, path); if (bytes is null) return new(null, "");
        var bom = false; var skip = 0; var cp = codePage ?? 65001;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) { cp = 65001; skip = 3; bom = true; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) { cp = 1200; skip = 2; bom = true; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) { cp = 1201; skip = 2; bom = true; }
        var encoding = Encoding.GetEncoding(cp, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        return new(bytes, encoding.GetString(bytes, skip, bytes.Length - skip), cp, bom);
    }
    public static byte[] Encode(string path, SourceFileDraft data)
    {
        if (IsNdf(path))
        {
            if ((data.CodePage != 65001 || data.Bom) && !data.ConvertEncoding) throw new InvalidDataException("请确认转换为 UTF-8 无 BOM / Confirm conversion to UTF-8 without BOM: " + path);
            return new UTF8Encoding(false, true).GetBytes(data.Text);
        }
        var encoding = Encoding.GetEncoding(data.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        return (data.Bom ? encoding.GetPreamble() : []).Concat(encoding.GetBytes(data.Text)).ToArray();
    }
    public static bool Equal(byte[]? a, byte[]? b) => ChangePaths.Equal(a, b);
}
