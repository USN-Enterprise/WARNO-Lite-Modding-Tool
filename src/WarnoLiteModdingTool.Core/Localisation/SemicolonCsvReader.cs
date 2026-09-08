using System.Text;

namespace WarnoLiteModdingTool.Core.Localisation;

public static class SemicolonCsvReader
{
    public static IReadOnlyList<IReadOnlyList<string>> Read(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];
            if (quoted)
            {
                if (character == '"')
                {
                    if (index + 1 < source.Length && source[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(character);
                }

                continue;
            }

            switch (character)
            {
                case '"' when field.Length == 0:
                    quoted = true;
                    break;
                case ';':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    if (index + 1 < source.Length && source[index + 1] == '\n')
                    {
                        index++;
                    }

                    FinishRow(rows, row, field);
                    break;
                case '\n':
                    FinishRow(rows, row, field);
                    break;
                default:
                    field.Append(character);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            FinishRow(rows, row, field);
        }

        return rows;
    }

    private static void FinishRow(
        ICollection<IReadOnlyList<string>> rows,
        ICollection<string> row,
        StringBuilder field)
    {
        row.Add(field.ToString());
        field.Clear();
        if (row.Any(value => value.Length > 0))
        {
            rows.Add(row.ToArray());
        }

        row.Clear();
    }
}
