using System.Globalization;
using WarnoLiteModdingTool.Core.Ndf;

namespace WarnoLiteModdingTool.Core.Weapons;

public static class WeaponValueConverter
{
    public static bool TryRead(WeaponFieldDefinition definition, string raw, out string display)
    {
        display = string.Empty;
        switch (definition.ValueKind)
        {
            case WeaponValueKind.Boolean:
                if (string.Equals(raw, "True", StringComparison.OrdinalIgnoreCase)) { display = "是"; return true; }
                if (string.Equals(raw, "False", StringComparison.OrdinalIgnoreCase)) { display = "否"; return true; }
                return false;
            case WeaponValueKind.Reference:
                display = NdfSyntaxDocument.Leaf(NdfSyntaxDocument.Unquote(raw));
                return display.Length > 0;
            case WeaponValueKind.Choice:
                display = NdfSyntaxDocument.Leaf(NdfSyntaxDocument.Unquote(raw));
                return display.Length > 0;
            case WeaponValueKind.Integer:
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) { display = integer.ToString(CultureInfo.InvariantCulture); return true; }
                return false;
            case WeaponValueKind.Decimal:
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) { display = number.ToString("G15", CultureInfo.InvariantCulture); return true; }
                return false;
            case WeaponValueKind.Degrees:
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var radians)) { display = (radians * 180d / Math.PI).ToString("0.###", CultureInfo.InvariantCulture); return true; }
                return false;
            default:
                return false;
        }
    }

    public static bool TryFormat(WeaponFieldValue field, string input, out string normalized, out string raw, out string error)
    {
        normalized = input.Trim();
        raw = string.Empty;
        error = string.Empty;
        switch (field.Definition.ValueKind)
        {
            case WeaponValueKind.Boolean:
                if (normalized is "是" or "True" or "true") { normalized = "是"; raw = "True"; return true; }
                if (normalized is "否" or "False" or "false") { normalized = "否"; raw = "False"; return true; }
                error = "请选择是或否";
                return false;
            case WeaponValueKind.Reference:
                if (!field.Choices.Contains(normalized, StringComparer.Ordinal)) { error = "目标对象不在当前项目可选列表中"; return false; }
                raw = $"$/GFX/Weapon/{normalized}";
                return true;
            case WeaponValueKind.Choice:
                var requestedChoice = normalized;
                var choice = field.Choices.FirstOrDefault(item =>
                    string.Equals(NdfSyntaxDocument.Leaf(NdfSyntaxDocument.Unquote(item)), requestedChoice, StringComparison.OrdinalIgnoreCase));
                if (choice is null) { error = "请选择当前 Mod 中已识别的伤害族"; return false; }
                normalized = NdfSyntaxDocument.Leaf(NdfSyntaxDocument.Unquote(choice));
                raw = choice;
                return true;
            case WeaponValueKind.Integer:
                if (!int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) { error = "请输入整数"; return false; }
                if (field.Definition.NonNegative && integer < 0) { error = "数值不能为负数"; return false; }
                normalized = integer.ToString(CultureInfo.InvariantCulture);
                raw = normalized;
                return true;
            case WeaponValueKind.Decimal:
            case WeaponValueKind.Degrees:
                if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)) { error = "请输入有效数字"; return false; }
                if (field.Definition.NonNegative && number < 0) { error = "数值不能为负数"; return false; }
                normalized = number.ToString("G15", CultureInfo.InvariantCulture);
                raw = field.Definition.ValueKind == WeaponValueKind.Degrees
                    ? (number * Math.PI / 180d).ToString("G17", CultureInfo.InvariantCulture)
                    : normalized;
                return true;
            default:
                error = "不支持的字段类型";
                return false;
        }
    }
}
