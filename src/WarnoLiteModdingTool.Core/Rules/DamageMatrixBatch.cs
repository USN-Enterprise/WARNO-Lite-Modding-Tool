using System.Globalization;

namespace WarnoLiteModdingTool.Core.Rules;

public enum DamageMath { Set, Multiply, Add }
public static class DamageMatrixBatch
{
    public static IReadOnlyList<DamageChange> Preview(IEnumerable<DamageCell> cells, IReadOnlyDictionary<string,string> current,
        DamageMath operation, string operand, string minimum, string maximum, bool keepZero)
    {
        if (!decimal.TryParse(operand, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
            throw new InvalidDataException("请输入有限数值");
        decimal? min = minimum.Trim().Length == 0 ? null : DamageWorkspace.Number(minimum);
        decimal? max = maximum.Trim().Length == 0 ? null : DamageWorkspace.Number(maximum);
        if (min > max) throw new InvalidDataException("下限不能大于上限");
        var changes = new List<DamageChange>();
        foreach (var cell in cells)
        {
            var raw = current.GetValueOrDefault(cell.Key, cell.Raw);
            DamageWorkspace.Number(cell.Raw); var value = DamageWorkspace.Number(raw);
            var after = keepZero && value == 0 ? 0 : operation switch
            {
                DamageMath.Set => amount, DamageMath.Multiply => checked(value * amount), DamageMath.Add => checked(value + amount),
                _ => throw new InvalidDataException("未知计算方式")
            };
            if (!(keepZero && value == 0))
            {
                if (min.HasValue) after = Math.Max(min.Value, after);
                if (max.HasValue) after = Math.Min(max.Value, after);
            }
            DamageWorkspace.Number(DamageWorkspace.Format(after));
            changes.Add(new(cell.Key, cell.Raw, DamageWorkspace.Format(after)));
        }
        return changes;
    }
}
