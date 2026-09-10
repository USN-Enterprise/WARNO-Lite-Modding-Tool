using System.Text.Json;
using System.Text.RegularExpressions;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Divisions;
using WarnoLiteModdingTool.Core.Weapons;
using WarnoLiteModdingTool.Core.Indexing;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Transactions;
namespace WarnoLiteModdingTool.Core.Units;

public sealed record UnitCreationState(string Mother,string Id,string Guid,string Token,int SerializerId,string Name,
    Dictionary<string,string> Fields,bool IndependentWeapons,Dictionary<string,DivisionUnitRuleState> Divisions,
    Dictionary<string,string> DivisionBaselines,Dictionary<string,string> WeaponBaselines);
public static class UnitCreation
{
    public const string SerializerPath="GameData/Generated/Gameplay/Decks/DeckSerializer.ndf";
    public static UnitCreationState Read(DraftOperation op)=>JsonSerializer.Deserialize<UnitCreationState>(op.TargetRaw)??throw new InvalidDataException("创建草稿为空");
    public static string Source(UnitRecord u)=>(u.SourceSnapshot??File.ReadAllText(u.Source.SourceFile)).Substring(u.Source.CharacterOffset,u.Source.CharacterLength);
    public static DraftOperation Operation(UnitRecord mother,UnitCreationState state,string? baseline=null)
    {
        var json=JsonSerializer.Serialize(state);var raw=baseline??Source(mother);
        return new(DraftOperation.CreateId(DraftTargetKind.UnitCreate,mother.Source.RelativeSourceFile,state.Id,"unit.create"),"create:"+state.Id,DraftTargetKind.UnitCreate,"units",mother.Source.RelativeSourceFile,state.Id,mother.Source.TypeName,"unit.create","Unit/Create","UnitCreation",raw,raw,json,json,$"新增单位 · {state.Name}",state.Token,true,DateTimeOffset.UtcNow);
    }
    public static ResolvedDraftOperation Resolve(UnitWorkspaceData data,DraftOperation op)
    {
        try{
            var s=Read(op);var mother=data.Units.SingleOrDefault(u=>u.Name==s.Mother);
            if(mother is null||Source(mother)!=op.BaselineRaw)throw new InvalidDataException("母版已变化或不存在");
            if(s.Id!=op.ObjectName||!Regex.IsMatch(s.Id,@"^Descriptor_Unit_[A-Za-z0-9_]+$")||!Guid.TryParse(s.Guid,out _)||!Regex.IsMatch(s.Token,@"^[A-Z0-9]{10}$")||s.SerializerId<0||string.IsNullOrWhiteSpace(s.Name))throw new InvalidDataException("新单位身份无效");
            if(data.Units.Any(u=>u.Name==s.Id||u.NameToken==s.Token) || Localisation.VanillaNames.Lookup("UNITS",s.Token) is not null)throw new InvalidDataException("新单位名称或token已占用");
            return new(op,DraftResolutionStatus.Active,"");
        }catch(Exception ex)when(ex is IOException or InvalidDataException or InvalidOperationException or JsonException or ArgumentException){return new(op,DraftResolutionStatus.Conflict,ex.Message);}
    }
    public static UnitCreationState New(UnitRecord mother,UnitWorkspaceData data,IEnumerable<DraftOperation> drafts)
    {
        Localisation.VanillaNames.RequireAvailable();
        var root=data.Localisation.ProjectRoot;
        var text=File.ReadAllText(Path.Combine(root,SerializerPath));var doc=new NdfSyntaxDocument(text);
        var used=doc.ReadMapEntries(doc.FindDirectAssignments(doc.FindConstructors("TDeckSerializerEntries").Single(),"UnitIds").Single()).Select(e=>int.Parse(doc.Raw(e.Value))).ToHashSet();
        foreach(var op in drafts.Where(o=>o.TargetKind==DraftTargetKind.UnitCreate))used.Add(Read(op).SerializerId);
        var next=used.Count==0?0:checked(used.Max()+1);
        string suffix;
        do { suffix=Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(); }
        while (Localisation.VanillaNames.Lookup("UNITS",suffix) is not null || data.Units.Any(u=>u.NameToken==suffix));
        return new(mother.Name,"Descriptor_Unit_WL_"+suffix,Guid.NewGuid().ToString(),suffix,next,mother.DisplayName+" 新单位",[],false,[],[],[]);
    }
    private static string Patch(string text,IReadOnlyList<TextReplacement> edits)=>SemicolonCsvDocument.ApplyReplacements(text,edits);
    private static void Assign(NdfSyntaxDocument doc,string text,List<TextReplacement> edits,string constructor,string field,string value,bool required=true)
    {
        var matches=doc.FindConstructors(constructor).SelectMany(c=>doc.FindDirectAssignments(c,field)).ToArray();
        if(matches.Length!=1){if(required)throw new InvalidDataException($"母版字段不能唯一定位：{field}");return;}
        var span=matches[0];edits.Add(new(doc.StartOffset(span),doc.Length(span),doc.Raw(span),value,field));
    }
    public static string Clone(UnitRecord mother,UnitCreationState state,string source)
    {
        var doc=new NdfSyntaxDocument(source);var edits=new List<TextReplacement>();
        var top=Regex.Match(source,@"\b"+Regex.Escape(mother.Name)+@"\s+is\b");if(!top.Success)throw new InvalidDataException("母版声明无法定位");
        edits.Add(new(top.Index,mother.Name.Length,mother.Name,state.Id,"新单位"));
        Assign(doc,source,edits,mother.Source.TypeName,"DescriptorId","GUID:{"+state.Guid+"}");
        foreach(var span in doc.FindAssignmentsAnywhere("DescriptorId")){if(edits.Any(e=>e.Offset==doc.StartOffset(span)))continue;edits.Add(new(doc.StartOffset(span),doc.Length(span),doc.Raw(span),"GUID:{"+Guid.NewGuid()+"}","模块GUID"));}
        Assign(doc,source,edits,mother.Source.TypeName,"ClassNameForDebug","'"+state.Id["Descriptor_".Length..]+"'");
        Assign(doc,source,edits,"TUnitUIModuleDescriptor","NameToken","'"+state.Token+"'");
        foreach(var tagSet in doc.FindConstructors("TTagsModuleDescriptor").SelectMany(c=>doc.FindDirectAssignments(c,"TagSet")))
            foreach(var span in doc.ReadArrayElements(tagSet))if(NdfSyntaxDocument.Unquote(doc.Raw(span)).StartsWith("UNITE_",StringComparison.Ordinal))edits.Add(new(doc.StartOffset(span),doc.Length(span),doc.Raw(span),'"'+"UNITE_"+state.Id["Descriptor_Unit_".Length..]+'"',"身份标签"));
        foreach(var (key,value) in state.Fields){var f=mother.Field(key)??throw new InvalidDataException("未知字段");if(key=="structure.tags"||key=="structure.upgradeFrom")throw new InvalidDataException("创建向导不修改身份标签或升级链");if(key=="structure.specialties"&&f.Availability==UnitFieldAvailability.Missing)
        {
            var ui=doc.FindConstructors("TUnitUIModuleDescriptor");
            if(ui.Count!=1||!UnitValueConverter.TryFormatTarget(f,value,out _,out var inserted,out _))throw new InvalidDataException("不能安全补建单位特性");
            var at=doc.StartOffset(new NdfValueSpan(ui[0].CloseTokenIndex,ui[0].CloseTokenIndex));
            edits.Add(new(at,0,"","SpecialtiesList = "+inserted+(source.Contains("\r\n")?"\r\n":"\n"),key));continue;
        }
        if(!f.CanEdit||f.Location is null||!UnitValueConverter.TryFormatTarget(f,value,out _,out var raw,out var error))throw new InvalidDataException("字段不可编辑："+key);edits.Add(new(f.Location.CharacterOffset-mother.Source.CharacterOffset,f.Location.CharacterLength,f.RawValue,raw,key));}
        return Patch(source,edits);
    }
    public static UnitRecord Project(UnitRecord mother,UnitCreationState state,string source)
    {
        var text=Clone(mother,state,source);var scan=new NdfTopLevelScanner().Scan(text,mother.Source.SourceFile,"units",Path.GetDirectoryName(mother.Source.SourceFile)!);
        var unit=new UnitCatalogBuilder().Build(scan.Objects.Select(o=>o with {RelativeSourceFile=mother.Source.RelativeSourceFile}).ToArray(),new Dictionary<string,string>{{mother.Source.SourceFile,text}}).Single();
        unit.DisplayName=state.Name;unit.NameToken=state.Token;unit.CanEditName=true;unit.UnitsCsvRelativePath=mother.UnitsCsvRelativePath;
        unit.ReplaceFields(unit.Fields.Select(f=>f with {Choices=mother.Field(f.Definition.Key)?.Choices??f.Choices}).ToArray());return unit;
    }
    public static void Plan(string root,UnitWorkspaceData units,WeaponWorkspaceData weapons,DivisionWorkspaceData? divisions,ProjectIndexResult index,IReadOnlyList<DraftOperation> operations,List<PlannedFileChange> files)
    {
        var creates=operations.Where(o=>o.TargetKind==DraftTargetKind.UnitCreate).ToArray();if(creates.Length==0)return;
        Localisation.VanillaNames.RequireAvailable();
        var csv=units.Localisation.UniqueUnitsCsvPath??throw new TransactionValidationException("无法唯一定位UNITS.csv声明，不能新增单位");
        var existingNames=index.Objects.Select(o=>o.Name).ToHashSet();var tokens=units.Units.Select(u=>u.NameToken).ToHashSet();var ids=new HashSet<int>();
        var guids=units.Units.Select(u=>u.SourceSnapshot).Where(s=>s is not null).Distinct(ReferenceEqualityComparer.Instance).Cast<string>().SelectMany(text=>{var doc=new NdfSyntaxDocument(text);return doc.FindAssignmentsAnywhere("DescriptorId").Select(doc.Raw).ToArray();}).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var touched=new Dictionary<string,(TextFileSnapshot Snapshot,string Text)> (StringComparer.OrdinalIgnoreCase);
        string Get(string path,FormalTextFileKind kind=FormalTextFileKind.Ndf){path=path.Replace('\\','/');if(!touched.TryGetValue(path,out var pair)){var snap=TextFileSnapshot.Load(root,path,kind,allowMissing:kind==FormalTextFileKind.Csv);var prior=files.FirstOrDefault(f=>f.RelativePath.Equals(path,StringComparison.OrdinalIgnoreCase));pair=(snap,prior is null?snap.Text:Decode(prior,snap));touched[path]=pair;}return pair.Text;}
        void Put(string path,string text,FormalTextFileKind kind=FormalTextFileKind.Ndf){path=path.Replace('\\','/');Get(path,kind);touched[path]=(touched[path].Snapshot,text);}
        foreach(var op in creates)
        {
            var resolved=Resolve(units,op);if(resolved.Status!=DraftResolutionStatus.Active)throw new TransactionValidationException(resolved.Reason);
            var state=Read(op);var mother=units.Units.Single(u=>u.Name==state.Mother);
            if(!existingNames.Add(state.Id)||!tokens.Add(state.Token)||!ids.Add(state.SerializerId))throw new TransactionValidationException("新单位身份冲突");
            if(!guids.Add("GUID:{"+state.Guid+"}"))throw new TransactionValidationException("新单位GUID已占用");
            var clone=Clone(mother,state,op.BaselineRaw);
            // Re-read generated values before touching any formal file.
            var projected=Project(mother,state,op.BaselineRaw);
            foreach(var side in new[]{"front","side","rear","top"}){var family=projected.Field("armor."+side+".family");var value=projected.Field("armor."+side);if(family is null||value?.CanEdit!=true)continue;var def=units.DamageResistance.ResistanceFamilies.FirstOrDefault(f=>f.Name==NdfSyntaxDocument.Leaf(family.RawValue));if(def is not null&&int.TryParse(value.DisplayValue,out var n)&&(n<1||n>def.MaximumIndex||def.Name=="ResistanceFamily_infanterie"&&n!=1))throw new TransactionValidationException("新单位护甲类型与数值不匹配");}
            if(state.IndependentWeapons){var doc=new NdfSyntaxDocument(clone);var changes=new List<TextReplacement>();var clonedWeapons=new HashSet<string>();foreach(var reference in doc.FindReferences("WeaponDescriptor_")){
                var weapon=weapons.Weapon(reference.Leaf)??throw new TransactionValidationException("母版武器引用不存在");var original=File.ReadAllText(weapon.Source.SourceFile).Substring(weapon.Source.CharacterOffset,weapon.Source.CharacterLength);
                if(!state.WeaponBaselines.TryGetValue(weapon.Name,out var baseline)||baseline!=original)throw new TransactionValidationException("武器母版已变化");
                var name=weapon.Name+"_"+state.Id["Descriptor_Unit_".Length..];
                if(clonedWeapons.Add(name)){if(!existingNames.Add(name))throw new TransactionValidationException("独立武器名称已占用");var wdoc=new NdfSyntaxDocument(original);var match=Regex.Match(original,@"\b"+Regex.Escape(weapon.Name)+@"\s+is\b");var edits=new List<TextReplacement>{new(match.Index,weapon.Name.Length,weapon.Name,name,"独立武器")};foreach(var span in wdoc.FindAssignmentsAnywhere("DescriptorId"))edits.Add(new(wdoc.StartOffset(span),wdoc.Length(span),wdoc.Raw(span),"GUID:{"+Guid.NewGuid()+"}","武器GUID"));var body=Patch(original,edits);var old=Get(weapon.Source.RelativeSourceFile);Put(weapon.Source.RelativeSourceFile,old+touched[weapon.Source.RelativeSourceFile.Replace('\\','/')].Snapshot.NewLine+body);}
                changes.Add(new(doc.StartOffset(reference.Span),doc.Length(reference.Span),reference.Raw,"$/GFX/Weapon/"+name,"武器引用"));}clone=Patch(clone,changes);}
            var source=Get(mother.Source.RelativeSourceFile);Put(mother.Source.RelativeSourceFile,source+touched[mother.Source.RelativeSourceFile.Replace('\\','/')].Snapshot.NewLine+clone);
            var serializer=Get(SerializerPath);var sd=new NdfSyntaxDocument(serializer);var map=sd.FindDirectAssignments(sd.FindConstructors("TDeckSerializerEntries").Single(),"UnitIds").Single();var entries=sd.ReadMapEntries(map);if(entries.Any(e=>int.Parse(sd.Raw(e.Value))==state.SerializerId||NdfSyntaxDocument.Leaf(sd.Raw(e.Key))==state.Id))throw new TransactionValidationException("牌组编号已占用");var insertion=sd.StartOffset(map)+sd.Length(map)-1;Put(SerializerPath,serializer.Insert(insertion,(sd.NeedsArraySeparator(map)?","+touched[SerializerPath].Snapshot.NewLine:"")+$"    ({state.Id}, {state.SerializerId}),"+touched[SerializerPath].Snapshot.NewLine));
            var csvPath=Path.GetRelativePath(root,csv).Replace('\\','/');var csvText=Get(csvPath,FormalTextFileKind.Csv);if(csvText.Length==0)csvText="TOKEN;REFTEXT";var csvDoc=SemicolonCsvDocument.Parse(csvText);if(csvDoc.Rows[0].Fields.Count!=2||csvDoc.Rows[0].Fields[0].Value.Trim()!="TOKEN"||csvDoc.Rows[0].Fields[1].Value.Trim()!="REFTEXT"||csvDoc.Rows.Skip(1).Any(r=>r.Fields[0].Value==state.Token))throw new TransactionValidationException("名称表格式不兼容或token冲突");var nl=touched[csvPath].Snapshot.NewLine;Put(csvPath,csvText+(csvText.EndsWith('\n')?"":nl)+state.Token+";"+SemicolonCsvDocument.Quote(state.Name)+nl,FormalTextFileKind.Csv);
            foreach(var (divisionId,rule) in state.Divisions){var division=divisions?.Division(divisionId)??throw new TransactionValidationException("所选师不可用");if(!state.DivisionBaselines.TryGetValue(divisionId,out var baseline)||baseline!=DivisionDraftCodec.Serialize(division.Baseline))throw new TransactionValidationException("所选师基线已变化");
                var desired=rule with {Unit=state.Id};var candidate=division.Baseline with {UnitRules=division.Baseline.UnitRules.Append(desired).ToArray()};var expanded=units with {Units=units.Units.Append(projected).ToArray()};var errors=DivisionStateValidator.Validate(divisions! with {Units=expanded},division,candidate);if(errors.Count>0)throw new TransactionValidationException(string.Join(";",errors));
                var path=division.UnitRuleList.RelativeSourceFile;var text=Get(path);var doc=new NdfSyntaxDocument(text);var objects=doc.FindConstructors("TDeckDivisionRule");
                // Locate the exact named rules object, then its existing array; preserve every old rule byte.
                var scan=new NdfTopLevelScanner().Scan(text,Path.Combine(root,path),"divisions",root);var obj=scan.Objects.Single(o=>o.Name==division.DivisionRuleName);var objDoc=new NdfSyntaxDocument(text,obj.CharacterOffset,obj.CharacterLength);
                var lists=objDoc.FindDirectAssignments(objDoc.FindConstructors("TDeckDivisionRule").Single(),"UnitRuleList");if(lists.Count!=1)throw new TransactionValidationException("师单位池无法定位");var list=lists[0];var oldElements=objDoc.ReadArrayElements(list).Select(objDoc.Raw).ToArray();var at=objDoc.StartOffset(list)+objDoc.Length(list)-1;var block=DivisionApplyPlanner.FormatRules([desired]);block=(objDoc.NeedsArraySeparator(list)?",":"")+block[(block.IndexOf('[')+1)..block.LastIndexOf(']')];var newline=touched[path.Replace('\\','/')].Snapshot.NewLine;var candidateText=text.Insert(at,block.Replace("\n",newline));var candidateDoc=new NdfSyntaxDocument(candidateText,obj.CharacterOffset,obj.CharacterLength+block.Replace("\n",newline).Length);var candidateList=candidateDoc.FindDirectAssignments(candidateDoc.FindConstructors("TDeckDivisionRule").Single(),"UnitRuleList").Single();var elements=candidateDoc.ReadArrayElements(candidateList).ToArray();if(elements.Length!=oldElements.Length+1||!elements.Take(oldElements.Length).Select(candidateDoc.Raw).SequenceEqual(oldElements)||candidateDoc.FindConstructors("TDeckUniteRule",elements[^1]).Count!=1)throw new TransactionValidationException("新增师规则候选回读失败");Put(path,candidateText);
            }
        }
        foreach(var (path,pair) in touched){if(pair.Snapshot.Kind==FormalTextFileKind.Ndf){var scan=new NdfTopLevelScanner().Scan(pair.Text,pair.Snapshot.FullPath,"units",root);if(scan.Diagnostics.Any(d=>d.Severity==NdfDiagnosticSeverity.Error)||scan.Objects.GroupBy(o=>o.Name).Any(g=>g.Count()>1))throw new TransactionValidationException("创建候选结构或对象唯一性校验失败");}files.RemoveAll(f=>f.RelativePath.Equals(path,StringComparison.OrdinalIgnoreCase));files.Add(new(path,pair.Snapshot.FullPath,pair.Snapshot.Kind,PlannedFileAction.Write,pair.Snapshot.Existed,pair.Snapshot.OriginalBytes,pair.Snapshot.Encode(pair.Text),pair.Snapshot.LastWriteUtc,["新增单位及注册"]));}
    }
    private static string Decode(PlannedFileChange file,TextFileSnapshot snapshot){var bytes=file.CandidateBytes;return snapshot.Kind==FormalTextFileKind.Ndf?System.Text.Encoding.UTF8.GetString(bytes):new StreamReader(new MemoryStream(bytes),true).ReadToEnd();}
}
