namespace WarnoLiteModdingTool.App.Advanced;

public static class EditorMode
{
    public static bool IsAdvanced { get; set; }
    public static bool AdvancedField(string key) => key.StartsWith("structure.", StringComparison.Ordinal) && key != "structure.tags" || key.EndsWith(".family", StringComparison.Ordinal);
    public static bool CanEdit(string key) => key.StartsWith("armor.") && key.EndsWith(".family") ? IsAdvanced && key == "armor.front.family" :
        IsAdvanced || key is not ("structure.upgradeFrom" or "survival.suppression" or "survival.stun" or "recon.vision.low" or "recon.vision.high" or "recon.optics.low" or "recon.optics.high");
}
