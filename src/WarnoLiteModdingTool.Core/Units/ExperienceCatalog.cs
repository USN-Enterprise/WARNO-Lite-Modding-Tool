using System.Text.RegularExpressions;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Transactions;
namespace WarnoLiteModdingTool.Core.Units;

public sealed class ExperienceCatalog
{
    public sealed record Pack(string Name,string Raw,string Summary,string? Error);
    public List<Pack> Packs { get; } = [];
    public Dictionary<string,byte[]> Dependencies { get; } = new(StringComparer.OrdinalIgnoreCase);
    public static ExperienceCatalog Load(string root)
    {
        var result=new ExperienceCatalog();var objects=new Dictionary<string,List<(string Type,string Text)>>(StringComparer.Ordinal);
        var dir=Path.Combine(root,"GameData");if(!Directory.Exists(dir))return result;
        foreach(var path in Directory.EnumerateFiles(dir,"*.ndf",SearchOption.AllDirectories))
        {
            var text=File.ReadAllText(path);if(!text.Contains("TExperienceLevelsPackDescriptor")&&!text.Contains("TEffectsPackDescriptor"))continue;
            result.Dependencies[Path.GetRelativePath(root,path).Replace('\\','/')]=File.ReadAllBytes(path);
            foreach(var o in new NdfTopLevelScanner().Scan(text,path,"experience",root).Objects)
            {if(!objects.TryGetValue(o.Name,out var list))objects[o.Name]=list=[];list.Add((o.TypeName,text.Substring(o.CharacterOffset,o.CharacterLength)));}
        }
        foreach(var (name,items) in objects.Where(p=>p.Value.Any(o=>o.Type=="TExperienceLevelsPackDescriptor")))
        {
            string? error=null;var summary=new List<string>();
            if(items.Count!=1)error="经验配置声明不唯一";
            else
            {
                var doc=new NdfSyntaxDocument(items[0].Text);var levels=doc.FindConstructors("TExperienceLevelDescriptor");
                if(levels.Count!=4)error="当前切换要求0–3四档经验配置";
                for(var i=0;i<levels.Count;i++)
                {
                    string Value(string key){var v=doc.FindDirectAssignments(levels[i],key);return v.Count==1?doc.Raw(v[0]):"未显式配置";}
                    summary.Add($"{i}：门槛加值 {Value("ThresholdAdditionalValue")} · 价格系数 {Value("ThresholdPriceMultiplier")}");
                    var effects=doc.FindDirectAssignments(levels[i],"LevelEffectsPacks");
                    if(effects.Count==0){summary.Add("  未显式配置等级效果");continue;}
                    if(effects.Count!=1){error="等级效果赋值不唯一";continue;}
                    foreach(var span in doc.ReadArrayElements(effects[0]))
                    {
                        var raw=doc.Raw(span);var leaf=NdfSyntaxDocument.Leaf(raw);
                        if(!(raw.StartsWith("~/")||raw.StartsWith("$/GFX/EffectCapacity/"))||!objects.TryGetValue(leaf,out var found)||found.Count!=1||found[0].Type!="TEffectsPackDescriptor"){error="等级效果引用缺失、多义或不支持："+raw;continue;}
                        summary.AddRange(Summarize(found[0].Text).Select(s=>"  "+s));
                    }
                }
            }
            result.Packs.Add(new(name,"~/"+name,string.Join("\n",summary),error));
        }
        return result;
    }
    private static IEnumerable<string> Summarize(string text)
    {
        var doc=new NdfSyntaxDocument(text);
        var known=new Dictionary<string,(string Label,string Field)>{
            ["TUnitEffectIncreaseDamageTakenDescriptor"]=("承伤修饰 / Damage taken","BonusDamage"),
            ["TUnitEffectIncreaseSpeedDescriptor"]=("速度 / Speed","BonusSpeedBaseInPercent"),
            ["TBonusWeaponAimtimeEffectDescriptor"]=("瞄准时间 / Aim time","ModifierValue"),
            ["TUnitEffectIncreaseWeaponPrecisionArretDescriptor"]=("静止精度 / Stationary accuracy","ModifierValue"),
            ["TUnitEffectIncreaseWeaponPrecisionMouvementDescriptor"]=("移动精度 / Moving accuracy","ModifierValue"),
            ["TUnitEffectAlterWeaponTempsEntreDeuxSalvesDescriptor"]=("齐射间隔 / Salvo interval","ModifierValue"),
            ["TUnitEffectIncreaseWeaponDispersionMaxRangeDescriptor"]=("最大射程散布 / Dispersion","ModifierValue"),
            ["TUnitEffectHealOverTimeDescriptor"]=("恢复每秒 / Recovery per second","HealUnitsPerSecond"),
            ["TUnitEffectBonusPrecisionWhenTargetedDescriptor"]=("被瞄准精度 / Accuracy when targeted","BonusPrecisionWhenTargeted"),
            ["TUnitEffectRaiseTagDescriptor"]=("标签 / Tags","TagListToRaise")};
        foreach(var type in Regex.Matches(text,@"\b(T\w+)\s*\(").Select(m=>m.Groups[1].Value).Distinct())
        {
            if(type=="TEffectsPackDescriptor")continue;
            foreach(var c in doc.FindConstructors(type))
            {
                string Value(string key){var spans=doc.FindDirectAssignments(c,key);return spans.Count==1?doc.Raw(spans[0]):"";}
                if(known.TryGetValue(type,out var info))yield return info.Label+": "+Value(info.Field)+" "+Value("ModifierType").Replace("~/ModifierType_","").Replace("Additionnel","加值 / additive").Replace("Multiplicatif","倍率 / multiplier").Replace("Pourcentage","百分比 / percent")+" "+Value("DamageType").Replace("EDamageType/Suppress","压制 / suppression");
                else yield return "未解释效果 / Uninterpreted effect: "+type;
            }
        }
    }
    public static string Label(string name)
    {
        var alias=name switch {"ExperienceLevelsPackDescriptor_XP_pack_simple_v3"=>"普通", "ExperienceLevelsPackDescriptor_XP_pack_SF_v2"=>"特种经验", "ExperienceLevelsPackDescriptor_XP_pack_artillery"=>"火炮", "ExperienceLevelsPackDescriptor_XP_pack_helico"=>"直升机", "ExperienceLevelsPackDescriptor_XP_pack_avion"=>"固定翼",_=>"自定义"};
        return alias+" · "+name;
    }
    public void Apply(IReadOnlyList<UnitRecord> units)
    {
        var choices=Packs.Where(p=>p.Error is null).Select(p=>new UnitChoice(Label(p.Name),p.Raw)).OrderBy(c=>c.Display).ToArray();
        foreach(var unit in units)unit.ReplaceFields(unit.Fields.Select(f=>
        {
            if(f.Definition.Key!="experience.type")return f;
            var p=Packs.SingleOrDefault(p=>p.Raw==f.RawValue);
            return f with {DisplayValue=p is null?f.DisplayValue:Label(p.Name),Choices=choices,ChoiceDetails=Packs.ToDictionary(p=>Label(p.Name),p=>p.Error??p.Summary)};
        }).ToArray());
    }
    public void Validate(IEnumerable<DraftOperation> operations)
    {
        foreach(var op in operations.Where(o=>o.FieldKey=="experience.type"))
            if(!Packs.Any(p=>p.Raw==op.TargetRaw&&p.Error is null))throw new TransactionValidationException("目标经验配置无效，请刷新："+op.TargetRaw);
    }
}
