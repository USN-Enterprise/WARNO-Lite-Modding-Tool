using System.Globalization;
using WarnoLiteModdingTool.Core.Batch;
using WarnoLiteModdingTool.Core.Drafts;
namespace WarnoLiteModdingTool.Core.Units;
public static class VisionRatio
{
    public static (IReadOnlyList<DraftOperation> Upserts, IReadOnlyList<string> RemoveOperationIds) Preview(UnitRecord unit, string key, string input, IReadOnlyList<DraftOperation> existing)
    {
        if (key is not ("recon.vision.standard" or "recon.optics.standard")) throw new InvalidOperationException("不支持的视野字段");
        var prefix = key[..^8];
        var fields = new[] {"standard", "low", "high"}.Select(s => unit.Field(prefix + s)).ToArray();
        if (fields.Any(f => f?.CanEdit != true) || !decimal.TryParse(fields[0]!.DisplayValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var baseline) || baseline == 0)
            throw new InvalidOperationException("原始标准值为零或三项不是可编辑数值，请在专业模式分别修改");
        if (!decimal.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var target) || target < 0) throw new InvalidOperationException("请输入非负数值");
        var adds = new List<DraftOperation>(); var removes = new List<string>();
        var group = "vision:" + unit.Name + ":" + prefix;
        foreach (var field in fields)
        {
            var old = decimal.Parse(field!.DisplayValue, CultureInfo.InvariantCulture);
            var value = field.Definition.Key == key ? target : Math.Round(old * target / baseline, 0, MidpointRounding.AwayFromZero);
            var preview = UnitBatchPlanner.Preview(new(group, [unit], field.Definition.Key, UnitBatchOperation.Set, value.ToString(CultureInfo.InvariantCulture), UnitBatchRounding.None, null, null, existing));
            if (preview.Errors.Count != 0) throw new InvalidOperationException(string.Join("；", preview.Errors));
            adds.AddRange(preview.Upserts.Select(o => o with { GroupId = group })); removes.AddRange(preview.RemoveOperationIds);
        }
        return (adds, removes);
    }
}
