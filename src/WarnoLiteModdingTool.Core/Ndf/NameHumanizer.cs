using System.Text.RegularExpressions;

namespace WarnoLiteModdingTool.Core.Ndf;

public static partial class NameHumanizer
{
    private static readonly string[] KnownPrefixes =
    [
        "Descriptor_Unit_",
        "WeaponDescriptor_",
        "Descriptor_Deck_Division_",
        "Descriptor_",
        "Ammo_"
    ];

    public static string Humanize(string internalName)
    {
        var value = internalName;
        foreach (var prefix in KnownPrefixes)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = value[prefix.Length..];
                break;
            }
        }

        value = UnderscoreRuns().Replace(value, " ").Trim();
        return string.IsNullOrWhiteSpace(value) ? internalName : value;
    }

    [GeneratedRegex("_+")]
    private static partial Regex UnderscoreRuns();
}

