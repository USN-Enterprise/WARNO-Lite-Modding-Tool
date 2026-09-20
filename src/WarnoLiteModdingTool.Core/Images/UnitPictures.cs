using System.Text.Json;
using System.Text.RegularExpressions;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Core.Images;

public sealed record UnitPictureTexture(string Key, string Source, string File, string Bank, string States);
public sealed record UnitPictureState(string Key, UnitPictureTexture Template, string? PngBase64 = null, string Recipe = "");

public static class UnitPictures
{
    public const string FieldKey = "unit.picture";
    public static IReadOnlyList<UnitPictureTexture> Catalog(string root, IReadOnlyList<PlannedFileChange>? files = null)
    {
        var result = new List<UnitPictureTexture>();
        foreach (var path in UnitProjectGraph.InputFiles(root))
        {
            var candidate = files?.SingleOrDefault(f => f.RelativePath.Equals(path, StringComparison.OrdinalIgnoreCase));
            var text = candidate is null ? File.ReadAllText(TextFileSnapshot.ResolveInsideRoot(root,path)) : System.Text.Encoding.UTF8.GetString(candidate.CandidateBytes);
            if (!text.Contains("TBUCKToolAdditionalTextureBank", StringComparison.Ordinal)) continue;
            var scan = new NdfTopLevelScanner().Scan(text, Path.Combine(root,path), "textures", root);
            if (scan.Diagnostics.Any(d => d.Severity == NdfDiagnosticSeverity.Error)) throw new InvalidDataException("单位图片纹理文件结构无效：" + path);
            foreach (var bank in scan.Objects.Where(o => o.TypeName == "TBUCKToolAdditionalTextureBank"))
            {
                var doc = new NdfSyntaxDocument(text,bank.CharacterOffset,bank.CharacterLength);
                var maps = doc.FindDirectAssignments(doc.FindConstructors("TBUCKToolAdditionalTextureBank").Single(),"Textures");
                if (maps.Count != 1) throw new InvalidDataException("单位图片纹理表不唯一：" + path);
                foreach (var entry in doc.ReadMapEntries(maps[0]))
                {
                    var key = NdfSyntaxDocument.Unquote(doc.Raw(entry.Key));
                    var states = doc.ReadMapEntries(entry.Value).Where(e=>NdfSyntaxDocument.Leaf(doc.Raw(e.Key))=="Normal").ToArray();
                    var source = "";
                    if (states.Length == 1)
                    {
                        var nodes=doc.FindConstructors("TUIResourceTexture",states[0].Value);
                        if(nodes.Count==1 && doc.FindDirectAssignments(nodes[0],"FileName") is {Count:1} fields)
                            source=NdfSyntaxDocument.Unquote(doc.Raw(fields[0]));
                    }
                    // Keep even unsupported entries so they still reserve their keys.
                    result.Add(new(key,source,path,bank.Name,doc.Raw(entry.Value)));
                }
            }
        }
        return result;
    }

