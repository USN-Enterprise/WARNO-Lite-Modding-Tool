using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Projects;

namespace WarnoLiteModdingTool.Core.Localisation;

public sealed class UnitLocalisationLoader
{
    public UnitLocalisationCatalog Load(ModProjectContext context)
    {
        var diagnostics = new List<string>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dictionary in context.LocalisationDictionaries)
        {
            try
            {
                var source = File.ReadAllText(dictionary);
                var document = new NdfSyntaxDocument(source);
                var declarations = document.FindAssignmentsAnywhere("FileName")
                    .Concat(document.FindAssignmentsAnywhere("CsvFile"));
                foreach (var declaration in declarations)
                {
                    var declared = NdfSyntaxDocument.Unquote(document.Raw(declaration));
                    if (!declared.EndsWith("UNITS.csv", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var resolved = ResolveDeclaredPath(context.Layout.RootPath, dictionary, declared);
                    if (resolved is null)
                    {
                        diagnostics.Add($"本地化声明越出 Mod 根目录，已忽略：{declared}");
                    }
                    else
                    {
                        paths.Add(resolved);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add($"无法读取本地化声明 {Path.GetFileName(dictionary)}：{exception.Message}");
            }
        }

        var names = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var rows = SemicolonCsvReader.Read(File.ReadAllText(path));
                foreach (var row in rows.Skip(1).Where(row => row.Count >= 2))
                {
                    var token = row[0].Trim();
                    if (token.Length == 0)
                    {
                        continue;
                    }

                    if (!names.TryGetValue(token, out var values))
                    {
                        values = [];
                        names[token] = values;
                    }

                    values.Add(row[1]);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add($"无法读取 {Path.GetFileName(path)}：{exception.Message}");
            }
        }

        foreach (var duplicate in names.Where(pair => pair.Value.Count > 1))
        {
            diagnostics.Add($"UNITS.csv token 重复：{duplicate.Key}（{duplicate.Value.Count} 行）");
        }

        return new UnitLocalisationCatalog(
            context.Layout.RootPath,
            paths.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            names.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value,
                StringComparer.Ordinal),
            diagnostics);
    }

    private static string? ResolveDeclaredPath(string root, string dictionary, string declared)
    {
        string candidate;
        const string gameDataPrefix = "GameData:/";
        if (declared.StartsWith(gameDataPrefix, StringComparison.OrdinalIgnoreCase))
        {
            candidate = Path.Combine(root, "GameData", declared[gameDataPrefix.Length..].Replace('/', Path.DirectorySeparatorChar));
        }
        else
        {
            candidate = Path.Combine(Path.GetDirectoryName(dictionary)!, declared.Replace('/', Path.DirectorySeparatorChar));
        }

        var resolved = Path.GetFullPath(candidate);
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return resolved.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ? resolved : null;
    }
}
