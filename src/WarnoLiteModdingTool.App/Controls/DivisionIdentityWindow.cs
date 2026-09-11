using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.Core.Divisions;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Images;
using MessageBox=WarnoLiteModdingTool.App.Localisation.LocalizedMessageBox;
namespace WarnoLiteModdingTool.App.Controls;
public sealed class DivisionIdentityWindow : Window
{
    public DraftOperation? Result {get;private set;}
    private sealed record Emblem(string Key,string Label,string Source);
    public DivisionIdentityWindow(DivisionWorkspaceData data,IReadOnlyList<DraftOperation> drafts,bool create,DivisionRecord? selected=null)
    {
        Title=UiText.T(create?"新建战术师":"名称与徽章");Width=660;Height=550;WindowStartupLocation=WindowStartupLocation.CenterOwner;SetResourceReference(BackgroundProperty,"SurfaceBrush");SetResourceReference(ForegroundProperty,"TextBrush");
        var root=new DockPanel{Margin=new Thickness(20)};Content=root;var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);var panel=new StackPanel();root.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var name=new TextBox{Margin=new Thickness(0,4,0,12)};var template=new SearchPicker{ItemsSource=data.Divisions.Where(d=>d.CanEdit).ToArray(),DisplayMemberPath="DisplayName",SecondaryMemberPath="Name",IsEnabled=create};
        var emblemPicker=new SearchPicker{DisplayMemberPath="Label",SecondaryMemberPath="Key",Margin=new Thickness(0,4,0,8)};var picture=new Image{Width=72,Height=72,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Left};var description=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,8)};
        var labels=data.Divisions.Select(d=>(Key:DivisionIdentity.Field(d,"EmblemTexture"),d.DisplayName)).GroupBy(d=>d.Key).ToDictionary(g=>g.Key,g=>g.First().DisplayName);
        var choices=ModTextures.Read(data.ProjectRoot,true).Where(t=>t.Key.StartsWith("Texture_Division_Emblem_",StringComparison.Ordinal)||labels.ContainsKey(t.Key)).Select(t=>new Emblem(t.Key,labels.GetValueOrDefault(t.Key)??t.Key,t.Source)).ToList();
        DivisionRecord? mother=null;DivisionIdentityState? state=null;CancellationTokenSource? cancellation=null;
        void Load(DivisionRecord? d,DivisionIdentityState? saved=null){mother=d;state=saved;if(d is null)return;name.Text=saved?.Name??d.DisplayName+(create?" "+UiText.T("新师"):"");var key=saved?.Emblem??DivisionIdentity.Field(d,"EmblemTexture");var list=choices.ToList();if(!list.Any(e=>e.Key==key))list.Add(new(key,key.Length==0?UiText.T("无师徽"):key,""));emblemPicker.ItemsSource=list;emblemPicker.SelectedItem=list.Single(e=>e.Key==key);description.Text=UiText.T(create?"沿用模板单位池、费用和默认牌组；应用后可在师页面继续调整。简介与历史文字沿用模板。":"修改师名称和已有徽章，名称及标题一起更新。简介与历史文字保持原内容。");}
        if(create){var pending=drafts.Where(o=>o.TargetKind==DraftTargetKind.DivisionIdentity&&DivisionIdentity.Read(o).Create).ToArray();if(pending.Length>0){panel.Children.Add(new TextBlock{Text=UiText.T("继续编辑待创建师")});var savedPicker=new SearchPicker{ItemsSource=pending,DisplayMemberPath="Summary",SecondaryMemberPath="ObjectName",Margin=new Thickness(0,4,0,12)};panel.Children.Add(savedPicker);savedPicker.SelectedItemChanged+=(_,_)=>{if(savedPicker.SelectedItem is DraftOperation op){var s=DivisionIdentity.Read(op);template.SelectedItem=data.Division(s.Mother);template.IsEnabled=false;Load(data.Division(s.Mother),s);}};}}
        panel.Children.Add(new TextBlock{Text=UiText.T(create?"母版师":"战术师")});panel.Children.Add(template);panel.Children.Add(new TextBlock{Text=UiText.T("名称"),Margin=new Thickness(0,12,0,0)});panel.Children.Add(name);panel.Children.Add(new TextBlock{Text=UiText.T("师徽")});panel.Children.Add(emblemPicker);panel.Children.Add(picture);panel.Children.Add(description);
        template.SelectedItemChanged+=(_,_)=>Load(template.SelectedItem as DivisionRecord);template.SelectedItem=selected??data.Divisions.FirstOrDefault(d=>d.CanEdit);Load(template.SelectedItem as DivisionRecord);
        if(!create&&selected is not null){var op=drafts.FirstOrDefault(o=>o.TargetKind==DraftTargetKind.DivisionIdentity&&o.ObjectName==selected.Name);if(op is not null)Load(selected,DivisionIdentity.Read(op));}
        async void Preview(){cancellation?.Cancel();cancellation?.Dispose();cancellation=new();var token=cancellation.Token;picture.Source=null;var e=emblemPicker.SelectedItem as Emblem;if(e is null||e.Source.Length==0)return;try{var image=await LocalGameImages.LoadAsync(data.ProjectRoot,e.Source,token);if(!token.IsCancellationRequested)picture.Source=image;}catch(OperationCanceledException){}catch(Exception ex)when(ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException or IndexOutOfRangeException or KeyNotFoundException or OverflowException or ZstdSharp.ZstdException){if(!token.IsCancellationRequested)picture.ToolTip=UiText.T("暂无图片");}}
        emblemPicker.SelectedItemChanged+=(_,_)=>Preview();Loaded+=(_,_)=>Preview();Closed+=(_,_)=>{cancellation?.Cancel();cancellation?.Dispose();};
        void Add(string label,Action action){var b=new Button{Content=UiText.T(label),Padding=new Thickness(14,8,14,8),Margin=new Thickness(8,12,0,0)};b.Click+=(_,_)=>action();actions.Children.Add(b);}
        Add("加入草稿",()=>{try{if(mother is null)throw new InvalidDataException("请选择母版师");var s=state??DivisionIdentity.New(data,mother,create,drafts);s=s with {Name=name.Text.Trim(),Emblem=(emblemPicker.SelectedItem as Emblem)?.Key??s.Emblem};var op=DivisionIdentity.Operation(mother,s);var resolved=DivisionIdentity.Resolve(data,op);if(resolved.Status!=DraftResolutionStatus.Active)throw new InvalidDataException(resolved.Reason);Result=op;DialogResult=true;}catch(Exception ex){MessageBox.Show(this,ex.Message,"无法保存草稿");}});Add("取消",Close);
    }
}
