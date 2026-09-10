using WarnoLiteModdingTool.Core.Ndf;

namespace WarnoLiteModdingTool.Core.Strategic;

public static class StrategicType
{
    // The game's map-symbol category, independent of the broader BattleRole.
    public static string Read(string? pawn)
    {
        if (pawn is null) return "";
        var doc = new NdfSyntaxDocument(pawn);
        var span = StrategicSyntax.Field(doc, "TBUCKToolAlternativeValues_TUIValueTextureNameFromTEugBMutableInteger", "Values");
        if (span is null || !doc.Raw(span).TrimStart().StartsWith('[')) return "";
        var values = doc.ReadArrayElements(span).Select(v => NdfSyntaxDocument.Unquote(doc.Raw(v)))
            .Select(v => v.StartsWith("Texture_STRATEGIC_RTS_H_", StringComparison.Ordinal) ? v["Texture_STRATEGIC_RTS_H_".Length..] : v.StartsWith("Texture_STRATEGIC_", StringComparison.Ordinal) ? v["Texture_STRATEGIC_".Length..] : v)
            .Distinct(StringComparer.Ordinal).ToArray();
        return string.Join(" · ", values);
    }
}
