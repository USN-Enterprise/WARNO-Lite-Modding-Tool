using System.Windows;
using WarnoLiteModdingTool.App.Controls;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Images;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.App.ViewModels.Units;

public sealed partial class UnitWorkspaceViewModel
{
    public UnitPictureState? SelectedPicture
    {
        get
        {
            if(SelectedUnit is null)return null;
            var unit=SelectedUnit.Unit;
            var creation=_draftStore.Operations.FirstOrDefault(o=>o.TargetKind==DraftTargetKind.UnitCreate && o.ObjectName==unit.Name);
            if(creation is not null)return UnitCreation.Read(creation).Picture;
            var picture=_draftStore.Operations.FirstOrDefault(o=>o.TargetKind==DraftTargetKind.UnitPicture && o.ObjectName==unit.Name);
            return picture is not null && UnitPictures.Resolve(_data,picture).Status==DraftResolutionStatus.Active?UnitPictures.Read(picture):null;
        }
    }
    public async Task EditPictureAsync(Window owner)
    {
        await FlushAsync();
        if(!CanEditSelectedLifecycle || SelectedUnit is null)throw new InvalidOperationException(Localisation.UiText.T("请先选择可编辑单位"));
        var unit=SelectedUnit.Unit;_ = UnitPictures.Raw(UnitCreation.Source(unit));
        var creation=_draftStore.Operations.FirstOrDefault(o=>o.TargetKind==DraftTargetKind.UnitCreate && o.ObjectName==unit.Name);
        var old=_draftStore.Operations.FirstOrDefault(o=>o.TargetKind==DraftTargetKind.UnitPicture && o.ObjectName==unit.Name);
        var catalog=await Task.Run(()=>UnitPictures.Catalog(_draftStore.ProjectRoot));
        var state=creation is not null?UnitCreation.Read(creation).Picture:old is not null?UnitPictures.Read(old):null;
        var window=new UnitPictureWindow(_draftStore.ProjectRoot,unit,_data.Units,catalog,state){Owner=owner};
        if(window.ShowDialog()!=true || window.State is null)return;
        if(creation is not null)
        {
            var updated=UnitCreation.Read(creation) with {Picture=window.State};
            await _draftStore.UpsertAsync(UnitCreation.Operation(_data.Units.Single(u=>u.Name==updated.Mother),updated,creation.BaselineRaw));
        }
        else if(window.State.PngBase64 is null && window.State.Key==ModTextures.UnitKey(unit))
        {if(old is not null)await _draftStore.RemoveAsync(old.Id);}
        else await _draftStore.UpsertAsync(UnitPictures.Operation(unit,window.State));
        RefreshExternalDraftState();_setStatus("单位图片已保存草稿");
    }
}