    public static UnitPictureTexture Require(IReadOnlyList<UnitPictureTexture> catalog,string key)
    {
        var matches=catalog.Where(t=>t.Key==key).ToArray();
        if(matches.Length!=1 || string.IsNullOrWhiteSpace(matches[0].Source)) throw new InvalidDataException("单位图片引用缺失、不支持或重复："+key);
        return matches[0];
    }
    public static NdfValueSpan Field(NdfSyntaxDocument doc)
    {
        var ui=doc.FindConstructors("TUnitUIModuleDescriptor");
        if(ui.Count!=1 || doc.FindDirectAssignments(ui[0],"ButtonTexture") is not {Count:1} fields)
            throw new InvalidDataException("无法唯一定位单位ButtonTexture");
        var raw=doc.Raw(fields[0]);
        if(raw.Length<2 || raw[0] is not ('\'' or '"') || raw[^1]!=raw[0]) throw new InvalidDataException("单位图片引用不是字符串");
        return fields[0];
    }
    public static string Raw(string body){var doc=new NdfSyntaxDocument(body);return doc.Raw(Field(doc));}
    public static string Apply(string body,string key)
    {
        if(key.Length==0 || key.Any(c=>c is '\'' or '"' or '\r' or '\n' or '\\')) throw new InvalidDataException("单位图片标识无效");
        var doc=new NdfSyntaxDocument(body);var field=Field(doc);var raw=doc.Raw(field);
        return UnitProjectGraph.Patch(body,[new(doc.StartOffset(field),doc.Length(field),raw,raw[0]+key+raw[0],"单位图片")]);
    }
    public static UnitPictureState Read(DraftOperation op)=>JsonSerializer.Deserialize<UnitPictureState>(op.TargetRaw)??throw new InvalidDataException("单位图片草稿为空");
    public static DraftOperation Operation(UnitRecord unit,UnitPictureState state,string? baseline=null)
    {
        var raw=baseline??Raw(UnitCreation.Source(unit));var json=JsonSerializer.Serialize(state);
        return new(DraftOperation.CreateId(DraftTargetKind.UnitPicture,unit.Source.RelativeSourceFile,unit.Name,FieldKey),"picture:"+unit.Name,DraftTargetKind.UnitPicture,"units",unit.Source.RelativeSourceFile,unit.Name,unit.Source.TypeName,FieldKey,"TUnitUIModuleDescriptor/ButtonTexture","UnitPicture",raw,raw,state.Key,json,"修改单位图片 · "+unit.DisplayName,null,false,DateTimeOffset.UtcNow);
    }
    public static ResolvedDraftOperation Resolve(UnitWorkspaceData workspace,DraftOperation op)
    {
        try
        {
            var unit=workspace.Units.Single(u=>u.Name==op.ObjectName && u.Source.RelativeSourceFile==op.RelativeSourceFile && u.Source.TypeName==op.ObjectType);
            if(Raw(UnitCreation.Source(unit))!=op.BaselineRaw || op.FieldKey!=FieldKey) throw new InvalidDataException("单位图片引用基线已变化");
            var state=Read(op);_ = Apply(UnitCreation.Source(unit),state.Key);
            return new(op,DraftResolutionStatus.Active,"");
        }
        catch(Exception ex) when(ex is IOException or InvalidOperationException or ArgumentException or JsonException)
        {return new(op,DraftResolutionStatus.Conflict,ex.Message);}
    }
    public static UnitPictureState Import(UnitPictureTexture template,byte[] png,string recipe="")
    {
        var state=new UnitPictureState("Texture_Button_Unit_mod_"+Guid.NewGuid().ToString("N"),template,Convert.ToBase64String(png),recipe);
        _=PngAssets.Decode(state.PngBase64!);_ = CustomStates(state);return state;
    }
    public static string AssetPath(string key)=>"GameData/Assets/2D/Interface/Common/UnitsIcons/Custom/"+key+".png";
    private static string CustomStates(UnitPictureState state)
    {
        var doc=new NdfSyntaxDocument("Picture is TObject(States = "+state.Template.States+")");
        var map=doc.FindDirectAssignments(doc.FindConstructors("TObject").Single(),"States").Single();
        var states=doc.ReadMapEntries(map);
        if(states.Count!=1 || NdfSyntaxDocument.Leaf(doc.Raw(states[0].Key))!="Normal") throw new InvalidDataException("自定义图片目前需要唯一Normal状态纹理");
        var node=doc.FindConstructors("TUIResourceTexture",states[0].Value).Single();
        var field=doc.FindDirectAssignments(node,"FileName").Single();var raw=doc.Raw(field);
        var offset=doc.StartOffset(field)-doc.StartOffset(map);
        return state.Template.States.Remove(offset,doc.Length(field)).Insert(offset,"\"GameData:/"+AssetPath(state.Key)[9..]+"\"");
    }

