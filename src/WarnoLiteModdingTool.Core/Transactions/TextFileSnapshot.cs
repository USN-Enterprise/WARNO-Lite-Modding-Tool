using System.Text;

namespace WarnoLiteModdingTool.Core.Transactions;

public enum FormalTextFileKind
{
    Ndf,
    Csv,
    Log
}

public sealed class TextFileSnapshot
{
    private static readonly UTF8Encoding StrictUtf8NoBom = new(false, true);
    private readonly Encoding _encoding;
    private readonly byte[] _preamble;

    private TextFileSnapshot(
        string projectRoot,
        string relativePath,
        string fullPath,
        FormalTextFileKind kind,
        bool existed,
        byte[] originalBytes,
        string text,
        Encoding encoding,
        byte[] preamble,
        DateTime? lastWriteUtc)
    {
        ProjectRoot = projectRoot;
        RelativePath = Normalize(relativePath);
        FullPath = fullPath;
        Kind = kind;
        Existed = existed;
        OriginalBytes = originalBytes;
        Text = text;
        _encoding = encoding;
        _preamble = preamble;
        LastWriteUtc = lastWriteUtc;
        NewLine = DetectNewLine(text);
    }

    public string ProjectRoot { get; }

    public string RelativePath { get; }

    public string FullPath { get; }

    public FormalTextFileKind Kind { get; }

    public bool Existed { get; }

    public byte[] OriginalBytes { get; }

    public string Text { get; }

    public DateTime? LastWriteUtc { get; }

    public string NewLine { get; }

    public static TextFileSnapshot Load(
        string projectRoot,
        string relativePath,
        FormalTextFileKind kind,
        bool allowMissing = false)
    {
        var root = Path.GetFullPath(projectRoot);
        var fullPath = ResolveInsideRoot(root, relativePath);
        if (!File.Exists(fullPath))
        {
            if (!allowMissing)
            {
                throw new FileNotFoundException($"目标文件不存在：{Normalize(relativePath)}", fullPath);
            }

            return new TextFileSnapshot(
                root,
                relativePath,
                fullPath,
                kind,
                false,
                [],
                string.Empty,
                StrictUtf8NoBom,
                [],
                null);
        }

        var bytes = File.ReadAllBytes(fullPath);
        Encoding encoding;
        byte[] preamble;
        int contentOffset;
        if (HasPrefix(bytes, [0xEF, 0xBB, 0xBF]))
        {
            if (kind == FormalTextFileKind.Ndf)
            {
                throw new InvalidDataException($"NDF 文件带 UTF-8 BOM，拒绝在隐式归一化后写入：{Normalize(relativePath)}");
            }

            encoding = new UTF8Encoding(false, true);
            preamble = [0xEF, 0xBB, 0xBF];
            contentOffset = 3;
        }
        else if (HasPrefix(bytes, [0xFF, 0xFE]))
        {
            if (kind == FormalTextFileKind.Ndf)
            {
                throw new InvalidDataException($"NDF 文件不是 UTF-8 无 BOM：{Normalize(relativePath)}");
            }

            encoding = new UnicodeEncoding(false, false, true);
            preamble = [0xFF, 0xFE];
            contentOffset = 2;
        }
        else if (HasPrefix(bytes, [0xFE, 0xFF]))
        {
            if (kind == FormalTextFileKind.Ndf)
            {
                throw new InvalidDataException($"NDF 文件不是 UTF-8 无 BOM：{Normalize(relativePath)}");
            }

            encoding = new UnicodeEncoding(true, false, true);
            preamble = [0xFE, 0xFF];
            contentOffset = 2;
        }
        else
        {
            encoding = StrictUtf8NoBom;
            preamble = [];
            contentOffset = 0;
        }

        string text;
        try
        {
            text = encoding.GetString(bytes, contentOffset, bytes.Length - contentOffset);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException($"文件编码不是受支持的 UTF 文本：{Normalize(relativePath)}", exception);
        }

        return new TextFileSnapshot(
            root,
            relativePath,
            fullPath,
            kind,
            true,
            bytes,
            text,
            encoding,
            preamble,
            File.GetLastWriteTimeUtc(fullPath));
    }

    public byte[] Encode(string text)
    {
        var body = _encoding.GetBytes(text);
        if (_preamble.Length == 0)
        {
            return body;
        }

        var result = new byte[_preamble.Length + body.Length];
        Buffer.BlockCopy(_preamble, 0, result, 0, _preamble.Length);
        Buffer.BlockCopy(body, 0, result, _preamble.Length, body.Length);
        return result;
    }

    public static string ResolveInsideRoot(string projectRoot, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException($"事务路径必须是项目相对路径：{relativePath}");
        }

        var root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"事务路径越出目标 Mod：{relativePath}");
        }

        return fullPath;
    }

    private static bool HasPrefix(IReadOnlyList<byte> bytes, IReadOnlyList<byte> prefix)
    {
        if (bytes.Count < prefix.Count)
        {
            return false;
        }

        for (var index = 0; index < prefix.Count; index++)
        {
            if (bytes[index] != prefix[index])
            {
                return false;
            }
        }

        return true;
    }

    private static string DetectNewLine(string text)
    {
        var lineFeed = text.IndexOf('\n');
        if (lineFeed >= 0)
        {
            return lineFeed > 0 && text[lineFeed - 1] == '\r' ? "\r\n" : "\n";
        }

        return text.Contains('\r', StringComparison.Ordinal) ? "\r" : "\r\n";
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
