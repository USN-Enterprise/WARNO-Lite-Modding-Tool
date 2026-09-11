using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.Core.Images;
using WarnoLiteModdingTool.Core.Units;
namespace WarnoLiteModdingTool.App.Controls;
public sealed class UnitPortrait : Button
{
    public static readonly DependencyProperty UnitProperty=DependencyProperty.Register(nameof(Unit),typeof(UnitRecord),typeof(UnitPortrait),new PropertyMetadata(null,Changed));
    public static readonly DependencyProperty RootProperty=DependencyProperty.Register(nameof(Root),typeof(string),typeof(UnitPortrait),new PropertyMetadata(null,Changed));
    public UnitRecord? Unit {get=>(UnitRecord?)GetValue(UnitProperty);set=>SetValue(UnitProperty,value);}
    public string? Root {get=>(string?)GetValue(RootProperty);set=>SetValue(RootProperty,value);}
    private CancellationTokenSource? _cancel;private BitmapSource? _bitmap;private readonly Popup _popup=new(){StaysOpen=false,AllowsTransparency=true,Placement=PlacementMode.Bottom};
    private string _status="查看图片";
    public UnitPortrait(){SetResourceReference(BackgroundProperty,"ControlBrush");SetResourceReference(ForegroundProperty,"TextBrush");SetResourceReference(BorderBrushProperty,"BorderBrush");HorizontalAlignment=HorizontalAlignment.Right;VerticalAlignment=VerticalAlignment.Center;Margin=new Thickness(8,0,0,0);Padding=new Thickness(3);Loaded+=(_,_)=>{if(Parent is FrameworkElement parent)parent.SizeChanged+=ParentSizeChanged;Reload();};Unloaded+=(_,_)=>{if(Parent is FrameworkElement parent)parent.SizeChanged-=ParentSizeChanged;_cancel?.Cancel();_popup.IsOpen=false;};Click+=(_,_)=>Open();}
    private static void Changed(DependencyObject d,DependencyPropertyChangedEventArgs e){if(((UnitPortrait)d).IsLoaded)((UnitPortrait)d).Reload();}
    private void ParentSizeChanged(object sender,SizeChangedEventArgs e)=>Refresh();
    private void Refresh(){var wide=(Parent as FrameworkElement)?.ActualWidth>=500&&Unit?.DisplayName.Length<40;Content=wide&&_bitmap is not null?new Image{Source=_bitmap,Width=88,Height=50,Stretch=Stretch.Uniform}:new TextBlock{Text=UiText.T(_status)};ToolTip=UiText.T(_status);IsEnabled=Unit is not null;}
    private async void Reload()
    {
        _cancel?.Cancel();_cancel?.Dispose();_cancel=new();var token=_cancel.Token;_bitmap=null;_popup.IsOpen=false;_status="加载图片…";Refresh();
        try{var unit=Unit;var root=Root;if(unit is null||root is null)return;var bitmap=await Task.Run(async()=>{var key=ModTextures.UnitKey(unit);if(key is null)return null;var source=ModTextures.Read(root).SingleOrDefault(t=>t.Key==key)?.Source;return source is null?null:await LocalGameImages.LoadAsync(root,source,token);},token);if(token.IsCancellationRequested)return;_bitmap=bitmap;_status=bitmap is null?"暂无图片":"查看图片";}
        catch(OperationCanceledException){return;}
        catch(Exception ex)when(ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or OverflowException or KeyNotFoundException or IndexOutOfRangeException or ZstdSharp.ZstdException){if(token.IsCancellationRequested)return;_status="暂无图片";}
        finally{if(!token.IsCancellationRequested)Refresh();}
    }
    private void Open(){if(_bitmap is null)return;var panel=new StackPanel();var close=new Button{Content=UiText.T("关闭"),HorizontalAlignment=HorizontalAlignment.Right};close.Click+=(_,_)=>_popup.IsOpen=false;panel.Children.Add(close);panel.Children.Add(new TextBlock{Text=Unit?.DisplayName,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,8)});panel.Children.Add(new Image{Source=_bitmap,Width=360,MaxHeight=300,Stretch=Stretch.Uniform});var border=new Border{Child=panel,Padding=new Thickness(12),CornerRadius=new CornerRadius(8),Focusable=true};border.SetResourceReference(Border.BackgroundProperty,"SurfaceBrush");border.SetResourceReference(TextElement.ForegroundProperty,"TextBrush");border.PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){_popup.IsOpen=false;e.Handled=true;}};_popup.Child=border;_popup.PlacementTarget=this;_popup.IsOpen=true;border.Focus();}
}
