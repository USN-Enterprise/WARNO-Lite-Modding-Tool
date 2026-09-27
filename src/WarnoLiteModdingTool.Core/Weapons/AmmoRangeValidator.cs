using System.Globalization;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.Core.Weapons;

/// <summary>Validates edited target domains without rewriting inactive or untouched source ranges.</summary>
public static class AmmoRangeValidator
{
    public static void Validate(AmmoRecord ammo, IReadOnlyDictionary<string, string> final)
    {
        foreach (var domain in new[] { "ground", "heli", "air", "projectile" })
        {
            var prefix = "ammo.range." + domain;
            var low = prefix + ".min";
            var high = prefix + ".max";
            var minimum = final.GetValueOrDefault(low);
            var maximum = final.GetValueOrDefault(high);
            if (Same(minimum, ammo.Field(low)?.DisplayValue) && Same(maximum, ammo.Field(high)?.DisplayValue)) continue;

            if (!Read(minimum, out var min) || !Read(maximum, out var max) || min < 0 || max < 0)
                throw new TransactionValidationException("射程范围缺失或数值无效：" + ammo.Name + " / " + prefix);
            // Native ammunition can retain a positive minimum for a domain with no range (maximum 0).
            if (max > 0 && min > max)
                throw new TransactionValidationException("最小射程不能大于最大射程：" + ammo.Name + " / " + prefix);
        }
    }

    private static bool Same(string? left, string? right) => left == right ||
        Read(left, out var a) && Read(right, out var b) && a == b;

    private static bool Read(string? value, out double number) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);
}
