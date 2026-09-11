using System.Windows;
using System.Windows.Controls;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.App.Controls;

public sealed class UnitCreationWeaponPanel : StackPanel
{
    private readonly List<(WeaponRecord Weapon, MountedWeaponRecord Mount, SearchPicker Picker)> _rows=[];
    private IReadOnlyList<UnitCreationMountChoice> _unresolved=[];
    private sealed record Option(string Name,string Label,string Search);
    public IReadOnlyList<UnitCreationMountChoice> Choices => _unresolved.Concat(_rows
        .Where(r=>r.Picker.SelectedItem is Option a&&a.Name!=r.Mount.AmmoName)
        .Select(r=>new UnitCreationMountChoice(r.Weapon.Name,r.Mount.Index,((Option)r.Picker.SelectedItem!).Name))).ToArray();

    public void Load(UnitRecord mother,WeaponWorkspaceData data,IReadOnlyList<UnitCreationMountChoice> choices)
    {
        Children.Clear();_rows.Clear();
        var options=data.Ammunition.Select(a=>new Option(a.Name,string.IsNullOrWhiteSpace(a.DisplayName)?a.Name:a.DisplayName,a.SearchText)).ToArray();
        var group=0;
        foreach(var name in mother.Weapons.Distinct())
        {
            var weapon=data.Weapon(name);
            if(weapon is null){Children.Add(new TextBlock{Text=UiText.T("母版武器不存在"),ToolTip=name});continue;}
            Children.Add(new TextBlock{Text=UiText.T("武器配置")+" "+(++group),FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,14,0,0),ToolTip=name});
            foreach(var mount in weapon.Mounts)
            {
                var panel=new StackPanel{Margin=new Thickness(0,14,0,0)};Children.Add(panel);
                var shared=mount.AmmoBoxIndex is not null&&weapon.Mounts.Count(m=>m.AmmoBoxIndex==mount.AmmoBoxIndex)>1;
                panel.Children.Add(new TextBlock{Text=UiText.T("槽位")+" "+(mount.Index+1)+(shared?" · "+UiText.T("共用弹药箱"):""),ToolTip=name+"\nTurret "+mount.TurretIndex+" · AmmoBox "+mount.AmmoBoxIndex});
                var line=new DockPanel{Margin=new Thickness(0,5,0,5)};panel.Children.Add(line);
                var reset=new Button{Content=UiText.T("恢复母版"),Margin=new Thickness(8,0,0,0)};DockPanel.SetDock(reset,Dock.Right);line.Children.Add(reset);
                var selected=choices.FirstOrDefault(c=>c.WeaponName==name&&c.MountIndex==mount.Index)?.AmmoName??mount.AmmoName;
                var available=options.ToList();
                foreach(var id in new[]{mount.AmmoName,selected}.Distinct())if(!available.Any(a=>a.Name==id))available.Add(new(id,id+" · "+UiText.T("对象不可用"),id));
                var picker=new SearchPicker{ItemsSource=available,DisplayMemberPath="Label",SecondaryMemberPath="Search",SelectedItem=available.Single(a=>a.Name==selected),IsEnabled=mount.Fields.Count(f=>f.Definition.FieldName=="Ammunition")==1};line.Children.Add(picker);
                _rows.Add((weapon,mount,picker));
                var amount=new TextBlock{TextWrapping=TextWrapping.Wrap};panel.Children.Add(amount);
                string Count(string id)=>mount.AmmoBoxIndex is int box&&int.TryParse(weapon.Field("weapon.salves."+box)?.DisplayValue,out var salves)&&data.Ammo(id)?.ShotsPerSalvo is int shots?((long)salves*shots).ToString():UiText.T("无法推导");
                void Refresh(){var current=(picker.SelectedItem as Option)?.Name??mount.AmmoName;amount.Text=UiText.T("按当前弹药计算")+"："+Count(mount.AmmoName)+(current==mount.AmmoName?"":" → "+Count(current));amount.ToolTip="Salves[AmmoBoxIndex] × ShotsCountPerSalvo";}
                picker.SelectedItemChanged+=(_,_)=>Refresh();reset.Click+=(_,_)=>picker.SelectedItem=available.Single(a=>a.Name==mount.AmmoName);Refresh();
            }
        }
        // Keep stale draft choices visible and reject them on save instead of silently dropping them.
        _unresolved=choices.Where(c=>!_rows.Any(r=>r.Weapon.Name==c.WeaponName&&r.Mount.Index==c.MountIndex)).ToArray();
        if(_unresolved.Count>0)Children.Add(new TextBlock{Text=UiText.T("草稿槽位已不存在，请重新选择母版创建。"),TextWrapping=TextWrapping.Wrap});
        if(_rows.Count==0)Children.Add(new TextBlock{Text=UiText.T("母版没有可识别的武器槽位。")});
    }
}
