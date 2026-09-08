using System.Globalization;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Localisation;
using WarnoLiteModdingTool.Core.Transactions;
namespace WarnoLiteModdingTool.Core.Rules;
public static class RuleDependencies
{
    public static string AdjustAndValidate(string file, string baseline, string candidate, IReadOnlySet<int> changed)
    {
        var doc = new NdfSyntaxDocument(candidate);
        var patches = new List<TextReplacement>();
        const string w = "TWargameTunableConstante", t = "TTunableConstante", a = "TStrategicTunableConstante";
        NdfValueSpan Field(string type, string field) => RuleWorkspace.Locate(doc, type, field);
        decimal Number(NdfValueSpan span) => decimal.Parse(doc.Raw(span), CultureInfo.InvariantCulture);
        IReadOnlyList<NdfValueSpan> Array(string type, string field) => doc.ReadArrayElements(Field(type, field));
        void Member(string type, string choices, string def)
        {
            if (!Array(type, choices).Select(Number).Contains(Number(Field(type, def)))) throw new TransactionValidationException(def + " 必须属于 " + choices + "；请一并修改后应用");
        }
        void Index(string type, string choices, string index)
        {
            var n = Number(Field(type,index));
            if (n < 0 || n != decimal.Truncate(n) || n >= Array(type,choices).Count) throw new TransactionValidationException(index + " 超出选项范围");
        }
        void Patch(NdfValueSpan span, string value) => patches.Add(new(doc.StartOffset(span),doc.Length(span),doc.Raw(span),value,"联动关联表"));
        if (changed.Overlaps(new[]{1,2}))
        {
            Member(w,"ArgentInitialSetting","DefaultArgentInitial");
            var money = Array(w,"ArgentInitialSetting").Select(Number).ToArray();
            if (money.Distinct().Count() != money.Length) throw new TransactionValidationException("初始资金选项不能重复");
            if (changed.Contains(2))
            {
                var map = Field(w,"VictoryTypeDestructionLevelsTable");
                var existing = doc.ReadMapEntries(map).Select(e => Number(e.Key)).ToHashSet();
                var additions = money.Where(n => !existing.Contains(n)).ToArray();
                if (additions.Length > 0)
                {
                    var raw = doc.Raw(map); var closing = raw.LastIndexOf(']');
                    var items = string.Join("", additions.Select(n => "(" + n.ToString(CultureInfo.InvariantCulture) + ", " + doc.Raw(Field(w,"DefaultDestructionScoreToReachSetting")) + "),"));
                    Patch(map, raw[..closing] + (doc.NeedsArraySeparator(map) ? "," : "") + items + raw[closing..]);
                }
            }
        }
        if (changed.Overlaps(new[]{3,4})) Member(t,"TimeLimitTable","DefaultTimeLimitInMinutes");
        if (changed.Contains(5)) Index(w,"ConquestPossibleScores","ConquestPointsDefaultIndex");
        if (changed.Contains(11)) Index(w,"IncomeMultiplier","DefaultIncomeMultiplierIndex");
        if (changed.Contains(12)) Member(w,"UpkeepPercentAvailableSettings","UpkeepPercentDefaultSetting");
        if (changed.Overlaps(new[]{33,34,35}))
        {
            var total = Array(a,"BattleNbMaxPawnByRole").Sum(Number);
            if (total < 1 || total > 64 || total != decimal.Truncate(total)) throw new TransactionValidationException("参战总名额必须为 1–64 的整数");
            foreach(var name in new[]{"DefaultStartingTicketsPointsByPawnNumber","DefaultTicketsPointsIncomeByPawnNumber"})
            {
                var outer = Array(a,name);
                if(outer.Count != 2) throw new TransactionValidationException(name + " 必须包含攻方和守方两行");
                foreach(var row in outer) Resize(row,(int)total+1);
            }
            foreach(var name in new[]{"MaxTicketFactorByPawnNumber","UpkeepPercentByPawnNumber"}) Resize(Field(a,name),(int)total);
        }
        return SemicolonCsvDocument.ApplyReplacements(candidate, patches);
        void Resize(NdfValueSpan span,int length)
        {
            var values = doc.ReadArrayElements(span).Select(doc.Raw).ToList();
            if (values.Count == length) return;
            if (!changed.Contains(33) || values.Count == 0) throw new TransactionValidationException("参战数量与关联表长度不一致");
            if (values.Any(v=>!decimal.TryParse(v,NumberStyles.Float,CultureInfo.InvariantCulture,out _))) throw new TransactionValidationException("关联表包含非数值，无法联动");
            while(values.Count < length) values.Add(values[^1]);
            Patch(span, "[" + string.Join(", ",values.Take(length)) + "]");
        }
    }
}
