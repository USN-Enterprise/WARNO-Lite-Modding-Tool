using System.Globalization;
using System.Text.Json;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Localisation;
using WarnoLiteModdingTool.Core.Transactions;
namespace WarnoLiteModdingTool.Core.Rules;

public static class AirLayout
{
    public const int Number=54;
    public const string Directory="GameData/UserInterface/Use/InGame/";
    public static readonly string[] Files=["UISpecificSkirmishProductionMenuView.ndf","UISpecificOffMapView.ndf","UISpecificOffMapAirplaneView.ndf"];
    public static readonly string[] Layouts=["3×3","4×3","4×4","5×4","5×5","6×4","6×5","6×6"];
    public static readonly string[] Scales=["0.5","0.6","0.75","1","1.25","1.5"];
    private sealed record Data(TextFileSnapshot[] Snapshots,NdfSyntaxDocument[] Docs,NdfValueSpan Capacity,NdfValueSpan Grid,NdfValueSpan Slots,NdfValueSpan Card,NdfValueSpan Panel,NdfValueSpan Area,NdfValueSpan Margin,NdfValueSpan Gap);
    private static Data Load(string root,IReadOnlyDictionary<string,string>? candidates=null)
    {
        var s=Files.Select(f=>TextFileSnapshot.Load(root,Directory+f,FormalTextFileKind.Ndf)).ToArray();
        var d=s.Select(f=>new NdfSyntaxDocument(candidates?.GetValueOrDefault(f.RelativePath)??f.Text)).ToArray();
        IEnumerable<NdfConstructorSpan> All(int doc,string type,int start=0,int end=int.MaxValue)
        {foreach(var c in d[doc].FindConstructors(type,start,end)){yield return c;foreach(var child in All(doc,type,c.OpenTokenIndex+1,c.CloseTokenIndex-1))yield return child;}}
        NdfConstructorSpan Named(int doc,string type,string field,string value)=>All(doc,type).Single(c=>d[doc].FindDirectAssignments(c,field) is {Count:1} spans && NdfSyntaxDocument.Unquote(d[doc].Raw(spans[0]))==value);
        NdfValueSpan Direct(int doc,NdfConstructorSpan c,string field)=>d[doc].FindDirectAssignments(c,field).Single();
        var grid=Named(1,"BUCKGridDescriptor","ElementName","UnitGrid");
        var panel=Named(1,"BUCKContainerDescriptor","ElementName","Container");
        var panelFrame=d[1].FindConstructors("TUIFramePropertyRTTI",Direct(1,panel,"ComponentFrame")).Single();
        // The immediate container enclosing UnitGrid owns the height; require a unique structural match.
        var area=All(1,"BUCKContainerDescriptor").Where(c=>c.TypeTokenIndex<grid.TypeTokenIndex && c.CloseTokenIndex>grid.CloseTokenIndex).OrderBy(c=>c.CloseTokenIndex-c.TypeTokenIndex).First();
        var areaFrame=d[1].FindConstructors("TUIFramePropertyRTTI",Direct(1,area,"ComponentFrame")).Single();
        return new(s,d,RuleWorkspace.Locate(d[0],"TUISpecificSkirmishProductionMenuViewDescriptor","NbMaxPlanes"),Direct(1,grid,"MaxElementsPerDimension"),Direct(1,grid,"GridElements"),RuleWorkspace.Locate(d[2],"","OffMapAirplaneComponentDimension"),Direct(1,panelFrame,"MagnifiableWidthHeight"),Direct(1,areaFrame,"MagnifiableWidthHeight"),Direct(1,grid,"FirstElementMargin"),Direct(1,grid,"InterElementMargin"));
    }
    private static decimal[] Pair(NdfSyntaxDocument d,NdfValueSpan span)=>d.ReadArrayElements(span).Select(v=>decimal.Parse(d.Raw(v),CultureInfo.InvariantCulture)).ToArray() is {Length:2} pair?pair:throw new InvalidDataException("飞机栏需要二维尺寸");
    public static RuleGroup Read(string root,RuleDefinition definition)
    {
        var a=Load(root); var pair=Pair(a.Docs[1],a.Grid);var card=Pair(a.Docs[2],a.Card);
        if(pair.Any(n=>n<1||n!=decimal.Truncate(n))||card.Any(n=>n<=0))throw new InvalidDataException("飞机栏布局或卡片尺寸无效");
        if(decimal.Parse(a.Docs[0].Raw(a.Capacity),CultureInfo.InvariantCulture)!=pair[0]*pair[1])throw new InvalidDataException("飞机栏容量与现有网格不一致，请先核对原文件");
        var key=string.Join("×",pair.Select(n=>n.ToString(CultureInfo.InvariantCulture)));
        return new(definition,[new("grid","飞机栏排列",key,0,0,false,false),new("scale","卡片大小倍率（相对当前文件）","1",0,0,false,false)],JsonSerializer.Serialize(a.Snapshots.Select(s=>s.Text)),"");
    }
    public static void Validate(RuleGroup g,IReadOnlyDictionary<string,string> values)
    {
        if(!g.CanEdit||values.Count!=2||!values.TryGetValue("grid",out var grid)||!values.TryGetValue("scale",out var scale))throw new InvalidDataException("飞机栏草稿结构无效");
        if(!Layouts.Contains(grid)&&grid!=g.Cells[0].Raw)throw new InvalidDataException("请选择已有网格方案");
        if(!Scales.Contains(scale))throw new InvalidDataException("请选择卡片大小倍率");
    }
    public static void Plan(string root,DraftOperation op,List<PlannedFileChange> planned)
    {
        var definition=RuleCatalog.All.Single(d=>d.Number==Number);var group=Read(root,definition);var values=RuleWorkspace.Values(op);Validate(group,values);
        if(group.Baseline!=op.BaselineRaw)throw new TransactionValidationException("飞机栏文件已变化，请重新编辑");
        var a=Load(root);var dims=values["grid"].Split('×').Select(int.Parse).ToArray();var scale=decimal.Parse(values["scale"],CultureInfo.InvariantCulture);
        var changes=new List<TextReplacement>[] {[],[],[]};
        void Patch(int file,NdfValueSpan span,string text) {var d=a.Docs[file];if(d.Raw(span)!=text)changes[file].Add(new(d.StartOffset(span),d.Length(span),d.Raw(span),text,op.Summary));}
        string F(decimal n)=>n.ToString("0.######",CultureInfo.InvariantCulture);
        var card=Pair(a.Docs[2],a.Card).Select(n=>n*scale).ToArray();
        var marginCtor=a.Docs[1].FindConstructors("TRTTILength2",a.Margin).Single();var gapCtor=a.Docs[1].FindConstructors("TRTTILength2",a.Gap).Single();
        var margin=Pair(a.Docs[1],a.Docs[1].FindDirectAssignments(marginCtor,"Magnifiable").Single());var gap=Pair(a.Docs[1],a.Docs[1].FindDirectAssignments(gapCtor,"Magnifiable").Single());
        var entries=a.Docs[1].ReadMapEntries(a.Slots);
        if(entries.Count==0 || entries.Any(e=>a.Docs[1].Raw(e.Value).Trim()!="~/DummyOffMapPanel"))throw new TransactionValidationException("飞机栏包含非标准槽位，不能自动重建");
        var oldCoordinates=entries.Select(e=>string.Join(",",Pair(a.Docs[1],e.Key))).ToArray();
        if(oldCoordinates.Distinct().Count()!=oldCoordinates.Length)throw new TransactionValidationException("飞机栏存在重复槽位");
        Patch(0,a.Capacity,(dims[0]*dims[1]).ToString(CultureInfo.InvariantCulture));
        Patch(1,a.Grid,$"[{dims[0]}, {dims[1]}]");
        var nl=a.Snapshots[1].NewLine;var offset=a.Docs[1].StartOffset(a.Slots);var line=a.Snapshots[1].Text.LastIndexOf('\n',Math.Max(0,offset-1))+1;
        var indent=new string(a.Snapshots[1].Text[line..offset].TakeWhile(char.IsWhiteSpace).ToArray());
        var slots=from x in Enumerable.Range(0,dims[0]) from y in Enumerable.Range(0,dims[1]) select $"{indent}    ( [{x}, {y}], ~/DummyOffMapPanel ),";
        if(values["grid"]!=group.Cells[0].Raw)Patch(1,a.Slots,"MAP"+nl+indent+"["+nl+string.Join(nl,slots)+nl+indent+"]");
        Patch(2,a.Card,$"[{F(card[0])}, {F(card[1])}]");
        var oldGrid=Pair(a.Docs[1],a.Grid);var oldCard=Pair(a.Docs[2],a.Card);var oldPanel=Pair(a.Docs[1],a.Panel);var oldArea=Pair(a.Docs[1],a.Area);
        var widthPad=Math.Max(0,oldPanel[0]-oldGrid[0]*oldCard[0]-(oldGrid[0]-1)*gap[0]-margin[0]);
        var heightPad=Math.Max(0,oldArea[1]-oldGrid[1]*oldCard[1]-(oldGrid[1]-1)*gap[1]-margin[1]);
        Patch(1,a.Panel,$"[{F(dims[0]*card[0]+(dims[0]-1)*gap[0]+margin[0]+widthPad)}, {F(oldPanel[1])}]");
        Patch(1,a.Area,$"[{F(oldArea[0])}, {F(dims[1]*card[1]+(dims[1]-1)*gap[1]+margin[1]+heightPad)}]");
        var candidateTexts=a.Snapshots.Select((snapshot,i)=>(snapshot.RelativePath,Text:SemicolonCsvDocument.ApplyReplacements(snapshot.Text,changes[i]))).ToDictionary(p=>p.RelativePath,p=>p.Text);
        var final=Load(root,candidateTexts);
        if(decimal.Parse(final.Docs[0].Raw(final.Capacity),CultureInfo.InvariantCulture)!=dims[0]*dims[1] || !Pair(final.Docs[1],final.Grid).SequenceEqual(dims.Select(n=>(decimal)n)) || !Pair(final.Docs[2],final.Card).SequenceEqual(card.Select(n=>decimal.Parse(F(n),CultureInfo.InvariantCulture))) || final.Docs[1].ReadMapEntries(final.Slots).Count!=dims[0]*dims[1])throw new TransactionValidationException("飞机栏组合候选回读不一致");
        for(var i=0;i<3;i++)if(changes[i].Count>0)
        {
            var snapshot=a.Snapshots[i];if(planned.Any(p=>p.RelativePath.Equals(snapshot.RelativePath,StringComparison.OrdinalIgnoreCase)))throw new TransactionValidationException("飞机栏文件存在重叠修改");
            var candidate=candidateTexts[snapshot.RelativePath];
            _=new NdfSyntaxDocument(candidate);
            planned.Add(new(snapshot.RelativePath,snapshot.FullPath,FormalTextFileKind.Ndf,PlannedFileAction.Write,true,snapshot.OriginalBytes,snapshot.Encode(candidate),snapshot.LastWriteUtc,[op.Summary]));
        }
    }
}