    // Run after creation and ordinary fields, before identity renaming/deletion.
    public static Dictionary<string,byte[]> Plan(string root,IReadOnlyList<DraftOperation> operations,List<PlannedFileChange> files)
    {
        var tasks=operations.Where(o=>o.TargetKind==DraftTargetKind.UnitPicture).Select(o=>(Op:o,State:Read(o),Created:false))
            .Concat(operations.Where(o=>o.TargetKind==DraftTargetKind.UnitCreate).Select(o=>(Op:o,State:UnitCreation.Read(o).Picture,Created:true)).Where(t=>t.State is not null).Select(t=>(t.Op,State:t.State!,t.Created))).ToArray();
        var dependencies=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        if(tasks.Length==0)return dependencies;
        var original=Catalog(root);
        foreach(var (op,state,created) in tasks)
        {
            var template=Require(original,state.Template.Key);
            if(template!=state.Template)throw new TransactionValidationException("单位图片来源已变化，请重新选择图片");
            if(state.PngBase64 is null)
            {
                if(state.Key!=template.Key)throw new TransactionValidationException("单位图片键与候选不一致");
                _=Require(Catalog(root,files),state.Key);
                var local=ModTextures.LocalPath(root,template.Source);
                if(template.Source.StartsWith("GameData:/",StringComparison.OrdinalIgnoreCase) && local is null)throw new TransactionValidationException("单位图片路径越出Mod资源目录");
                if(local is not null)dependencies[Path.GetRelativePath(root,local).Replace('\\','/')]=File.Exists(local)?File.ReadAllBytes(local):[];
            }
            else
            {
                if(!Regex.IsMatch(state.Key,@"^Texture_Button_Unit_mod_[A-Za-z0-9_]+$"))throw new TransactionValidationException("自定义单位图片标识无效");
                var png=PngAssets.Decode(state.PngBase64);
                if(Catalog(root,files).Any(t=>t.Key==state.Key))throw new TransactionValidationException("单位图片标识已存在，请重新导入");
                var graph=new UnitProjectGraph(root,files);var bank=graph.RequireObject(template.File,template.Bank);
                var source=graph.FileOf(bank);var doc=new NdfSyntaxDocument(source.Text,bank.CharacterOffset,bank.CharacterLength);
                var map=doc.FindDirectAssignments(doc.FindConstructors("TBUCKToolAdditionalTextureBank").Single(),"Textures").Single();
                var at=doc.StartOffset(map)+doc.Length(map)-1;var nl=source.Text.Contains("\r\n")?"\r\n":"\n";
                var indentation="        ";var entries=doc.ReadMapEntries(map);
                if(entries.Count>0){var start=doc.StartOffset(entries[^1].Key)-1;var line=source.Text.LastIndexOf('\n',Math.Max(0,start-1))+1;var prefix=source.Text[line..start];if(prefix.All(c=>c is ' ' or '\t'))indentation=prefix;}
                var candidate=source.Text.Insert(at,(doc.NeedsArraySeparator(map)?",":"")+nl+indentation+"(\""+state.Key+"\", "+CustomStates(state)+"),"+nl);
                UnitProjectGraph.Put(root,template.File,candidate,files,"新增独立单位图片纹理");
                var path=AssetPath(state.Key);var snap=TextFileSnapshot.Load(root,path,FormalTextFileKind.Binary,true);
                if(snap.Existed || Directory.Exists(snap.FullPath) || files.Any(f=>f.RelativePath.Equals(path,StringComparison.OrdinalIgnoreCase)))throw new TransactionValidationException("单位图片文件已存在，不覆盖");
                files.Add(new(path,snap.FullPath,FormalTextFileKind.Binary,PlannedFileAction.Write,false,[],png,null,["新增单位图片PNG"]));
            }
            var combined=new UnitProjectGraph(root,files);var obj=combined.RequireObject(op.RelativeSourceFile,op.ObjectName);var body=combined.Body(obj);
            if(!created && Raw(body)!=op.BaselineRaw)throw new TransactionValidationException("组合操作改变了单位图片引用");
            var changed=Apply(body,state.Key);var file=combined.FileOf(obj);
            UnitProjectGraph.Put(root,file.Path,file.Text.Remove(obj.CharacterOffset,obj.CharacterLength).Insert(obj.CharacterOffset,changed),files,"修改所选单位图片");
            var final=Require(Catalog(root,files),state.Key);
            if(final.Source!=(state.PngBase64 is null?template.Source:"GameData:/"+AssetPath(state.Key)[9..]))throw new TransactionValidationException("最终单位图片引用不一致");
        }
        return dependencies;
    }
}
