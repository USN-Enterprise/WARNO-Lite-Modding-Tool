using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Rules;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.Core.Changes;
public static partial class ChangeMerge
{
    // Reuse existing business rules, but retain identities so one old problem
    // does not exempt a different object's newly introduced invalid quantity.
    public static IReadOnlyDictionary<string,string> SourceKnownIssues(string path,string text)
    {
        var issues=new Dictionary<string,string>(StringComparer.Ordinal);
        if(path.EndsWith(".csv",StringComparison.OrdinalIgnoreCase))
        {
            var csv=SemicolonCsvDocument.Parse(text);
            if(csv.Rows.FirstOrDefault()?.Fields.FirstOrDefault()?.Value=="TOKEN")
                foreach(var group in csv.Rows.Skip(1).Where(r=>r.Fields.Count>0).GroupBy(r=>r.Fields[0].Value).Where(g=>g.Count()>1))issues["dictionary:"+group.Key]="Duplicate dictionary token: "+group.Key+" · "+group.Count();
            return issues;
        }
        var doc=new NdfSyntaxDocument(text);ValidateLexicalEnd(text);
        void Quantities(string identity,string source)
        {
            foreach(var cell in Cells(source).Values.Where(c=>c.Numeric))
                try { ValidateQuantity(cell); }
                catch(Exception ex) when(ex is InvalidDataException or OverflowException or FormatException) { issues[identity+":"+cell.Key]=ex.Message+" · "+cell.Raw; }
        }
        foreach(var obj in Objects(text))Quantities(obj.Key,obj.Value.Text);
        Quantities("$file",text);
        try { ValidateMapKeys(doc,path); } catch(InvalidDataException ex) { issues["map:"+ex.Message]=ex.Message; }
        foreach(var rule in RuleCatalog.All.Where(r=>r.RelativePath.Equals(path,StringComparison.OrdinalIgnoreCase)&&r.Number!=54))
        {
            RuleGroup group;try {group=RuleWorkspace.Read(rule,text);}catch(InvalidDataException){continue;}
            try {RuleWorkspace.Validate(group,group.Cells.ToDictionary(c=>c.Key,c=>c.Raw));}
            catch(InvalidDataException ex) {issues["rule:"+rule.Number]=ex.Message;}
        }
        return issues;
    }
}
