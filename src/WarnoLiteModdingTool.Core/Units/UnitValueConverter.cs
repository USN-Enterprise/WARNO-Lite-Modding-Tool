using System.Globalization;
using System.Text;
using WarnoLiteModdingTool.Core.Ndf;

namespace WarnoLiteModdingTool.Core.Units;

public static class UnitValueConverter
{
    public static bool TryReadDisplay(
        UnitFieldDefinition definition,
        NdfSyntaxDocument document,
        NdfValueSpan value,
        out string display,
        out string error)
    {
        var raw = document.Raw(value);
        switch (definition.ValueKind)
        {
            case UnitValueKind.Integer:
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                {
                    display = integer.ToString(CultureInfo.InvariantCulture);
                    error = string.Empty;
                    return true;
                }

                return Failure("值不是直接整数；常量引用暂不改写", out display, out error);

            case UnitValueKind.Decimal:
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    display = number.ToString("0.##########", CultureInfo.InvariantCulture);
                    error = string.Empty;
                    return true;
                }

                return Failure("值不是直接数字；常量引用暂不改写", out display, out error);

            case UnitValueKind.EcmPercent:
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var ecm) && ecm <= 0 && ecm >= -1)
                {
                    var percent = ecm == 0 ? 0 : -ecm * 100;
                    display = percent.ToString("0.####", CultureInfo.InvariantCulture);
                    error = string.Empty;
                    return true;
                }

                return Failure("ECM 原值不在已验证的 -1 到 0 范围", out display, out error);

            case UnitValueKind.QuotedString:
                display = NdfSyntaxDocument.Unquote(raw);
                error = string.Empty;
                return true;

            case UnitValueKind.Choice:
            case UnitValueKind.UnitReference:
                display = NdfSyntaxDocument.Leaf(NdfSyntaxDocument.Unquote(raw));
                error = string.Empty;
                return display.Length > 0 || Failure("值为空", out display, out error);

            case UnitValueKind.StringList:
            case UnitValueKind.PathList:
                if (definition.Key == "structure.specialties" && !raw.TrimStart().StartsWith('['))
                    return Failure("特性列表不是直接数组，引用暂不改写", out display, out error);
                var elements = document.ReadArrayElements(value)
                    .Select(document.Raw)
                    .Select(NdfSyntaxDocument.Unquote)
                    .Select(item => definition.ValueKind == UnitValueKind.PathList ? NdfSyntaxDocument.Leaf(item) : item)
                    .Where(item => item.Length > 0)
                    .ToArray();
                display = string.Join(", ", elements);
                error = string.Empty;
                return true;

            default:
                return Failure("未支持的值类型", out display, out error);
        }
    }

    public static bool TryFormatTarget(
        UnitFieldValue field,
        string input,
        out string normalizedDisplay,
        out string rawTarget,
        out string error)
    {
        var definition = field.Definition;
        var trimmed = input.Trim();
        switch (definition.ValueKind)
        {
            case UnitValueKind.Integer:
                if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) ||
                    (definition.NonNegative && integer < 0))
                {
                    return Failure("请输入不小于 0 的整数", out normalizedDisplay, out rawTarget, out error);
                }

                normalizedDisplay = integer.ToString(CultureInfo.InvariantCulture);
                rawTarget = normalizedDisplay;
                error = string.Empty;
                return true;

            case UnitValueKind.Decimal:
                if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
                    !double.IsFinite(number) || (definition.NonNegative && number < 0))
                {
                    return Failure(definition.NonNegative ? "请输入不小于 0 的数字" : "请输入有效数字", out normalizedDisplay, out rawTarget, out error);
                }

                normalizedDisplay = number.ToString("0.##########", CultureInfo.InvariantCulture);
                rawTarget = normalizedDisplay.Contains('.', StringComparison.Ordinal)
                    ? normalizedDisplay
                    : normalizedDisplay + ".0";
                error = string.Empty;
                return true;

            case UnitValueKind.EcmPercent:
                if (!double.TryParse(trimmed.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) ||
                    !double.IsFinite(percent) || percent < 0 || percent > 100)
                {
                    return Failure("ECM 请输入 0 到 100 之间的百分比", out normalizedDisplay, out rawTarget, out error);
                }

                normalizedDisplay = percent.ToString("0.####", CultureInfo.InvariantCulture);
                var rawEcm = -percent / 100;
                rawTarget = rawEcm == 0
                    ? "0.0"
                    : rawEcm.ToString("0.####", CultureInfo.InvariantCulture);
                error = string.Empty;
                return true;

            case UnitValueKind.QuotedString:
                if (trimmed.Length == 0)
                {
                    return Failure("值不能为空", out normalizedDisplay, out rawTarget, out error);
                }

                normalizedDisplay = trimmed;
                rawTarget = QuoteLike(field.RawValue, trimmed);
                error = string.Empty;
                return true;

            case UnitValueKind.Choice:
            case UnitValueKind.UnitReference:
                var choice = field.Choices.FirstOrDefault(item =>
                    string.Equals(item.Display, trimmed, StringComparison.CurrentCultureIgnoreCase));
                if (choice is null)
                {
                    return Failure("请选择当前项目中已识别的值", out normalizedDisplay, out rawTarget, out error);
                }

                normalizedDisplay = choice.Display;
                rawTarget = choice.RawValue;
                error = string.Empty;
                return true;

            case UnitValueKind.StringList:
                var strings = SplitList(trimmed);
                var requiredIdentityTags = SplitList(definition.Key == "structure.tags" ? field.DisplayValue : "")
                    .Where(item => item.StartsWith("UNITE_", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (definition.Key == "structure.tags" && strings.Count == 0 || strings.Any(item => item.Any(char.IsWhiteSpace)))
                {
                    return Failure("请填写逗号分隔且不含空格的标签", out normalizedDisplay, out rawTarget, out error);
                }
                if (requiredIdentityTags.Any(required => !strings.Contains(required, StringComparer.OrdinalIgnoreCase)))
                {
                    return Failure("Unit 身份标签不能删除", out normalizedDisplay, out rawTarget, out error);
                }

                normalizedDisplay = string.Join(", ", strings);
                rawTarget = strings.Count == 0 ? "[]" : FormatList(strings.Select(Quote));
                error = string.Empty;
                return true;

            case UnitValueKind.PathList:
                var selectedPaths = SplitList(trimmed);
                var pathChoices = selectedPaths.Select(selected => field.Choices.FirstOrDefault(item =>
                    string.Equals(item.Display, selected, StringComparison.CurrentCultureIgnoreCase))).ToArray();
                if (pathChoices.Length == 0 || pathChoices.Any(item => item is null))
                {
                    return Failure("请只选择当前 Mod 中已识别的单位类别", out normalizedDisplay, out rawTarget, out error);
                }

                normalizedDisplay = string.Join(", ", pathChoices.Select(item => item!.Display));
                rawTarget = FormatList(pathChoices.Select(item => item!.RawValue));
                error = string.Empty;
                return true;

            default:
                return Failure("该字段暂不支持草稿编辑", out normalizedDisplay, out rawTarget, out error);
        }
    }

    private static IReadOnlyList<string> SplitList(string value) =>
        value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string FormatList(IEnumerable<string> values) =>
        "[ " + string.Join(", ", values) + ", ]";

    private static string QuoteLike(string currentRaw, string value)
    {
        var quote = currentRaw.TrimStart().StartsWith('"') ? '"' : '\'';
        var escaped = value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(quote.ToString(), $"\\{quote}", StringComparison.Ordinal);
        return $"{quote}{escaped}{quote}";
    }

    private static string Quote(string value) =>
        $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static bool Failure(string message, out string display, out string error)
    {
        display = string.Empty;
        error = message;
        return false;
    }

    private static bool Failure(string message, out string display, out string raw, out string error)
    {
        display = string.Empty;
        raw = string.Empty;
        error = message;
        return false;
    }
}
