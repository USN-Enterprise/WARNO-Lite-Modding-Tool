namespace WarnoLiteModdingTool.Core.Changes;

public static class ChangePaths
{
    public static string Normalize(string path)
    {
        path = path.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains(':') || path.IndexOfAny(['\0', '*', '?']) >= 0 ||
            path.Split('/').Any(p => p is "" or "." or ".." || p.EndsWith(' ') || p.EndsWith('.') ||
                System.Text.RegularExpressions.Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
            throw new InvalidDataException("无效的相对路径 / Invalid relative path: " + path);
        return path;
    }
    public static string Resolve(string root, string relative)
    {
        root = Path.GetFullPath(root); relative = Normalize(relative);
        NoLinks(root);
        var current = root;
        foreach (var part in relative.Split('/')) { current = Path.Combine(current, part); NoLinks(current); }
        return current;
    }
    public static void NoLinks(string path)
    {
        var full = Path.GetFullPath(path);
        for (var cursor = full; cursor is not null; cursor = Path.GetDirectoryName(cursor))
            if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("不跟随目录链接 / Links are not followed: " + cursor);
    }
    public static bool Equal(byte[]? left, byte[]? right) => left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);
    public static bool Excluded(string path)
    {
        var parts = Normalize(path).Split('/'); var top = parts[0];
        return new[] { ".warno-editor", ".git", ".svn", ".base", "Gen" }.Contains(top, StringComparer.OrdinalIgnoreCase) ||
            parts.Length == 1 && top.Equals("base.zip", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".wlmtchanges", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("logs/WARNO Lite Modding Tool-", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
    }
    public static void Writable(string path)
    {
        path = Normalize(path);
        if (Excluded(path) || path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            new[] { ".bat", ".cmd", ".ps1", ".py" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("此文件仅记录，不自动还原 / Record only; automatic restore is disabled: " + path);
    }
}
