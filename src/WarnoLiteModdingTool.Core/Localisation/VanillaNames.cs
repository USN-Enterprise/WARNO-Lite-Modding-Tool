using System.Text.Json;
namespace WarnoLiteModdingTool.Core.Localisation;

public static class VanillaNames
{
    private static Dictionary<string, Dictionary<string,string>> Data = new();
    public static long Revision { get; private set; }
    public static bool Available => GameNameCache.Keys.All(k => Data.ContainsKey(k));
    public static void Replace(Dictionary<string, Dictionary<string,string>> names)
    {
        lock (Translations) { if (ReferenceEquals(Data, names)) return; Data = names; Translations.Clear(); Revision++; }
    }
    public static bool UnitsAvailable => new[] { "US/UNITS", "SC/UNITS" }.All(k => Data.TryGetValue(k, out var rows) && rows.Count > 0);
    public static void ReplaceUnits(Dictionary<string, Dictionary<string, string>> names)
    {
        lock (Translations)
        {
            var next = new Dictionary<string, Dictionary<string,string>>(Data);
            foreach (var key in new[] { "US/UNITS", "SC/UNITS" })
                if (names.TryGetValue(key, out var values)) next[key] = values; else next.Remove(key);
            Data = next; Translations.Clear(); Revision++;
        }
    }
    public static void RequireUnitsAvailable()
    {
        if (!UnitsAvailable) throw new InvalidOperationException("请先提取原版中英文UNITS词典，以核对正文token占用");
    }
    public static void RequireAvailable()
    {
        if (!Available) throw new InvalidOperationException("请先在设置中加载原版名称，再创建或修改名称。");
    }
    public static string? Lookup(string kind, string token, string language = "US")
    {
        ulong key = 0;
        if (token.Length > 10) return null;
        foreach (var c in token.ToUpperInvariant())
        {
            var value = c is >= 'A' and <= 'Z' ? c - 54 : c is >= '0' and <= '9' ? c - 47 : -1;
            if (value < 0) return null;
            key = (key << 6) | (uint)value;
        }
        return Data.GetValueOrDefault(language + "/" + kind)?.GetValueOrDefault(key.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
    private static readonly Dictionary<string, Dictionary<string,string>> Translations = new();
    public static string Chinese(string kind, string english)
    {
        lock (Translations)
        {
            if (!Translations.TryGetValue(kind, out var map))
            {
                var en = Data.GetValueOrDefault("US/"+kind) ?? []; var zh = Data.GetValueOrDefault("SC/"+kind) ?? [];
                map = en.GroupBy(p => p.Value).Where(g => g.Select(p => zh.GetValueOrDefault(p.Key, p.Value)).Distinct().Count() == 1)
                    .ToDictionary(g => g.Key, g => zh.GetValueOrDefault(g.First().Key, g.Key)); Translations[kind] = map;
            }
            return map.GetValueOrDefault(english, english);
        }
    }
}
