using System.Text.RegularExpressions;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.Core.Changes;

public static partial class ChangeMerge
{
    private enum ScalarKind { None, QuotedString, Enumeration, Boolean }

    // Only existing editor definitions identify literal fields. References, token
    // identities, list elements and constructor arguments cannot be forced here.
    private static ScalarKind ScalarType(string type, string field, string? map)
    {
        if (map is not null) return ScalarKind.None;
        var unit = UnitFieldDefinitions.All.FirstOrDefault(d => d.Selector.ModuleType == type && d.Selector.FieldName == field &&
            d.Selector.MapKey is null && d.Selector.ArgumentName is null && d.Selector.NestedField is null);
        if (unit?.ValueKind == UnitValueKind.QuotedString) return ScalarKind.QuotedString;
        // Experience is also rendered as a choice by the editor, but stores an object reference.
        if (unit?.ValueKind == UnitValueKind.Choice && unit.Key is "structure.coalition" or "structure.factory") return ScalarKind.Enumeration;
        if (type is "TAmmunitionDescriptor" or "TAmmunitionMissileDescriptor" &&
            WeaponFieldDefinitions.Ammo.Any(d => d.FieldName == field && d.MapKey is null && d.ArgumentName is null && d.Constructor is null && d.ValueKind == WeaponValueKind.Boolean))
            return ScalarKind.Boolean;
        return ScalarKind.None;
    }

    private static bool SupportedScalar(Cell b, Cell m, Cell n)
    {
        if (b.Numeric || m.Numeric || n.Numeric || b.Literal == ScalarKind.None || b.Literal != m.Literal || b.Literal != n.Literal) return false;
        var values = new[] { b.Raw, m.Raw, n.Raw };
        if (b.Literal == ScalarKind.Boolean) return values.All(v => v is "True" or "False");
        if (b.Literal == ScalarKind.Enumeration)
            return values.All(v => Regex.IsMatch(v, @"^E[A-Za-z0-9_]+/[A-Za-z_][A-Za-z0-9_]*$")) && values.Select(v => v[..v.IndexOf('/')]).Distinct().Count() == 1;
        return values.All(v =>
        {
            if (v.Length < 2 || v[0] is not ('\'' or '"') || v[^1] != v[0]) return false;
            ValidateLexicalEnd(v);
            var tokens = new NdfSyntaxDocument(v).Tokens;
            return tokens.Count == 1 && tokens[0].Text == v;
        });
    }

    private sealed class ScalarReview(string path, IReadOnlyList<ChangeTextDecision> decisions)
    {
        private readonly HashSet<ChangeTextDecision> _used = [];
        private bool _unresolved;
        public bool Resolved(ChangeTextConflict conflict) => _used.Any(d => d.Conflict == conflict);
        public string Resolve(string name, Cell b, Cell m, Cell n, out ChangeTextConflict conflict, out string status)
        {
            conflict = new(path, name, b.Key, b.Raw, m.Raw, n.Raw) { Kind = ChangeConflictKind.NdfScalar };
            var choice = decisions.SingleOrDefault(d => d.Conflict.Kind == ChangeConflictKind.NdfScalar &&
                d.Conflict.Path.Equals(path, StringComparison.OrdinalIgnoreCase) && d.Conflict.Token == name && d.Conflict.Column == b.Key);
            if (choice is null)
            {
                _unresolved = true; status = "字段冲突：待选择 / Field conflict: choose an outcome"; return n.Raw;
            }
            if (choice.Conflict != conflict) throw new InvalidDataException("冲突依据已变化，请清除旧选择重新核对 / Conflict evidence changed; clear the previous choice and review again");
            _used.Add(choice);
            status = choice.Choice == TextConflictChoice.KeepTarget ? "保留新版（原修改未还原） / Keep target (recorded change omitted)" : "使用记录内容 / Use recorded value";
            return choice.Choice == TextConflictChoice.KeepTarget ? n.Raw : m.Raw;
        }
        public void Complete()
        {
            if (_used.Count != decisions.Count) throw new InvalidDataException("冲突选择已不对应当前字段，请清除后重新核对 / Choices no longer match current fields; clear and review again");
            if (_unresolved) throw new InvalidDataException("NDF 字段双方均修改，请在明细中逐项选择 / NDF fields changed on both sides; choose each outcome in the details");
        }
    }
}
