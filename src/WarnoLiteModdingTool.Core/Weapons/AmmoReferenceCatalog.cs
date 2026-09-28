using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Core.Weapons;

public static class AmmoReferenceCatalog
{
    public sealed record Missile(string File, string Name, bool Exported);
    // Definitions belong to the chosen Mod. No stock archive/runtime fallback.
    public static bool HasDefinitions(string text) => text.Contains("TUIResourceTexture_Common", StringComparison.Ordinal)
        || text.Contains("TMimeticWorldHappeningRegistration", StringComparison.Ordinal) || text.Contains("MinMax_", StringComparison.Ordinal)
        || text.Contains("TGuidedMissileModuleDescriptor", StringComparison.Ordinal);
    public static Dictionary<string, IReadOnlyList<string>> Load(string root, CancellationToken token, out IReadOnlyList<string> dependencies, out IReadOnlyList<Missile> missiles)
    {
        var files = new List<string>();
        var missileObjects = new List<Missile>();
        var textures = new List<string>(); var impacts = new List<string>(); var categories = new List<string>();
        foreach (var path in UnitProjectGraph.InputFiles(root))
        {
            token.ThrowIfCancellationRequested();
            string text;
            try { text = ProjectReadScope.ReadAllText(System.IO.Path.Combine(root, path)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            var texture = text.Contains("TUIResourceTexture_Common", StringComparison.Ordinal);
            var missile = text.Contains("TGuidedMissileModuleDescriptor", StringComparison.Ordinal);
            var impact = text.Contains("TMimeticWorldHappeningRegistration", StringComparison.Ordinal);
            // Category aliases are scalar declarations, not constructor objects.
            var category = text.Contains("MinMax_", StringComparison.Ordinal) && text.Contains(" is ", StringComparison.Ordinal);
            if (!texture && !impact && !category && !missile) continue;
            files.Add(path);
            var doc = new NdfSyntaxDocument(text);
            if (texture || missile)
            {
                var scan = new NdfTopLevelScanner().Scan(text, System.IO.Path.Combine(root, path), "textures", root);
                textures.AddRange(scan.Objects.Where(o => o.TypeName == "TUIResourceTexture_Common").Select(o => o.Name));
                foreach (var obj in scan.Objects.Where(o => o.TypeName == "TEntityDescriptor"))
                {
                    var body = new NdfSyntaxDocument(text, obj.CharacterOffset, obj.CharacterLength);
                    if (body.FindConstructors("TGuidedMissileModuleDescriptor").Count == 1 && body.FindConstructors("TGuidedMissileMovementModuleDescriptor").Count == 1)
                        missileObjects.Add(new(path, obj.Name, body.Tokens[0].Text == "export"));
                }
            }
            if (impact) impacts.AddRange(AmmoProfessionalValidation.ImpactKeys(doc));
            if (category) categories.AddRange(AmmoProfessionalValidation.ScalarKeys(doc).Where(k => k.StartsWith("MinMax_", StringComparison.Ordinal)));
        }
        IReadOnlyList<string> Unique(IEnumerable<string> values, bool quote) => values.GroupBy(v => v).Where(g => g.Count() == 1).Select(g => quote ? "'" + g.Key + "'" : g.Key).Order(StringComparer.Ordinal).ToArray();
        dependencies = files;
        missiles = missileObjects;
        return new() { [AmmoProfessional.Key("InterfaceWeaponTexture")] = Unique(textures, true), [AmmoProfessional.Key("ImpactHappening")] = Unique(impacts, true), [AmmoProfessional.Key("MinMaxCategory")] = Unique(categories, false) };
    }
}
