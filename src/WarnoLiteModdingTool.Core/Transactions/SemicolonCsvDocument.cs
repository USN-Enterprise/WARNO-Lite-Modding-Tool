using System.Text;

namespace WarnoLiteModdingTool.Core.Transactions;

public sealed class SemicolonCsvDocument
{
    private SemicolonCsvDocument(string source, IReadOnlyList<CsvRowSpan> rows)
    {
        Source = source;
        Rows = rows;
    }

    public string Source { get; }

    public IReadOnlyList<CsvRowSpan> Rows { get; }

    public static SemicolonCsvDocument Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var rows = new List<CsvRowSpan>();
        var fields = new List<CsvFieldSpan>();
        var rowStart = 0;
        var fieldStart = 0;
        var quoted = false;

        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];
            if (quoted)
            {
                if (character != '"')
                {
                    continue;
                }

                if (index + 1 < source.Length && source[index + 1] == '"')
                {
                    index++;
                }
                else
                {
                    quoted = false;
                }

                continue;
            }

            if (character == '"' && index == fieldStart)
            {
                quoted = true;
                continue;
            }

            if (character == ';')
            {
                fields.Add(CreateField(source, fieldStart, index - fieldStart));
                fieldStart = index + 1;
                continue;
            }

            if (character is not ('\r' or '\n'))
            {
                continue;
            }

            fields.Add(CreateField(source, fieldStart, index - fieldStart));
            var lineEndLength = character == '\r' && index + 1 < source.Length && source[index + 1] == '\n' ? 2 : 1;
            rows.Add(new CsvRowSpan(rowStart, index - rowStart, lineEndLength, fields.ToArray()));
            fields.Clear();
            index += lineEndLength - 1;
            rowStart = index + 1;
            fieldStart = rowStart;
        }

        if (quoted)
        {
            throw new InvalidDataException("CSV 存在未闭合的引号字段。");
        }

        if (rowStart < source.Length || fields.Count > 0)
        {
            fields.Add(CreateField(source, fieldStart, source.Length - fieldStart));
            rows.Add(new CsvRowSpan(rowStart, source.Length - rowStart, 0, fields.ToArray()));
        }

        return new SemicolonCsvDocument(source, rows);
    }

    public static string Quote(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    public static string ApplyReplacements(string source, IEnumerable<TextReplacement> replacements)
    {
        var ordered = replacements.OrderByDescending(item => item.Offset).ToArray();
        var previousStart = source.Length;
        var builder = new StringBuilder(source);
        foreach (var replacement in ordered)
        {
            if (replacement.Offset < 0 || replacement.Length < 0 || replacement.Offset + replacement.Length > source.Length)
            {
                throw new InvalidDataException($"文本补丁越界：{replacement.Description}");
            }

            if (replacement.Offset + replacement.Length > previousStart)
            {
                throw new InvalidDataException($"文本补丁重叠：{replacement.Description}");
            }

            var current = source.Substring(replacement.Offset, replacement.Length);
            if (!string.Equals(current, replacement.Expected, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"文本补丁基线不匹配：{replacement.Description}");
            }

            builder.Remove(replacement.Offset, replacement.Length);
            builder.Insert(replacement.Offset, replacement.Target);
            previousStart = replacement.Offset;
        }

        return builder.ToString();
    }

    private static CsvFieldSpan CreateField(string source, int start, int length)
    {
        var raw = source.Substring(start, length);
        var value = raw;
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
        {
            value = raw[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
        }

        return new CsvFieldSpan(start, length, raw, value);
    }
}

public sealed record CsvRowSpan(
    int Offset,
    int Length,
    int LineEndLength,
    IReadOnlyList<CsvFieldSpan> Fields);

public sealed record CsvFieldSpan(
    int Offset,
    int Length,
    string Raw,
    string Value);

public sealed record TextReplacement(
    int Offset,
    int Length,
    string Expected,
    string Target,
    string Description);
