using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Localisation;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
using static WarnoLiteModdingTool.Core.Strategic.StrategicSyntax;
namespace WarnoLiteModdingTool.Core.Strategic;

public sealed record StrategicPackEdit(string Name,string Unit,string Transport,int Xp);
public sealed record StrategicPackUse(string Deck,string Battalion,string Group,int Slot,string PawnId="");
public static class StrategicPackEditing
{
    public static StrategicPackEdit State(StrategicPack pack)=>new(pack.Source.Info.Name,pack.Unit,pack.Transport,pack.Xp);
    public static StrategicPackEdit Read(DraftOperation op)=>JsonSerializer.Deserialize<StrategicPackEdit>(op.TargetRaw)??throw new InvalidDataException("SP 草稿为空");
    public static HashSet<string> ExistingNames(string root)
    {
        var result=new HashSet<string>(StringComparer.Ordinal);
        foreach(var file in System.IO.Directory.EnumerateFiles(Path.Combine(root,"GameData"),"*.ndf",SearchOption.AllDirectories))
            foreach(var obj in new NdfTopLevelScanner().Scan(File.ReadAllText(file),file,"sp",root).Objects)result.Add(obj.Name);
        return result;
    }
    public static string Name(string unit,string transport,int xp,ISet<string> used)
    {
        string Part(string s)=>Regex.Replace(s.Replace("Descriptor_Unit_",""),"[^A-Za-z0-9_]","_");
        var basis=$"Descriptor_StrategicPack_mod_{Part(unit)}"+(transport.Length>0?"_Transport_"+Part(transport):"")+"_Xp"+xp;
        var name=basis;for(var i=1;!used.Add(name);i++)name=basis+"_"+i.ToString("00",CultureInfo.InvariantCulture);return name;
    }
    public static DraftOperation Operation(StrategicPack pack,StrategicPackEdit state)
    {
        var source=pack.Source.Info;var original=State(pack);
        string Describe(StrategicPackEdit s)=>$"{s.Name} · {s.Unit} · {s.Transport} · XP {s.Xp}";
        return new(DraftOperation.CreateId(DraftTargetKind.StrategicPack,source.RelativeSourceFile,source.Name,"pack"),null,DraftTargetKind.StrategicPack,"sp",source.RelativeSourceFile,source.Name,source.TypeName,"pack","StrategicPack","StrategicPackEdit",Describe(original),pack.Source.Text,Describe(state),JsonSerializer.Serialize(state),$"战略 Pack · {Describe(original)} → {Describe(state)}",null,false,DateTimeOffset.UtcNow);
    }
    public static ResolvedDraftOperation Resolve(StrategicWorkspace? data,DraftOperation op)
    {
        try
        {
            if(data is null||!data.Packs.TryGetValue(op.ObjectName,out var pack))throw new InvalidDataException("SP 不存在");
            var state=Read(op);var expected=Operation(pack,state);
            if(op.Id!=expected.Id||op.BaselineRaw!=expected.BaselineRaw||op.RelativeSourceFile!=expected.RelativeSourceFile||op.Module!="sp"||op.ObjectType!=expected.ObjectType||op.FieldKey!="pack")throw new InvalidDataException("SP 草稿身份或基线已变化");
            if(!Regex.IsMatch(state.Name,@"^[A-Za-z_][A-Za-z0-9_]*$"))throw new InvalidDataException("Pack 名称只能使用英文字母、数字和下划线，不能以数字开头");
            if(state.Name!=op.ObjectName && (data.Packs.ContainsKey(state.Name)||data.Decks.ContainsKey(state.Name)||data.CombatGroups.ContainsKey(state.Name)))throw new InvalidDataException("Pack 名称已存在");
            if(state.Xp<0||state.Xp>3)throw new InvalidDataException("老练度必须为0–3");
            if(state.Unit!=pack.Unit&&!data.Units.Units.Any(u=>u.Name==state.Unit))throw new InvalidDataException("单位必须来自当前 Mod");
            if(state.Transport!=pack.Transport&&state.Transport.Length>0&&!data.Units.Units.Any(u=>u.Name==state.Transport&&u.HasUniqueTransporterModule))throw new InvalidDataException("运输必须具有已识别的运输模块");
            return new(op,DraftResolutionStatus.Active,"");
        }catch(Exception ex) when(ex is InvalidDataException or JsonException or ArgumentException or InvalidOperationException){return new(op,DraftResolutionStatus.Conflict,ex.Message);}
    }
    public static IReadOnlyList<StrategicPackUse> Uses(StrategicWorkspace data,string pack)
    {
        var result=new List<StrategicPackUse>();
        foreach(var deck in data.Decks.Values)
        {
            var d=new NdfSyntaxDocument(deck.Text);var list=Field(d,"TDeckDescriptor","DeckPackList");if(list is null)throw new InvalidDataException("编制引用无法解析："+deck.Info.Name);
            var entries=d.ReadArrayElements(list);var records=data.Records.Where(r=>r.Deck.Info.Name==deck.Info.Name).ToArray();
            for(var i=0;i<entries.Count;i++)if(Leaf(d.Raw(entries[i]))==pack)
            {
                foreach(var r in records.DefaultIfEmpty())
                {
                    var groups=r?.Baseline.Companies.SelectMany(c=>c.Groups.Select(g=>(Company:c,Group:g))).Where(x=>x.Group.Slots.Any(s=>s.Pack==pack && int.TryParse(r!.Templates.GetValueOrDefault(s.Id+"/start"),out var start) && i>=start && i<start+s.Count)).Select(x=>x.Company.Name+" / "+x.Group.Name).Distinct()??[];
                    result.Add(new(deck.Info.Name,r?.DisplayName??"",string.Join(", ",groups),i,r?.Pawn?.Info.Name??""));
                }
            }
        }return result;
    }
    public static void Plan(StrategicWorkspace data,IReadOnlyList<DraftOperation> operations,List<PlannedFileChange> planned)
    {
        var edits=operations.Where(o=>o.TargetKind==DraftTargetKind.StrategicPack).ToArray();if(edits.Length==0)return;
        foreach(var op in edits){var r=Resolve(data,op);if(r.Status!=DraftResolutionStatus.Active)throw new TransactionValidationException(r.Reason);}
        var states=edits.ToDictionary(o=>o.ObjectName,Read,StringComparer.Ordinal);
        if(states.Values.Select(s=>s.Name).Distinct(StringComparer.Ordinal).Count()!=states.Count)throw new TransactionValidationException("同批 Pack 名称重复");
        var names=new HashSet<string>(StringComparer.Ordinal);
        var declarations=new Dictionary<string,int>(StringComparer.Ordinal);
        var snapshots=new Dictionary<string,TextFileSnapshot>(StringComparer.OrdinalIgnoreCase);
        var texts=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        // Scan every current Mod NDF, so renames include references outside recognised battalion trees.
        foreach(var file in System.IO.Directory.EnumerateFiles(Path.Combine(data.Root,"GameData"),"*.ndf",SearchOption.AllDirectories))
        {
            var relative=Path.GetRelativePath(data.Root,file).Replace('\\','/');var snapshot=TextFileSnapshot.Load(data.Root,relative,FormalTextFileKind.Ndf);
            var prior=planned.FirstOrDefault(p=>p.RelativePath.Equals(relative,StringComparison.OrdinalIgnoreCase));var text=prior is null?snapshot.Text:System.Text.Encoding.UTF8.GetString(prior.CandidateBytes);
            snapshots[relative]=snapshot;texts[relative]=text;
            foreach(var obj in new NdfTopLevelScanner().Scan(text,file,"sp",data.Root).Objects){names.Add(obj.Name);declarations[obj.Name]=declarations.GetValueOrDefault(obj.Name)+1;}
        }
        foreach(var old in states.Keys)if(declarations.GetValueOrDefault(old)!=1)throw new TransactionValidationException("Pack 对象名存在多义定义："+old);
        foreach(var (old,s) in states)if(old!=s.Name&&names.Contains(s.Name))throw new TransactionValidationException("Pack 名称与当前 Mod 或本批新对象重复："+s.Name);
        foreach(var op in edits)
        {
            var pack=data.Packs[op.ObjectName];var path=pack.Source.Info.RelativeSourceFile;var text=texts[path];var scan=new NdfTopLevelScanner().Scan(text,snapshots[path].FullPath,"sp",data.Root);
            var obj=scan.Objects.Single(o=>o.Name==op.ObjectName);var block=text.Substring(obj.CharacterOffset,obj.CharacterLength);var state=states[op.ObjectName];var nl=snapshots[path].NewLine;
            if(state.Unit!=pack.Unit)block=Set(block,"DeckPackDescriptor","Unit","$/GFX/Unit/"+state.Unit);
            if(state.Xp!=pack.Xp)block=Set(block,"DeckPackDescriptor","Xp",state.Xp.ToString(CultureInfo.InvariantCulture),true,nl);
            if(state.Transport!=pack.Transport)block=state.Transport.Length==0?StrategicPlanner.RemoveAssignment(block,"DeckPackDescriptor","Transport"):Set(block,"DeckPackDescriptor","Transport","$/GFX/Unit/"+state.Transport,true,nl);
            texts[path]=text[..obj.CharacterOffset]+block+text[(obj.CharacterOffset+obj.CharacterLength)..];
        }
        var renamed=states.Where(p=>p.Key!=p.Value.Name).ToDictionary(p=>p.Key,p=>p.Value.Name,StringComparer.Ordinal);
        foreach(var path in texts.Keys.ToArray())
        {
            var text=texts[path];var d=new NdfSyntaxDocument(text);var patches=new List<TextReplacement>();
            foreach(var reference in d.FindReferences(""))if(renamed.TryGetValue(reference.Leaf,out var name))
            {
                if(reference.Raw.StartsWith('"')||reference.Raw.StartsWith('\''))continue;
                var raw=reference.Raw;var replacement=raw[..(raw.Length-reference.Leaf.Length)]+name;
                patches.Add(new(d.StartOffset(reference.Span),d.Length(reference.Span),raw,replacement,"SP 重命名引用"));
            }
            text=SemicolonCsvDocument.ApplyReplacements(text,patches);
            var original=texts[path];texts[path]=text;
            var snapshot=snapshots[path];var prior=planned.FirstOrDefault(p=>p.RelativePath.Equals(path,StringComparison.OrdinalIgnoreCase));
            if(text==snapshot.Text && prior is null)continue;
            if(text==original && edits.All(o=>o.RelativeSourceFile!=path))continue;
            var scan=new NdfTopLevelScanner().Scan(text,snapshot.FullPath,"sp",data.Root);
            if(scan.Diagnostics.Any(d=>d.Severity==NdfDiagnosticSeverity.Error)||scan.Objects.GroupBy(o=>o.Name).Any(g=>g.Count()>1))throw new TransactionValidationException("SP 候选语法或名称冲突："+path);
            planned.RemoveAll(p=>p.RelativePath.Equals(path,StringComparison.OrdinalIgnoreCase));
            planned.Add(new(path,snapshot.FullPath,FormalTextFileKind.Ndf,PlannedFileAction.Write,true,snapshot.OriginalBytes,snapshot.Encode(text),snapshot.LastWriteUtc,(prior?.Summaries??[]).Concat(edits.Select(o=>o.Summary)).ToArray()));
        }
        foreach(var op in edits)
        {
            var state=states[op.ObjectName];var path=op.RelativeSourceFile;var scan=new NdfTopLevelScanner().Scan(texts[path],snapshots[path].FullPath,"sp",data.Root);var obj=scan.Objects.Single(o=>o.Name==state.Name);var block=texts[path].Substring(obj.CharacterOffset,obj.CharacterLength);
            if(Leaf(ReadField(block,"Unit"))!=state.Unit||Leaf(ReadField(block,"Transport"))!=state.Transport||int.Parse(ReadField(block,"Xp","0"),CultureInfo.InvariantCulture)!=state.Xp)throw new TransactionValidationException("SP 候选回读不一致");
        }
    }
    private static string ReadField(string text,string field,string fallback="")=>StrategicSyntax.Read(text,"DeckPackDescriptor",field,fallback);
}
