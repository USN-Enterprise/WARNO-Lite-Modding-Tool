using System.IO;
using WarnoLiteModdingTool.Core.Drafts;

namespace WarnoLiteModdingTool.App.ViewModels.Drafts;

public sealed class DraftItemViewModel : ObservableObject
{
    public DraftItemViewModel(ResolvedDraftOperation resolved, WarnoLiteModdingTool.Core.Strategic.StrategicWorkspace? strategic = null)
    {
        Resolved = resolved;
        _record=strategic?.Records.FirstOrDefault(r=>r.Id==resolved.Operation.ObjectName);
        if(IsStrategic)try{_changes=WarnoLiteModdingTool.Core.Strategic.StrategicDiff.Compare(resolved.Operation);}catch(Exception e)when(e is System.Text.Json.JsonException or InvalidDataException or NullReferenceException){_error="战略草稿无法解析，请查看冲突原因";}
    }

    private readonly WarnoLiteModdingTool.Core.Strategic.StrategicRecord? _record;
    private readonly IReadOnlyList<WarnoLiteModdingTool.Core.Strategic.StrategicChange> _changes=[];
    private readonly string? _error;
    private bool _selected;
    public bool IsSelected { get => _selected; set => SetProperty(ref _selected, value); }
    public string BatchSummary => Localisation.UiText.T("批量修改") + " · " + Localisation.UiText.T(WarnoLiteModdingTool.Core.Units.UnitFieldDefinitions.All.FirstOrDefault(f=>f.Key==Resolved.Operation.FieldKey)?.Label ?? Module);
    public string BatchKey => Resolved.Operation.GroupId ?? Resolved.Operation.Id;
    public ResolvedDraftOperation Resolved { get; }

    public bool IsStrategic => Resolved.Operation.TargetKind == DraftTargetKind.StrategicPlan;
    public string RawValues => Resolved.Operation.BaselineValue + "\n↓\n" + Resolved.Operation.TargetValue;
    public string ChangeDetails => _error ?? string.Join("\n\n",_changes.Select(c=>$"{Localisation.UiText.T(c.Path)}：{c.Before} → {c.After}"));
    public string Summary => IsStrategic ? $"{Localisation.UiText.T("将军模式")} · {Localisation.GameText.Display("country",string.IsNullOrEmpty(_record?.Country)?"未知":_record.Country)} · {_record?.DisplayName??ObjectName} · {_changes.Count} {Localisation.UiText.T("项修改")}" : Resolved.Operation.Summary;


    public string File => Resolved.Operation.RelativeSourceFile;

    public string Status => Resolved.Status == DraftResolutionStatus.Active ? "可恢复" : $"冲突：{Resolved.Reason}";

    public bool IsConflict => Resolved.Status == DraftResolutionStatus.Conflict;

    public string Module => Resolved.Operation.Module switch
    {
        "units" => "单位",
        "weapons" => "武器",
        "ammo" => "弹药",
        "divisions" => "战术师",
        "strategic" => "将军模式",
        "rules" => "游戏规则",
        _ => Resolved.Operation.Module
    };

    public string ObjectName => Resolved.Operation.ObjectName;

    public string ObjectType => Resolved.Operation.ObjectType;

    public string FieldPath => Resolved.Operation.FieldPath;

    public string BaselineValue => IsStrategic ? "以下为同一编制事务的全部修改" : Resolved.Operation.BaselineValue;

    public string TargetValue => IsStrategic ? ChangeDetails : Resolved.Operation.TargetValue;

    public string Scope => Resolved.Operation.EditScope switch
    {
        DraftEditScope.CurrentUnit => "仅当前 Unit",
        DraftEditScope.SelectedUnits => "所选 Unit",
        DraftEditScope.AllReferences => "全部引用",
        _ => "当前对象"
    };

    public string UpdatedText => Resolved.Operation.UpdatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}
