using System.Text.Json;
using System.Text.Json.Nodes;
using WarnoLiteModdingTool.Core.Divisions;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.Core.Units;

public static class UnitDraftLinks
{
    public static bool Touches(DraftOperation op, string name)
    {
        if (op.ObjectName == name || op.SelectedUnitNames?.Contains(name) == true) return true;
        bool Contains(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            // JSON schemas embed unit names as values, NDF keeps them as atoms.
            if (text.Contains('"'))
            {
                try
                {
                    using var json = JsonDocument.Parse(text);
                    bool Has(JsonElement e) => e.ValueKind switch
                    {
                        JsonValueKind.String => e.GetString() == name || HasNdf(e.GetString() ?? ""),
                        JsonValueKind.Array => e.EnumerateArray().Any(Has),
                        JsonValueKind.Object => e.EnumerateObject().Any(p => Has(p.Value)),
                        _ => false
                    };
                    return Has(json.RootElement);
                }
                catch (JsonException) { }
            }
            return HasNdf(text);
        }
        bool HasNdf(string text) => new NdfSyntaxDocument(text).Tokens.Any(t => t.Text[0] is not ('\'' or '"') && NdfSyntaxDocument.Leaf(t.Text) == name);
        return Contains(op.TargetRaw) || Contains(op.BaselineRaw);
    }
    public static bool Lifecycle(DraftOperation op) => op.TargetKind is DraftTargetKind.UnitRename or DraftTargetKind.UnitDelete or DraftTargetKind.UnitCapabilities or DraftTargetKind.UnitRegistration;
    public static IReadOnlyList<DraftOperation> Expand(IReadOnlyList<DraftOperation> selected, IReadOnlyList<DraftOperation> all)
    {
        var result = selected.ToDictionary(o => o.Id);
        var combined = all.Concat(selected).GroupBy(o => o.Id).Select(g => g.Last()).ToArray();
        bool changed;
        do
        {
            changed = false;
            foreach (var identity in combined.Where(Lifecycle))
            {
                var related = combined.Where(o => Touches(o, identity.ObjectName)).ToArray();
                if (!related.Any(o => result.ContainsKey(o.Id))) continue;
                foreach (var op in related.Append(identity)) if (result.TryAdd(op.Id, op)) changed = true;
            }
        } while (changed);
        return result.Values.ToArray();
    }
    public static async Task ReplaceCreationAsync(DraftStore store, DraftOperation original, DraftOperation updated)
    {
        if (original.TargetKind != DraftTargetKind.UnitCreate || updated.TargetKind != DraftTargetKind.UnitCreate) throw new InvalidOperationException("需要创建草稿");
        var oldName = original.ObjectName; var newName = updated.ObjectName;
        var upserts = new List<DraftOperation> { updated }; var removals = new List<string> { original.Id };
        foreach (var op in store.Operations.Where(o => o.Id != original.Id && Touches(o, oldName)))
        {
            removals.Add(op.Id); upserts.Add(RenamePendingReference(op, oldName, newName));
        }
        await store.ApplyBatchAsync(upserts, removals);
    }
    private static DraftOperation RenamePendingReference(DraftOperation op, string oldName, string newName)
    {
        string Atom(string raw)
        {
            var doc = new NdfSyntaxDocument(raw);
            return UnitProjectGraph.Patch(raw, doc.Tokens.Where(t => t.Text[0] is not ('\'' or '"') && NdfSyntaxDocument.Leaf(t.Text) == oldName)
                .Select(t => new TextReplacement(t.Start, t.Length, t.Text, t.Text[..(t.Text.Length - oldName.Length)] + newName, "待创建引用")));
        }
        string Payload(string raw)
        {
            if (op.TargetKind is not (DraftTargetKind.UnitCreate or DraftTargetKind.DivisionPlan or DraftTargetKind.StrategicPlan or DraftTargetKind.StrategicPack)) return Atom(raw);
            var node = JsonNode.Parse(raw) ?? throw new InvalidDataException("草稿为空");
            void Visit(JsonNode? item, string property = "")
            {
                if (item is JsonObject obj)
                    foreach (var pair in obj.ToArray())
                    {
                        if (pair.Value is JsonValue value && value.TryGetValue<string>(out var text) && text == oldName && new[] { "unit", "transport", "mother", "id" }.Contains(pair.Key.ToLowerInvariant())) obj[pair.Key] = newName;
                        else Visit(pair.Value, pair.Key.ToLowerInvariant());
                    }
                else if (item is JsonArray array)
                    for (var i = 0; i < array.Count; i++)
                        if (property is "availabletransports" or "standoutunits" && array[i] is JsonValue value && value.TryGetValue<string>(out var text) && text == oldName) array[i] = newName;
                        else Visit(array[i]);
            }
            Visit(node); return node.ToJsonString();
        }
        var objectName = op.ObjectName == oldName ? newName : op.ObjectName;
        var result = op with { ObjectName = objectName, Id = DraftOperation.CreateId(op.TargetKind, op.RelativeSourceFile, objectName, op.FieldKey),
            SelectedUnitNames = op.SelectedUnitNames?.Select(n => n == oldName ? newName : n).ToArray(),
            GroupId = op.GroupId?.EndsWith(":" + oldName, StringComparison.Ordinal) == true ? op.GroupId[..^oldName.Length] + newName : op.GroupId,
            TargetRaw = Payload(op.TargetRaw), TargetValue = Payload(op.TargetValue), UpdatedUtc = DateTimeOffset.UtcNow };
        if (Touches(result with { BaselineRaw = "" }, oldName)) throw new TransactionValidationException("此关联草稿需先调整：" + op.Summary);
        return result;
    }
    public static async Task CancelCreationAsync(DraftStore store, DraftOperation creation)
    {
        var name = creation.ObjectName; var removals = new List<string> { creation.Id }; var upserts = new List<DraftOperation>();
        foreach (var op in store.Operations.Where(o => o.Id != creation.Id && Touches(o, name)))
        {
            if (op.ObjectName == name) { removals.Add(op.Id); continue; }
            if (op.TargetKind == DraftTargetKind.DivisionPlan && DivisionDraftCodec.TryDeserialize(op.TargetRaw, out var state, out _))
            {
                if (state.DefaultDeck.Any(p => p.Unit == name || p.Transport == name)) throw new TransactionValidationException("请先解除卡组草稿引用：" + op.Summary);
                var rules = state.UnitRules.Where(r => r.Unit != name).Select(r => r with { AvailableTransports = r.AvailableTransports.Where(t => t != name).ToArray() }).ToArray();
                if (rules.Any(r => !r.AvailableWithoutTransport && r.AvailableTransports.Count == 0)) throw new TransactionValidationException("取消创建会使运输为空，请先调整师草稿");
                var json = DivisionDraftCodec.Serialize(state with { UnitRules = rules, StandoutUnits = state.StandoutUnits.Where(n => n != name).ToArray() });
                upserts.Add(op with { TargetRaw = json, TargetValue = json, UpdatedUtc = DateTimeOffset.UtcNow }); continue;
            }
            throw new TransactionValidationException("请先解除依赖草稿：" + op.Summary);
        }
        await store.ApplyBatchAsync(upserts, removals);
    }
}
