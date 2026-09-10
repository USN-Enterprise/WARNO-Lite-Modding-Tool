using System.Globalization;
using WarnoLiteModdingTool.Core.Indexing;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;
using static WarnoLiteModdingTool.Core.Strategic.StrategicSyntax;

namespace WarnoLiteModdingTool.Core.Strategic;

public sealed class StrategicLoader
{
    public const string DirectoryPath = "GameData/Generated/Gameplay/Decks/";
    public Task<StrategicWorkspace> LoadAsync(ModProjectContext context, ProjectIndexResult index, UnitWorkspaceData units,
        CancellationToken cancellationToken = default) => Task.Run(() => Load(context, index, units, cancellationToken), cancellationToken);

    internal static StrategicWorkspace Load(ModProjectContext context, ProjectIndexResult index, UnitWorkspaceData units, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? overrides = null)
    {
        var workspace = new StrategicWorkspace { Root = context.Layout.RootPath, Units = units };
        LoadNames(context, workspace);
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pawns = new List<StrategicSource>();
        foreach (var obj in index.Objects.Where(o => o.ModuleKey == "strategic"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!files.TryGetValue(obj.RelativeSourceFile, out var text))
                    files[obj.RelativeSourceFile] = text = overrides?.GetValueOrDefault(obj.RelativeSourceFile.Replace('\\', '/')) ?? File.ReadAllText(obj.SourceFile);
                var source = new StrategicSource(obj with { RelativeSourceFile = obj.RelativeSourceFile.Replace('\\', '/') }, text.Substring(obj.CharacterOffset, obj.CharacterLength));
                if (obj.TypeName == "DeckPackDescriptor")
                {
                    var xp = Read(source.Text, obj.TypeName, "Xp", "0");
                    if (!int.TryParse(xp, out var level)) throw new InvalidDataException("无法解析战略 Pack 老练度");
                    workspace.Packs.Add(obj.Name, new(source, Leaf(Read(source.Text, obj.TypeName, "Unit")),
                        Leaf(Read(source.Text, obj.TypeName, "Transport")), level));
                }
                else if (obj.TypeName == "TDeckCombatGroupDescriptor") workspace.CombatGroups.Add(obj.Name, source);
                else if (obj.TypeName == "TDeckDescriptor") workspace.Decks.Add(obj.Name, source);
                else if (obj.TypeName == "TEntityDescriptor") pawns.Add(source);
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IOException)
            { workspace.Diagnostics.Add($"{obj.Name}: {ex.Message}"); }
        }
        workspace.HasRoster = new[] { "StrategicPacks.ndf", "StrategicDecks.ndf", "StrategicCombatGroups.ndf" }
            .All(name => File.Exists(Path.Combine(workspace.Root, DirectoryPath, name)));
        var pawnLinks = pawns.GroupBy(p => NdfSyntaxDocument.Unquote(Read(p.Text, "TDeckModuleDescriptor", "DeckIdentifier")))
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        foreach (var deck in workspace.Decks.Values)
        {
            var doc = new NdfSyntaxDocument(deck.Text);
            var list = Field(doc, "TDeckDescriptor", "DeckCombatGroupList");
            if (list is not null)
                foreach (var group in doc.ReadArrayElements(list).Select(e => Leaf(doc.Raw(e))))
                    workspace.GroupUsers[group] = workspace.GroupUsers.GetValueOrDefault(group) + 1;
        }
        foreach (var deck in workspace.Decks.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var identifier = NdfSyntaxDocument.Unquote(Read(deck.Text, "TDeckDescriptor", "DeckIdentifier"));
            var linked = pawnLinks.GetValueOrDefault(identifier) ?? [];
            workspace.DeckUsers[deck.Info.Name] = linked.Length;
            foreach (var pawn in linked.Cast<StrategicSource?>().DefaultIfEmpty(null))
            {
                var templates = new Dictionary<string, string>(StringComparer.Ordinal);
                var companies = new List<StrategicCompany>();
                string? error = null;
                try { companies.AddRange(ReadRoster(workspace, deck, templates)); }
                catch (Exception ex) when (ex is InvalidDataException or ArgumentException or KeyNotFoundException or OverflowException)
                { error = ex.Message; workspace.Diagnostics.Add($"{deck.Info.Name}: {error}"); }
                var values = ReadPawn(pawn);
                foreach (var pair in values)
                {
                    if (!workspace.Choices.TryGetValue(pair.Key, out var choices)) workspace.Choices[pair.Key] = choices = new(StringComparer.Ordinal);
                    choices.Add(pair.Value);
                }
                var token = pawn is null ? identifier : NdfSyntaxDocument.Unquote(Read(pawn.Text, "StrategicUIModuleDescriptor", "NameToken"));
                var name = workspace.Name("UNITS", token);
                var displayName = name == token ? Localisation.VanillaNames.Lookup("UNITS", token) ?? identifier.Replace("pion_", "").Replace('_', ' ') : name;
                workspace.Records.Add(new(pawn?.Info.Name ?? deck.Info.Name, displayName, deck, pawn,
                    new(companies, values, name), templates, error) { BattalionType=StrategicType.Read(pawn?.Text), Country=pawn is null?"":NdfSyntaxDocument.Unquote(Read(pawn.Text,"TTypeUnitModuleDescriptor","MotherCountry")), Coalition=pawn is null?"":Leaf(Read(pawn.Text,"TTypeUnitModuleDescriptor","Coalition")), Division=Leaf(Read(deck.Text,"TDeckDescriptor","DeckDivision")), HasCustomName = workspace.Names.GetValueOrDefault("UNITS")?.ContainsKey(token) == true });
            }
        }
        return workspace;
    }

    public static Dictionary<string, string> ReadPawn(StrategicSource? pawn)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (pawn is null) return values;
        var doc = new NdfSyntaxDocument(pawn.Text);
        foreach (var field in StrategicFields.All)
        {
            if (field.Key == "Movement")
            {
                var references = doc.FindReferences("StrategicMovementDescriptor_");
                if (references.Count == 1) values[field.Key] = references[0].Raw;
            }
            else
            {
                var span = Field(doc, field.Constructor, field.Field);
                if (span is not null) values[field.Key] = doc.Raw(span);
            }
        }
        return values;
    }

    private static IReadOnlyList<StrategicCompany> ReadRoster(StrategicWorkspace workspace, StrategicSource deck, Dictionary<string, string> templates)
    {
        var doc = new NdfSyntaxDocument(deck.Text);
        var packsSpan = Field(doc, "TDeckDescriptor", "DeckPackList") ?? throw new InvalidDataException("缺少 DeckPackList");
        var groupsSpan = Field(doc, "TDeckDescriptor", "DeckCombatGroupList") ?? throw new InvalidDataException("缺少 DeckCombatGroupList");
        var packs = doc.ReadArrayElements(packsSpan).Select(e => Leaf(doc.Raw(e))).ToArray();
        var coverage = new bool[packs.Length];
        var result = new List<StrategicCompany>();
        foreach (var groupSpan in doc.ReadArrayElements(groupsSpan))
        {
            var groupName = Leaf(doc.Raw(groupSpan));
            if (templates.ContainsKey(groupName)) throw new InvalidDataException("同 Deck 重复 CombatGroup，无法唯一编辑");
            var source = workspace.CombatGroups[groupName];
            templates[groupName] = source.Text;
            var cg = new NdfSyntaxDocument(source.Text);
            var smartList = Field(cg, "TDeckCombatGroupDescriptor", "SmartGroupList") ?? throw new InvalidDataException("缺少 SmartGroupList");
            var smartGroups = new List<StrategicGroup>();
            var groupIndex = 0;
            foreach (var element in cg.ReadArrayElements(smartList))
            {
                var raw = cg.Raw(element);
                var key = groupName + "/" + groupIndex++;
                var smart = new NdfSyntaxDocument(raw);
                if (smart.FindConstructors("TDeckSmartGroupDescriptor").Count != 1) throw new InvalidDataException("不支持的 SmartGroup 引用结构");
                templates[key] = raw;
                var tuples = Field(smart, "TDeckSmartGroupDescriptor", "PackIndexUnitNumberList") ?? throw new InvalidDataException("缺少 PackIndexUnitNumberList");
                var slots = new List<StrategicSlot>();
                var tupleNumber = 0;
                foreach (var tuple in smart.ReadMapEntries(tuples))
                {
                    if (!int.TryParse(smart.Raw(tuple.Key), out var start) || !int.TryParse(smart.Raw(tuple.Value), out var count) ||
                        start < 0 || count <= 0 || (long)start + count > packs.Length) throw new InvalidDataException("PackIndex 越界或数量无效");
                    var name = packs[start];
                    for (var i = start; i < start + count; i++)
                    {
                        if (coverage[i] || packs[i] != name) throw new InvalidDataException("PackIndex 重叠或元组覆盖不同 Pack");
                        coverage[i] = true;
                    }
                    var pack = workspace.Packs[name];
                    templates[key + "/" + tupleNumber + "/start"] = start.ToString(CultureInfo.InvariantCulture);
                    slots.Add(new(key + "/" + tupleNumber++, name, pack.Unit, pack.Transport, pack.Xp, count));
                }
                // Reject unknown array elements instead of silently losing them.
                if (slots.Count != smart.ReadArrayElements(tuples).Count) throw new InvalidDataException("无法识别的 PackIndex 元组");
                smartGroups.Add(new(key, workspace.Name("PLATOONS", NdfSyntaxDocument.Unquote(Read(raw, "TDeckSmartGroupDescriptor", "Name"))),
                    Read(raw, "TDeckSmartGroupDescriptor", "IsHQ", "False") == "True", slots));
            }
            result.Add(new(groupName, workspace.Name("COMPANIES", NdfSyntaxDocument.Unquote(Read(source.Text, "TDeckCombatGroupDescriptor", "Name"))),
                Read(source.Text, "TDeckCombatGroupDescriptor", "IsHQ", "False") == "True", smartGroups));
        }
        if (coverage.Any(v => !v)) throw new InvalidDataException("存在未编组 Pack 槽，保留只读以免丢失");
        return result;
    }

    private static string CompanyName(StrategicWorkspace workspace, StrategicSource source, string id)
    {
        var token = NdfSyntaxDocument.Unquote(Read(source.Text,"TDeckCombatGroupDescriptor","Name"));
        var name = workspace.Name("COMPANIES",token); return name == token ? id.Replace("Descriptor_CombatGroup_", "").Replace('_',' ') : name;
    }

    private static void LoadNames(ModProjectContext context, StrategicWorkspace workspace)
    {
        foreach (var kind in new[] { "UNITS", "COMPANIES", "PLATOONS" })
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dictionary in context.LocalisationDictionaries)
            {
                var doc = new NdfSyntaxDocument(File.ReadAllText(dictionary));
                foreach (var span in doc.FindAssignmentsAnywhere("FileName").Concat(doc.FindAssignmentsAnywhere("CsvFile")))
                {
                    var raw = NdfSyntaxDocument.Unquote(doc.Raw(span));
                    if (!raw.EndsWith(kind + ".csv", StringComparison.OrdinalIgnoreCase)) continue;
                    var full = raw.StartsWith("GameData:/", StringComparison.OrdinalIgnoreCase)
                        ? Path.Combine(workspace.Root, "GameData", raw[10..]) : Path.Combine(Path.GetDirectoryName(dictionary)!, raw);
                    var relative = Path.GetRelativePath(workspace.Root, Path.GetFullPath(full));
                    try { paths.Add(TextFileSnapshot.ResolveInsideRoot(workspace.Root, relative)); }
                    catch (InvalidDataException) { workspace.Diagnostics.Add("本地化声明越出目标 Mod"); }
                }
            }
            if (paths.Count != 1) continue;
            var path = paths.Single();
            workspace.CsvPaths[kind] = Path.GetRelativePath(workspace.Root, path).Replace('\\', '/');
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            if (File.Exists(path))
            {
                var csv = TextFileSnapshot.Load(workspace.Root, workspace.CsvPaths[kind], FormalTextFileKind.Csv);
                foreach (var group in SemicolonCsvDocument.Parse(csv.Text).Rows.Where(r => r.Fields.Count >= 2).Skip(1).GroupBy(r => r.Fields[0].Value))
                    if (group.Count() == 1) names[group.Key] = group.Single().Fields[1].Value;
            }
            workspace.Names[kind] = names;
        }
    }
}
