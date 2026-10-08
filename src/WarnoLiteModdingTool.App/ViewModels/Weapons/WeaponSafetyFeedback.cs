using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.App.ViewModels.Weapons;

internal static class WeaponSafetyFeedback
{
    public static void Refresh(IEnumerable<WeaponFieldViewModel> fields, WeaponWorkspaceData data, IReadOnlyList<DraftOperation> operations, string unit = "", string weapon = "", int mount = -1)
    {
        var rows = fields.ToArray();
        foreach (var row in rows) row.SafetyText = "";
        try
        {
            var ammo = rows.FirstOrDefault(f => f.Field.Definition.Owner == WeaponFieldOwner.Ammo)?.Field.OwnerObjectName;
            if (ammo is null) return;
            var issues = WeaponSafety.Preview(data, operations, ammo, unit, weapon, mount);
            foreach (var row in rows)
            {
                var related = issues.Where(i => i.Field.Split(" / ").Contains(row.Field.Definition.FieldName)).ToArray();
                row.SafetyText = string.Join("\n", related.Select(i =>
                {
                    var message = i.Message; var split = message.IndexOf(" / ", StringComparison.Ordinal);
                    if (i.Rule == "supply.info") message = Localisation.UiText.T("每发补给") + message[message.LastIndexOf(':')..];
                    if (split >= 0 && i.Rule != "supply.info") message = Localisation.UiText.Current.English ? message[(split + 3)..] : message[..split];
                    var prefix = i.Level == WeaponSafetyLevel.Error ? Localisation.UiText.T("需配套修改后应用") : i.Rule == "supply.info" ? "" : Localisation.UiText.T("请核对");
                    return (prefix.Length == 0 ? "" : prefix + " · ") + message + (i.Object == ammo ? "" : " · " + i.Object);
                }));
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            foreach (var row in rows.Where(r => r.Field.Definition.FieldName is "Ammunition" or "ShotsCountPerSalvo"))
                row.SafetyText = Localisation.UiText.T("关联预检未完成，请核对草稿") + " · " + ex.Message;
        }
    }
}
