using WarnoLiteModdingTool.Core.Drafts;
namespace WarnoLiteModdingTool.Core.Strategic;

public sealed record StrategicChange(string Path, string Before, string After)
{
    public override string ToString() => $"{Path}：{Before} → {After}";
}
public static class StrategicDiff
{
    public static IReadOnlyList<StrategicChange> Compare(StrategicState before, StrategicState after)
    {
        var a=Flatten(before);var b=Flatten(after);
        return a.Keys.Union(b.Keys).Where(k=>!a.TryGetValue(k,out var old)||!b.TryGetValue(k,out var next)||old.Value!=next.Value)
            .Select(k=>new StrategicChange(b.GetValueOrDefault(k).Label??a[k].Label,a.TryGetValue(k,out var old)?old.Value:"—",b.TryGetValue(k,out var next)?next.Value:"—")).ToArray();
    }
    public static IReadOnlyList<StrategicChange> Compare(DraftOperation op)=>Compare(StrategicCodec.Deserialize(op.BaselineValue),StrategicCodec.Deserialize(op.TargetValue));
    private static Dictionary<string,(string Label,string Value)> Flatten(StrategicState state)
    {
        var result=new Dictionary<string,(string,string)>();
        void Add(string id,string label,object? value)=>result[id]=(label,value?.ToString()??"—");
        string Unit(string raw)=>raw.Length==0?"无":StrategicSyntax.Leaf(raw).Replace("Descriptor_Unit_","").Replace('_',' ');
        Add("name","营/团名称",state.Name);
        foreach(var p in state.PawnValues)Add("pawn/"+p.Key,"棋子 / "+(StrategicFields.All.FirstOrDefault(f=>f.Key==p.Key)?.Label??p.Key),p.Value);
        for(var ci=0;ci<state.Companies.Count;ci++)
        {
            var c=state.Companies[ci];var cp="c/"+c.Id;var cl=$"连 {ci+1} · {c.Name}";
            Add(cp+"/name",cl+" / 名称",c.Name);Add(cp+"/order",cl+" / 位置",ci+1);Add(cp+"/hq",cl+" / 指挥编组",c.IsHQ);
            for(var gi=0;gi<c.Groups.Count;gi++)
            {
                var g=c.Groups[gi];var gp=cp+"/g/"+g.Id;var gl=cl+$" / 组 {gi+1} · {g.Name}";
                Add(gp+"/name",gl+" / 名称",g.Name);Add(gp+"/order",gl+" / 位置",gi+1);Add(gp+"/hq",gl+" / 指挥编组",g.IsHQ);
                for(var si=0;si<g.Slots.Count;si++)
                {
                    var s=g.Slots[si];var sp=gp+"/s/"+s.Id;var sl=gl+$" / 单位 {si+1}";
                    Add(sp+"/unit",sl+" / 单位",Unit(s.Unit));Add(sp+"/transport",sl+" / 运输",Unit(s.Transport));Add(sp+"/xp",sl+" / 经验",s.Xp);Add(sp+"/count",sl+" / 数量",s.Count);Add(sp+"/pack",sl+" / Pack",s.Pack);Add(sp+"/start",sl+" / 起始索引",s.Start);Add(sp+"/order",sl+" / 位置",si+1);
                }
            }
        }
        return result;
    }
}
