namespace WarnoLiteModdingTool.Core.Localisation;

public sealed class UnitLocalisationCatalog
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _namesByToken;
    private readonly HashSet<string> _allTokens;

    public UnitLocalisationCatalog(
        string projectRoot,
        IReadOnlyList<string> unitsCsvPaths,
        IReadOnlyDictionary<string, IReadOnlyList<string>> namesByToken,
        IReadOnlyList<string> diagnostics)
    {
        ProjectRoot = projectRoot;
        UnitsCsvPaths = unitsCsvPaths;
        _namesByToken = namesByToken;
        _allTokens = namesByToken.Keys.ToHashSet(StringComparer.Ordinal);
        Diagnostics = diagnostics;
        UniqueUnitsCsvPath = unitsCsvPaths.Count == 1 ? unitsCsvPaths[0] : null;
    }

    public string ProjectRoot { get; }

    public IReadOnlyList<string> UnitsCsvPaths { get; }

    public string? UniqueUnitsCsvPath { get; }

    public IReadOnlyList<string> Diagnostics { get; }

    public bool TryResolve(string? token, out string name)
    {
        if (token is not null &&
            _namesByToken.TryGetValue(token, out var values) &&
            values.Count == 1)
        {
            name = values[0];
            return true;
        }

        name = string.Empty;
        return false;
    }

    public bool IsTokenAmbiguous(string? token) =>
        token is not null &&
        _namesByToken.TryGetValue(token, out var values) &&
        values.Count > 1;

    public string GenerateToken()
    {
        VanillaNames.RequireAvailable();
        string token;
        do
        {
            token = "WL" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        }
        while (_allTokens.Contains(token) || VanillaNames.Lookup("UNITS",token) is not null);

        _allTokens.Add(token);
        return token;
    }

    public void AddKnownTokens(IEnumerable<string> tokens)
    {
        foreach (var token in tokens.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            _allTokens.Add(token);
        }
    }
}
