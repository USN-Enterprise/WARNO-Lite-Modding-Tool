using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.Core.Images;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.App.Controls;

public sealed class UnitPictureWindow : Window
{
    private readonly string _root;
    private readonly Image _preview=new(){Height=230,Stretch=Stretch.Uniform};
    private readonly TextBlock _status=new(){TextWrapping=TextWrapping.Wrap,Margin=new(4,10,4,4)};
    private readonly TextBlock _identity=new(){TextWrapping=TextWrapping.Wrap,Margin=new(4)};
    private CancellationTokenSource? _cancel;
    private UnitPictureTexture? _anchor;
    public UnitPictureState? State {get;private set;}
    public sealed record Choice(UnitPictureTexture Texture,string Display);

    public UnitPictureWindow(string root,UnitRecord unit,IReadOnlyList<UnitRecord> units,IReadOnlyList<UnitPictureTexture> catalog,UnitPictureState? state)
    {
        _root=root;State=state;
        Title=UiText.T("修改单位图片");Width=1040;Height=700;MinWidth=760;MinHeight=540;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"SurfaceBrush");SetResourceReference(ForegroundProperty,"TextBrush");
        var rootPanel=new DockPanel{Margin=new(18)};rootPanel.SetResourceReference(Panel.BackgroundProperty,"SurfaceBrush");Content=rootPanel;
        var bottom=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(bottom,Dock.Bottom);rootPanel.Children.Add(bottom);
        Add(bottom,"保存草稿",()=>{if(State is null)throw new InvalidOperationException("请先选择或导入单位图片");DialogResult=true;});Add(bottom,"取消",Close);
        var heading=new StackPanel();DockPanel.SetDock(heading,Dock.Top);rootPanel.Children.Add(heading);
        heading.Children.Add(new TextBlock{Text=unit.DisplayName,FontSize=20,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
        heading.Children.Add(new TextBlock{Text=UiText.T("仅修改所选单位的卡片图片；原图保留。保存后进入草稿，应用后需生成Mod。"),TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,16)});
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=new(1.1,GridUnitType.Star)});rootPanel.Children.Add(grid);
        var left=new DockPanel{Margin=new(0,0,18,0)};grid.Children.Add(left);
        var label=new TextBlock{Text=UiText.T("已有单位图片"),FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,8)};DockPanel.SetDock(label,Dock.Top);left.Children.Add(label);
        var search=new TextBox{ToolTip=UiText.T("搜索单位名称或图片标识"),Margin=new(0,0,0,8)};DockPanel.SetDock(search,Dock.Top);left.Children.Add(search);
        var list=new ListBox{DisplayMemberPath="Display",Tag="unit-picture-choices",HorizontalContentAlignment=HorizontalAlignment.Stretch};left.Children.Add(list);
        var names=units.GroupBy(u=>ModTextures.UnitKey(u)??"").ToDictionary(g=>g.Key,g=>string.Join(" / ",g.Select(u=>u.DisplayName).Distinct().Take(3)));
        var unitKeys=names.Keys.ToHashSet();
        var banks=catalog.Where(t=>unitKeys.Contains(t.Key)).Select(t=>(t.File,t.Bank)).ToHashSet();
        var choices=catalog.Where(t=>banks.Contains((t.File,t.Bank))).GroupBy(t=>t.Key).Where(g=>g.Count()==1 && g.Single().Source.Length>0).Select(g=>new Choice(g.Single(),(names.GetValueOrDefault(g.Key)??UiText.T("未被单位使用"))+"\n"+g.Key)).OrderBy(c=>c.Display).ToArray();
        void Fill(){list.ItemsSource=choices.Where(c=>c.Display.Contains(search.Text,StringComparison.OrdinalIgnoreCase)).ToArray();}
        search.TextChanged+=(_,_)=>Fill();Fill();
        var right=new StackPanel();Grid.SetColumn(right,1);var scroll=new ScrollViewer{Content=right,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetColumn(scroll,1);grid.Children.Add(scroll);
        right.Children.Add(new TextBlock{Text=UiText.T("结果预览"),FontWeight=FontWeights.SemiBold});
        var background=new Border{Child=_preview,Padding=new(12),Margin=new(0,8,0,8),Background=Brushes.DimGray};right.Children.Add(background);
        var backgrounds=new ComboBox{ItemsSource=new[]{UiText.T("深色预览"),UiText.T("浅色预览")},SelectedIndex=0};backgrounds.SelectionChanged+=(_,_)=>background.Background=backgrounds.SelectedIndex==0?Brushes.DimGray:Brushes.WhiteSmoke;right.Children.Add(backgrounds);
        right.Children.Add(_identity);
        var actions=new WrapPanel();right.Children.Add(actions);
        Add(actions,"导入自定义图片",()=>Edit(false));Add(actions,"编辑预览图片",()=>Edit(true));
        Add(actions,"还原本次选择",()=>{State=state;_anchor=state?.Template??catalog.Where(t=>t.Key==ModTextures.UnitKey(unit)).GroupBy(t=>t.Key).Where(g=>g.Count()==1).Select(g=>g.Single()).FirstOrDefault();list.SelectedItem=null;Reload();});
        right.Children.Add(new TextBlock{Text=UiText.T("保留图片比例和透明度。裁剪、缩放与圆形透明处理在独立编辑窗口中进行。"),TextWrapping=TextWrapping.Wrap,Margin=new(4,12,4,4)});
        right.Children.Add(_status);
        _anchor=state?.Template??catalog.Where(t=>t.Key==ModTextures.UnitKey(unit)).GroupBy(t=>t.Key).Where(g=>g.Count()==1).Select(g=>g.Single()).FirstOrDefault();
        list.SelectionChanged+=(_,_)=>{if(list.SelectedItem is Choice choice){_anchor=choice.Texture;State=new(choice.Texture.Key,choice.Texture);Reload();}};
        Loaded+=(_,_)=>Reload();Closed+=(_,_)=>{_cancel?.Cancel();_cancel?.Dispose();};
    }
    private void Add(Panel panel,string label,Action action)
    {
        var button=new Button{Content=UiText.T(label),Tag=label,Margin=new(4),Padding=new(10,6,10,6)};
        button.Click+=(_,_)=>{try{action();}catch(Exception ex)when(ex is IOException or InvalidOperationException or ArgumentException or FormatException){_status.Text=UiText.T(ex.Message);}};panel.Children.Add(button);
    }
    private async void Reload()
    {
        _cancel?.Cancel();_cancel?.Dispose();_cancel=new();var token=_cancel.Token;var state=State;var anchor=_anchor;
        _preview.Source=null;_identity.Text=state?.Key??anchor?.Key??"";_status.Text=UiText.T("加载图片…");
        try
        {
            var bitmap=state?.PngBase64 is {} data?EmblemEditorWindow.Decode(PngAssets.Decode(data)):anchor is null?null:await LocalGameImages.LoadAsync(_root,anchor.Source,token);
            if(token.IsCancellationRequested)return;_preview.Source=bitmap;_status.Text=UiText.T(bitmap is null?"暂无图片":"选择或编辑仅预览，保存草稿后仍需应用。");
        }
        catch(OperationCanceledException){}
        catch(Exception ex){if(!token.IsCancellationRequested)_status.Text=UiText.T("无法读取图片")+": "+ex.Message;}
    }
    private void Edit(bool current)
    {
        if(_anchor is null)throw new InvalidOperationException("请先选择当前Mod中的单位图片作为资源模板");
        byte[]? source=null;
        if(current){if(_preview.Source is not System.Windows.Media.Imaging.BitmapSource image)throw new InvalidOperationException("当前预览没有可编辑图片");source=EmblemEditorWindow.Encode(image);}
        var editor=new EmblemEditorWindow(source,current?State?.Recipe??"":"",unitPicture:true){Owner=this};
        if(editor.ShowDialog()!=true || editor.ResultPng is null)return;
        State=UnitPictures.Import(_anchor,editor.ResultPng,editor.Recipe);Reload();
    }
}
