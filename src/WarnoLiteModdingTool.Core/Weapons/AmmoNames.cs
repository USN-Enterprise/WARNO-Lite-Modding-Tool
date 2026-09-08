using System.Text;
using System.Text.RegularExpressions;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
namespace WarnoLiteModdingTool.Core.Weapons;
public static class AmmoNames
{
    public static DraftOperation Operation(UnitWorkspaceData units,AmmoRecord ammo,string name,string token)=>new(
        DraftOperation.CreateId(DraftTargetKind.AmmoName,ammo.Source.RelativeSourceFile,ammo.Name,"ammo.name"),null,DraftTargetKind.AmmoName,"ammo",ammo.Source.RelativeSourceFile,ammo.Name,ammo.Source.TypeName,"ammo.name","Name","Text",ammo.DisplayName,ammo.NameRaw,name.Trim(),name.Trim(),$"弹药 · {ammo.DisplayName} · 名称：{ammo.DisplayName} → {name.Trim()}",token,true,DateTimeOffset.UtcNow,ammo.NameToken,DraftEditScope.AllReferences);
    public static ResolvedDraftOperation Resolve(UnitWorkspaceData units,WeaponWorkspaceData? weapons,DraftOperation op)
    {
        var a=weapons?.Ammo(op.ObjectName);
        var error=a is null?"弹药不存在":!a.CanEditName?"弹药名称字段或UNITS.csv目标不可编辑":a.Source.TypeName!=op.ObjectType||a.Source.RelativeSourceFile.Replace('\\','/')!=op.RelativeSourceFile.Replace('\\','/')?"弹药来源已变化":a.NameRaw!=op.BaselineRaw||a.NameToken!=op.BaselineNameToken||a.DisplayName!=op.BaselineValue?"弹药名称基线已变化":op.FieldKey!="ammo.name"||op.FieldPath!="Name"||string.IsNullOrWhiteSpace(op.TargetValue)||op.TargetRaw!=op.TargetValue||op.NameToken is null||!Regex.IsMatch(op.NameToken,"^[A-Z0-9]{10}$")||op.NameToken==a.NameToken||!op.RequiresNameTokenChange||op.EditScope!=DraftEditScope.AllReferences?"弹药名称草稿无效":"";
        return new(op,error.Length==0?DraftResolutionStatus.Active:DraftResolutionStatus.Conflict,error);
    }
    public static void Plan(string root,UnitWorkspaceData units,WeaponWorkspaceData weapons,IReadOnlyList<DraftOperation> operations,List<PlannedFileChange> files)
    {
        var names=operations.Where(o=>o.TargetKind==DraftTargetKind.AmmoName).ToArray();if(names.Length==0)return;
        var csv=Path.GetRelativePath(root,units.Localisation.UniqueUnitsCsvPath!).Replace('\\','/');
        var snapshots=new Dictionary<string,(TextFileSnapshot Snapshot,string Text)>(StringComparer.OrdinalIgnoreCase);
        string Get(string path,FormalTextFileKind kind){path=path.Replace('\\','/');if(!snapshots.ContainsKey(path)){var snap=TextFileSnapshot.Load(root,path,kind,allowMissing:kind==FormalTextFileKind.Csv);var prior=files.FirstOrDefault(f=>f.RelativePath.Equals(path,StringComparison.OrdinalIgnoreCase));snapshots[path]=(snap,prior is null?snap.Text:kind==FormalTextFileKind.Ndf?Encoding.UTF8.GetString(prior.CandidateBytes):new StreamReader(new MemoryStream(prior.CandidateBytes),true).ReadToEnd());}return snapshots[path].Text;}
        void Put(string path,string text){path=path.Replace('\\','/');snapshots[path]=(snapshots[path].Snapshot,text);}
        var occupied=units.Units.Select(u=>u.NameToken).Concat(weapons.Ammunition.Select(a=>a.NameToken)).ToHashSet();
        foreach(var op in names){var resolved=Resolve(units,weapons,op);if(resolved.Status!=DraftResolutionStatus.Active)throw new TransactionValidationException(resolved.Reason);if(!occupied.Add(op.NameToken!) || WarnoLiteModdingTool.Core.Localisation.VanillaNames.Lookup("UNITS",op.NameToken!) is not null)throw new TransactionValidationException("名称token已占用");
            var path=op.RelativeSourceFile.Replace('\\','/');var text=Get(path,FormalTextFileKind.Ndf);var scan=new NdfTopLevelScanner().Scan(text,Path.Combine(root,path),"ammo",root);var obj=scan.Objects.Single(o=>o.Name==op.ObjectName);var doc=new NdfSyntaxDocument(text,obj.CharacterOffset,obj.CharacterLength);var span=doc.FindDirectAssignments(doc.FindConstructors(obj.TypeName).Single(),"Name").Single();if(doc.Raw(span)!=op.BaselineRaw)throw new TransactionValidationException("弹药名称引用已变化");
            Put(path,SemicolonCsvDocument.ApplyReplacements(text,[new(doc.StartOffset(span),doc.Length(span),op.BaselineRaw,"'"+op.NameToken+"'","弹药独立名称")]));
            var table=Get(csv,FormalTextFileKind.Csv);if(table.Length==0)table="TOKEN;REFTEXT";var rows=SemicolonCsvDocument.Parse(table).Rows;if(rows.Count==0||rows[0].Fields.Count!=2||rows[0].Fields[0].Value.Trim()!="TOKEN"||rows[0].Fields[1].Value.Trim()!="REFTEXT"||rows.Skip(1).Any(r=>r.Fields.Count>0&&r.Fields[0].Value.Trim()==op.NameToken))throw new TransactionValidationException("名称表格式不兼容或token冲突");var nl=snapshots[csv].Snapshot.NewLine;Put(csv,table+(table.EndsWith('\n')?"":nl)+op.NameToken+";"+SemicolonCsvDocument.Quote(op.TargetValue)+nl);
        }
        foreach(var (path,pair) in snapshots){if(pair.Snapshot.Kind==FormalTextFileKind.Ndf&&new NdfTopLevelScanner().Scan(pair.Text,pair.Snapshot.FullPath,"ammo",root).Diagnostics.Any(d=>d.Severity==NdfDiagnosticSeverity.Error))throw new TransactionValidationException("弹药名称候选结构无效");files.RemoveAll(f=>f.RelativePath.Equals(path,StringComparison.OrdinalIgnoreCase));files.Add(new(path,pair.Snapshot.FullPath,pair.Snapshot.Kind,PlannedFileAction.Write,pair.Snapshot.Existed,pair.Snapshot.OriginalBytes,pair.Snapshot.Encode(pair.Text),pair.Snapshot.LastWriteUtc,["弹药名称"]));}
    }
}
