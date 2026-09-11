using System.Text.Json;
using System.Text.RegularExpressions;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Images;
using WarnoLiteModdingTool.Core.Indexing;
using WarnoLiteModdingTool.Core.Localisation;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;
namespace WarnoLiteModdingTool.Core.Divisions;

public sealed record DivisionIdentityState(string Mother,string Id,bool Create,string Name,string Token,string Emblem,int SerializerId,Dictionary<string,string> Baselines);
public static class DivisionIdentity
{
    public static DivisionIdentityState Read(DraftOperation op)=>JsonSerializer.Deserialize<DivisionIdentityState>(op.TargetRaw)??throw new InvalidDataException("师草稿为空");
    public static string Source(DivisionRecord d)=>File.ReadAllText(d.Source.SourceFile).Substring(d.Source.CharacterOffset,d.Source.CharacterLength);
    public static string Field(DivisionRecord d,string field){var doc=new NdfSyntaxDocument(Source(d));var values=doc.FindDirectAssignments(doc.FindConstructors("TDeckDivisionDescriptor").Single(),field);return values.Count==1?NdfSyntaxDocument.Unquote(doc.Raw(values[0])):"";}
    public static DivisionIdentityState New(DivisionWorkspaceData data,DivisionRecord mother,bool create,IReadOnlyList<DraftOperation> drafts)
    {
        if(!mother.CanEdit)throw new InvalidDataException(mother.EditReason);VanillaNames.RequireAvailable();var token=data.Units.Localisation.GenerateToken();var id=create?"Descriptor_Deck_Division_WL_"+token:mother.Name;
        var baselines=new Dictionary<string,string>{{mother.Name,Source(mother)}};
        var next=0;if(create){foreach(var (name,path) in new[]{(mother.DivisionRuleName,mother.UnitRuleList.RelativeSourceFile),(mother.CostMatrixName,mother.CostMatrix.RelativeSourceFile),(mother.DefaultDeckName,mother.DeckPackList.RelativeSourceFile)}){var text=File.ReadAllText(Path.Combine(data.ProjectRoot,path));var scan=new NdfTopLevelScanner().Scan(text,Path.Combine(data.ProjectRoot,path),"divisions",data.ProjectRoot);if(name==mother.CostMatrixName){var md=new NdfSyntaxDocument(text);var mm=md.FindNamedMaps("MAP").Single(m=>m.Name==name);baselines[name]=md.Raw(new NdfValueSpan(mm.Value.StartTokenIndex-3,mm.Value.EndTokenIndex));}else{var o=scan.Objects.Single(o=>o.Name==name);baselines[name]=text.Substring(o.CharacterOffset,o.CharacterLength);}}
            var doc=new NdfSyntaxDocument(File.ReadAllText(Path.Combine(data.ProjectRoot,UnitCreation.SerializerPath)));var map=doc.FindDirectAssignments(doc.FindConstructors("TDeckSerializerEntries").Single(),"DivisionIds").Single();var ids=doc.ReadMapEntries(map).Select(e=>int.Parse(doc.Raw(e.Value))).Concat(drafts.Where(o=>o.TargetKind==DraftTargetKind.DivisionIdentity).Select(Read).Where(s=>s.Create).Select(s=>s.SerializerId));next=checked(ids.DefaultIfEmpty(-1).Max()+1);}
        return new(mother.Name,id,create,mother.DisplayName,token,Field(mother,"EmblemTexture"),next,baselines);
    }
    public static DraftOperation Operation(DivisionRecord mother,DivisionIdentityState state)
    {
        var raw=JsonSerializer.Serialize(state);return new(DraftOperation.CreateId(DraftTargetKind.DivisionIdentity,mother.Source.RelativeSourceFile,state.Id,"division.identity"),"division:"+state.Id,DraftTargetKind.DivisionIdentity,"divisions",mother.Source.RelativeSourceFile,state.Id,mother.Source.TypeName,"division.identity","Division/Identity","DivisionIdentity",state.Baselines[mother.Name],state.Baselines[mother.Name],raw,raw,(state.Create?"新建战术师 · ":"师名称与徽章 · ")+state.Name,state.Token,true,DateTimeOffset.UtcNow);
    }
    public static ResolvedDraftOperation Resolve(DivisionWorkspaceData? data,DraftOperation op)
    {
        try{var s=Read(op);var d=data?.Division(s.Mother)??throw new InvalidDataException("母版师已不存在");if(!d.CanEdit||Source(d)!=op.BaselineRaw||s.Baselines.GetValueOrDefault(s.Mother)!=op.BaselineRaw)throw new InvalidDataException("师母版已变化");if(s.Id!=op.ObjectName||s.Create&&data!.Divisions.Any(d=>d.Name==s.Id)||!s.Create&&s.Id!=s.Mother||!Regex.IsMatch(s.Id,@"^Descriptor_Deck_Division_[A-Za-z0-9_]+$")||!Regex.IsMatch(s.Token,@"^[A-Z0-9]{10}$")||s.SerializerId<0||string.IsNullOrWhiteSpace(s.Name))throw new InvalidDataException("师身份或名称无效");return new(op,DraftResolutionStatus.Active,"");}catch(Exception ex)when(ex is IOException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException){return new(op,DraftResolutionStatus.Conflict,ex.Message);}
    }
    public static void Plan(string root,DivisionWorkspaceData? data,ProjectIndexResult index,IReadOnlyList<DraftOperation> operations,List<PlannedFileChange> files)
    {
        var ops=operations.Where(o=>o.TargetKind==DraftTargetKind.DivisionIdentity).ToArray();if(ops.Length==0)return;if(data is null)throw new TransactionValidationException("师模块不可用");
        var touched=new Dictionary<string,(TextFileSnapshot Snap,string Text)>(StringComparer.OrdinalIgnoreCase);
        string Get(string path,FormalTextFileKind kind=FormalTextFileKind.Ndf){path=path.Replace('\\','/');if(!touched.TryGetValue(path,out var pair)){var snap=TextFileSnapshot.Load(root,path,kind,allowMissing:kind==FormalTextFileKind.Csv);var prior=files.FirstOrDefault(f=>f.RelativePath.Equals(path,StringComparison.OrdinalIgnoreCase));pair=(snap,prior is null?snap.Text:new StreamReader(new MemoryStream(prior.CandidateBytes),true).ReadToEnd());touched[path]=pair;}return pair.Text;}
        void Put(string path,string text,FormalTextFileKind kind=FormalTextFileKind.Ndf){path=path.Replace('\\','/');Get(path,kind);touched[path]=(touched[path].Snap,text);}
        var names=index.Objects.Select(o=>o.Name).Concat(data.Divisions.Select(d=>d.CostMatrixName)).ToHashSet();var tokens=new HashSet<string>();
        foreach(var op in ops)
        {
            var resolved=Resolve(data,op);if(resolved.Status!=DraftResolutionStatus.Active)throw new TransactionValidationException(resolved.Reason);var s=Read(op);var mother=data.Division(s.Mother)!;
            var emblemChoices=ModTextures.Read(root,true);if(s.Emblem!=Field(mother,"EmblemTexture")&&!emblemChoices.Any(e=>e.Key==s.Emblem))throw new TransactionValidationException("师徽不在当前Mod候选中");
            var rename=s.Create||s.Name!=mother.DisplayName;if(rename){VanillaNames.RequireAvailable();if(!tokens.Add(s.Token)||VanillaNames.Lookup("UNITS",s.Token) is not null||data.Units.Localisation.TryResolve(s.Token,out _)||data.Units.Localisation.IsTokenAmbiguous(s.Token))throw new TransactionValidationException("师名称token已占用");}
            string Change(string original,string type,Dictionary<string,string> values,string? oldName=null,string? newName=null)
            {
                var doc=new NdfSyntaxDocument(original);var edits=new List<TextReplacement>();if(oldName is not null){var scan=new NdfTopLevelScanner().Scan(original,"memory.ndf","divisions",root);if(scan.Objects.Count!=1||scan.Objects[0].Name!=oldName)throw new TransactionValidationException("克隆对象声明不唯一");var match=Regex.Match(original,@"\b"+Regex.Escape(oldName)+@"\s+is\b");if(!match.Success)throw new TransactionValidationException("克隆声明缺失");edits.Add(new(match.Index,oldName.Length,oldName,newName!,"新师对象"));foreach(var span in doc.FindAssignmentsAnywhere("DescriptorId"))edits.Add(new(doc.StartOffset(span),doc.Length(span),doc.Raw(span),"GUID:{"+Guid.NewGuid()+"}","新GUID"));}
                foreach(var (field,value) in values){var spans=doc.FindDirectAssignments(doc.FindConstructors(type).Single(),field);if(spans.Count==0&&field=="DescriptionHintTitleToken")continue;if(spans.Count!=1)throw new TransactionValidationException("师字段不能唯一定位："+field);var a=spans[0];edits.Add(new(doc.StartOffset(a),doc.Length(a),doc.Raw(a),value,field));}return SemicolonCsvDocument.ApplyReplacements(original,edits);
            }
            var values=new Dictionary<string,string>();if(rename){values["DivisionName"]="'"+s.Token+"'";values["DescriptionHintTitleToken"]="'"+s.Token+"'";}if(s.Emblem!=Field(mother,"EmblemTexture"))values["EmblemTexture"]="\""+s.Emblem+"\"";
            if(s.Create)
            {
                if(!names.Add(s.Id))throw new TransactionValidationException("师标识冲突");var suffix=s.Id["Descriptor_Deck_Division_".Length..];var rule=s.Id+"_Rule";var matrix="MatrixCostName_"+suffix;var deck="Descriptor_Deck_"+suffix;
                foreach(var (oldName,newName,type) in new[]{(mother.DivisionRuleName,rule,"TDeckDivisionRule"),(mother.CostMatrixName,matrix,"MAP"),(mother.DefaultDeckName,deck,"TDeckDescriptor")})
                {
                    if(!names.Add(newName))throw new TransactionValidationException("新师关联标识冲突");
                    if(type=="MAP")
                    {
                        var matrixPath=mother.CostMatrix.RelativeSourceFile;var matrixSource=File.ReadAllText(Path.Combine(root,matrixPath));var matrixDoc=new NdfSyntaxDocument(matrixSource);var matrixMap=matrixDoc.FindNamedMaps("MAP").Single(m=>m.Name==oldName);var originalMap=matrixDoc.Raw(new NdfValueSpan(matrixMap.Value.StartTokenIndex-3,matrixMap.Value.EndTokenIndex));if(s.Baselines.GetValueOrDefault(oldName)!=originalMap)throw new TransactionValidationException("费用矩阵母版已变化");var priorMatrix=Get(matrixPath);if(new NdfSyntaxDocument(priorMatrix).FindNamedMaps("MAP").Any(m=>m.Name==newName))throw new TransactionValidationException("费用矩阵名称已占用");Put(matrixPath,priorMatrix+touched[matrixPath.Replace('\\','/')].Snap.NewLine+newName+originalMap[oldName.Length..]);continue;
                    }
                    var obj=index.Objects.Single(o=>o.Name==oldName);var original=File.ReadAllText(obj.SourceFile).Substring(obj.CharacterOffset,obj.CharacterLength);if(s.Baselines.GetValueOrDefault(oldName)!=original)throw new TransactionValidationException("师关联母版已变化："+oldName);
                    var changes=new Dictionary<string,string>();if(type=="TDeckDescriptor"){changes["DeckDivision"]="$/GFX/Division/"+s.Id;}var clone=Change(original,type,changes,oldName,newName);var prior=Get(obj.RelativeSourceFile);Put(obj.RelativeSourceFile,prior+touched[obj.RelativeSourceFile.Replace('\\','/')].Snap.NewLine+clone);
                }
                values["CfgName"]="'"+suffix+"'";values["DivisionRule"]=rule;values["CostMatrix"]=matrix;var divisionClone=Change(s.Baselines[mother.Name],"TDeckDivisionDescriptor",values,mother.Name,s.Id);var path=mother.Source.RelativeSourceFile;Put(path,Get(path)+touched[path.Replace('\\','/')].Snap.NewLine+divisionClone);
                var serializer=Get(UnitCreation.SerializerPath);var doc=new NdfSyntaxDocument(serializer);var map=doc.FindDirectAssignments(doc.FindConstructors("TDeckSerializerEntries").Single(),"DivisionIds").Single();if(doc.ReadMapEntries(map).Any(e=>int.Parse(doc.Raw(e.Value))==s.SerializerId||NdfSyntaxDocument.Leaf(doc.Raw(e.Key))==s.Id))throw new TransactionValidationException("师注册编号已占用");var at=doc.StartOffset(map)+doc.Length(map)-1;var nl=touched[UnitCreation.SerializerPath].Snap.NewLine;Put(UnitCreation.SerializerPath,serializer.Insert(at,(doc.NeedsArraySeparator(map)?",":"")+nl+$"    ({s.Id}, {s.SerializerId}),"+nl));
            }
            else
            {
                var path=mother.Source.RelativeSourceFile;var current=Get(path);var scan=new NdfTopLevelScanner().Scan(current,Path.Combine(root,path),"divisions",root);var obj=scan.Objects.Single(o=>o.Name==mother.Name);var original=current.Substring(obj.CharacterOffset,obj.CharacterLength);var changed=Change(original,"TDeckDivisionDescriptor",values);Put(path,current.Remove(obj.CharacterOffset,obj.CharacterLength).Insert(obj.CharacterOffset,changed));
            }
            if(rename){var csv=data.Units.Localisation.UniqueUnitsCsvPath??throw new TransactionValidationException("无法唯一定位UNITS.csv");var path=Path.GetRelativePath(root,csv).Replace('\\','/');var text=Get(path,FormalTextFileKind.Csv);if(text.Length==0)text="TOKEN;REFTEXT";var doc=SemicolonCsvDocument.Parse(text);if(doc.Rows[0].Fields.Count!=2||doc.Rows[0].Fields[0].Value.Trim()!="TOKEN"||doc.Rows[0].Fields[1].Value.Trim()!="REFTEXT"||doc.Rows.Skip(1).Any(r=>r.Fields[0].Value==s.Token))throw new TransactionValidationException("师名称CSV格式或token冲突");var nl=touched[path].Snap.NewLine;Put(path,text+(text.EndsWith('\n')?"":nl)+s.Token+";"+SemicolonCsvDocument.Quote(s.Name)+nl,FormalTextFileKind.Csv);}
        }
        foreach(var (path,pair) in touched){if(pair.Snap.Kind==FormalTextFileKind.Ndf){var scan=new NdfTopLevelScanner().Scan(pair.Text,pair.Snap.FullPath,"divisions",root);if(scan.Diagnostics.Any(d=>d.Severity==NdfDiagnosticSeverity.Error)||scan.Objects.GroupBy(o=>o.Name).Any(g=>g.Count()>1))throw new TransactionValidationException("新师候选结构无效");}files.RemoveAll(f=>f.RelativePath.Equals(path,StringComparison.OrdinalIgnoreCase));files.Add(UnitApplyPlanner.ToWriteChange(pair.Snap,pair.Text,["战术师创建、名称与徽章"]));}
    }
}
