using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WarnoLiteModdingTool.App.Localisation;
using Image=System.Windows.Controls.Image;
namespace WarnoLiteModdingTool.App.Controls;

public sealed class EmblemEditorWindow : Window
{
    private BitmapSource? _original,_current;
    private readonly Stack<(BitmapSource Image, EditStep[] Steps)> _undo=[];
    private readonly List<EditStep> _steps=[];private bool _templateError;
    private readonly Canvas _canvas=new();private readonly Image _image=new();private readonly System.Windows.Shapes.Rectangle _rect=new(){Stroke=Brushes.DodgerBlue,StrokeThickness=2};private readonly Ellipse _circle=new(){Stroke=Brushes.DodgerBlue,StrokeThickness=2};
    private readonly TextBlock _status=new(){TextWrapping=TextWrapping.Wrap};private readonly ComboBox _tool=new();private Point _start;private Rect _selection;private bool _moving;private Rect _beforeMove;
    private readonly ComboBox _templates=new(){MinWidth=160};private readonly ComboBox _colors=new(){MinWidth=120};private readonly TextBox _number=new(){Text="35",MinWidth=100};private bool _templateMode;
    public byte[]? ResultPng {get;private set;}public string Recipe {get;private set;}="";
    public EmblemEditorWindow(byte[]? existing=null,string recipe="",bool templateMode=false,bool unitPicture=false)
    {
        Title=UiText.T(unitPicture?"单位图片编辑":"师徽图片编辑");Width=1120;Height=800;MinWidth=760;MinHeight=550;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"SurfaceBrush");SetResourceReference(ForegroundProperty,"TextBrush");
        var root=new DockPanel{Margin=new Thickness(14)};root.SetResourceReference(Panel.BackgroundProperty,"SurfaceBrush");Content=root;
        var bottom=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        Button Add(Panel panel,string label,Action action){var b=new Button{Content=UiText.T(label),Margin=new Thickness(4),Padding=new Thickness(10,6,10,6)};b.Click+=(_,_)=>{try{action();}catch(Exception ex)when(ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException){_status.Text=ex.Message;}};panel.Children.Add(b);return b;}
        Add(bottom,"确认图片",()=>{if(_current is null)throw new InvalidOperationException("请先选择图片或模板");if(_templateError)throw new InvalidOperationException("请修正番号输入");ResultPng=Encode(_current);Recipe=BuildRecipe();DialogResult=true;});Add(bottom,"取消",Close);
        var side=new StackPanel{Width=235,Margin=new Thickness(12,0,0,0)};DockPanel.SetDock(side,Dock.Right);root.Children.Add(new ScrollViewer{Content=side,Width=250,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});DockPanel.SetDock(root.Children[root.Children.Count-1],Dock.Right);
        Add(side,"导入自定义图片",()=>{var d=new Microsoft.Win32.OpenFileDialog{Filter="PNG / JPEG|*.png;*.jpg;*.jpeg"};if(d.ShowDialog(this)==true){if(new FileInfo(d.FileName).Length>8_000_000)throw new InvalidDataException("图片文件超过8MB");SetOriginal(Decode(File.ReadAllBytes(d.FileName)));_templateMode=false;Recipe="";}});
        var templateStart=side.Children.Count;
        side.Children.Add(new TextBlock{Text=UiText.T("固定模板"),Margin=new Thickness(4,12,4,4)});
        _templates.ItemsSource=EmblemTemplates.Names;_templates.SelectedIndex=0;side.Children.Add(_templates);
        side.Children.Add(new TextBlock{Text=UiText.T("番号（最多3位，可留空）")});side.Children.Add(_number);
        _colors.ItemsSource=new[]{UiText.T("深蓝"),UiText.T("红色"),UiText.T("金黄"),UiText.T("橄榄灰")};_colors.SelectedIndex=0;side.Children.Add(_colors);_colors.Visibility=Visibility.Collapsed;
        void RenderTemplate(){_colors.Visibility=_templates.SelectedIndex>=8?Visibility.Visible:Visibility.Collapsed;if(!_templateMode)return;try{SetOriginal(EmblemTemplates.Render(_templates.SelectedIndex,_number.Text,_colors.SelectedIndex),false);_templateError=false;_status.Text="";}catch(Exception ex){_templateError=true;_status.Text=ex.Message;}}
        Add(side,"生成模板",()=>{_templateMode=true;RenderTemplate();});_templates.SelectionChanged+=(_,_)=>RenderTemplate();_colors.SelectionChanged+=(_,_)=>RenderTemplate();_number.TextChanged+=(_,_)=>RenderTemplate();
        if(unitPicture)foreach(UIElement element in side.Children.Cast<UIElement>().Skip(templateStart))element.Visibility=Visibility.Collapsed;
        side.Children.Add(new TextBlock{Text=UiText.T("选区工具"),Margin=new Thickness(4,12,4,4)});_tool.ItemsSource=new[]{UiText.T("矩形裁剪"),UiText.T("圆形透明选区")};_tool.SelectedIndex=0;_tool.SelectionChanged+=(_,_)=>{_selection=Rect.Empty;ShowSelection();};side.Children.Add(_tool);
        side.Children.Add(new TextBlock{Text=UiText.T("拖动画选区；拖动内部移动；重新画框调整大小。"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4)});
        Add(side,"应用裁剪",()=>ApplySelection(0));Add(side,"圈外透明",()=>ApplySelection(1));Add(side,"圈内透明",()=>ApplySelection(2));
        Add(side,"撤销",()=>{if(_undo.TryPop(out var prior)){_steps.Clear();_steps.AddRange(prior.Steps);Show(prior.Image);}});Add(side,"重置图片",()=>{if(_original is not null){_undo.Push((_current!,_steps.ToArray()));_steps.Clear();Show(_original);}});
        side.Children.Add(new TextBlock{Text=UiText.T("画布缩放")});var zoom=new Slider{Minimum=.25,Maximum=4,Value=1,TickFrequency=.25};zoom.ValueChanged+=(_,_)=>_canvas.LayoutTransform=new ScaleTransform(zoom.Value,zoom.Value);side.Children.Add(zoom);
        var backgrounds=new ComboBox{ItemsSource=new[]{UiText.T("透明棋盘"),UiText.T("浅色预览"),UiText.T("深色预览")},SelectedIndex=0};backgrounds.SelectionChanged+=(_,_)=>_canvas.Background=backgrounds.SelectedIndex==1?Brushes.WhiteSmoke:backgrounds.SelectedIndex==2?Brushes.DimGray:Checker();side.Children.Add(backgrounds);side.Children.Add(_status);
        _canvas.Background=Checker();_canvas.Children.Add(_image);_canvas.Children.Add(_rect);_canvas.Children.Add(_circle);_rect.IsHitTestVisible=false;_circle.IsHitTestVisible=false;
        root.Children.Add(new ScrollViewer{Content=_canvas,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        _canvas.MouseLeftButtonDown+=(_,e)=>{if(_current is null)return;_start=e.GetPosition(_canvas);_moving=!_selection.IsEmpty&&_selection.Contains(_start);_beforeMove=_selection;_canvas.CaptureMouse();e.Handled=true;};
        _canvas.MouseMove+=(_,e)=>{if(!_canvas.IsMouseCaptured||_current is null)return;var q=e.GetPosition(_canvas);q.X=Math.Clamp(q.X,0,_current.PixelWidth);q.Y=Math.Clamp(q.Y,0,_current.PixelHeight);if(_moving){_selection=new Rect(Math.Clamp(_beforeMove.X+q.X-_start.X,0,_current.PixelWidth-_beforeMove.Width),Math.Clamp(_beforeMove.Y+q.Y-_start.Y,0,_current.PixelHeight-_beforeMove.Height),_beforeMove.Width,_beforeMove.Height);}else{var x=Math.Min(_start.X,q.X);var y=Math.Min(_start.Y,q.Y);var w=Math.Abs(q.X-_start.X);var h=Math.Abs(q.Y-_start.Y);if(_tool.SelectedIndex==1)w=h=Math.Min(w,h);_selection=new Rect(x,y,w,h);}ShowSelection();};
        _canvas.MouseLeftButtonUp+=(_,_)=>_canvas.ReleaseMouseCapture();
        if(existing is not null)SetOriginal(Decode(existing));
        if(!string.IsNullOrEmpty(recipe)){try{var r=JsonSerializer.Deserialize<EditorRecipe>(recipe);if(r?.Version==2&&r.Source is not null){if(!unitPicture && r.Template is { } tr){_templates.SelectedIndex=tr.Template;_number.Text=tr.Number;_colors.SelectedIndex=tr.Color;}_steps.AddRange(r.Steps);SetOriginal(Decode(Convert.FromBase64String(r.Source)),false);_templateMode=!unitPicture && r.Template is not null;Recipe=recipe;}}catch(Exception ex)when(ex is JsonException or FormatException or ArgumentException or IOException){_status.Text=UiText.T("编辑参数无法恢复，保留已保存图片");}}
        if(templateMode && !unitPicture){_templateMode=true;RenderTemplate();}ShowSelection();
    }
    public sealed record TemplateRecipe(int Version,int Template,string Number,int Color);
    public sealed record EditStep(int Mode,double X,double Y,double Width,double Height);
    public sealed record EditorRecipe(int Version,TemplateRecipe? Template,string? Source,EditStep[] Steps);
    private string BuildRecipe()=>JsonSerializer.Serialize(new EditorRecipe(2,_templateMode?new TemplateRecipe(1,_templates.SelectedIndex,_number.Text,_colors.SelectedIndex):null,_original is null?null:Convert.ToBase64String(Encode(_original)),_steps.ToArray()));
    private void SetOriginal(BitmapSource b,bool clear=true){_original=b;_undo.Clear();if(clear){_steps.Clear();_templateError=false;}foreach(var step in _steps)b=Process(b,step);Show(b);}
    private void Show(BitmapSource b){_current=b;_image.Source=b;_image.Width=b.PixelWidth;_image.Height=b.PixelHeight;_canvas.Width=b.PixelWidth;_canvas.Height=b.PixelHeight;_selection=Rect.Empty;ShowSelection();}
    private void ShowSelection(){foreach(var s in new Shape[]{_rect,_circle})s.Visibility=Visibility.Collapsed;if(_selection.IsEmpty)return;Shape shape=_tool.SelectedIndex==0?_rect:_circle;shape.Visibility=Visibility.Visible;shape.Width=_selection.Width;shape.Height=_selection.Height;Canvas.SetLeft(shape,_selection.X);Canvas.SetTop(shape,_selection.Y);}
    private void ApplySelection(int mode)
    {
        if(_current is null||_selection.IsEmpty||_selection.Width<1||_selection.Height<1)throw new InvalidOperationException("请先拖出选区");
        if(mode>0&&_tool.SelectedIndex!=1)throw new InvalidOperationException("请先选择圆形透明选区");
        _undo.Push((_current,_steps.ToArray()));var step=new EditStep(mode,_selection.X/_current.PixelWidth,_selection.Y/_current.PixelHeight,_selection.Width/_current.PixelWidth,_selection.Height/_current.PixelHeight);var result=Process(_current,step);_steps.Add(step);Show(result);
    }
    public static BitmapSource Process(BitmapSource input,EditStep step)
    {
        if(step.Mode is <0 or >2 || new[]{step.X,step.Y,step.Width,step.Height}.Any(v=>!double.IsFinite(v)))throw new InvalidDataException("选区参数无效");
        var selection=new Rect(step.X*input.PixelWidth,step.Y*input.PixelHeight,step.Width*input.PixelWidth,step.Height*input.PixelHeight);
        if(selection.X<0||selection.Y<0||selection.Width<1||selection.Height<1||selection.Right>input.PixelWidth+.001||selection.Bottom>input.PixelHeight+.001)throw new InvalidDataException("选区超出图片");
        if(step.Mode==0)return new CroppedBitmap(input,new Int32Rect((int)selection.X,(int)selection.Y,Math.Max(1,(int)selection.Width),Math.Max(1,(int)selection.Height)));
        var b=new FormatConvertedBitmap(input,PixelFormats.Bgra32,null,0);var data=new byte[b.PixelWidth*b.PixelHeight*4];b.CopyPixels(data,b.PixelWidth*4,0);var cx=selection.X+selection.Width/2;var cy=selection.Y+selection.Height/2;var radius=selection.Width/2;
        for(var y=0;y<b.PixelHeight;y++)for(var x=0;x<b.PixelWidth;x++){var distance=Math.Sqrt(Math.Pow(x+.5-cx,2)+Math.Pow(y+.5-cy,2));var coverage=Math.Clamp(radius+.5-distance,0,1);if(step.Mode==2)coverage=1-coverage;data[(y*b.PixelWidth+x)*4+3]=(byte)Math.Round(data[(y*b.PixelWidth+x)*4+3]*coverage);}
        var image=BitmapSource.Create(b.PixelWidth,b.PixelHeight,96,96,PixelFormats.Bgra32,null,data,b.PixelWidth*4);image.Freeze();return image;
    }

    public static BitmapSource Decode(byte[] bytes){using var stream=new MemoryStream(bytes);var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);var b=decoder.Frames[0];if(b.PixelWidth>4096||b.PixelHeight>4096)throw new InvalidDataException("图片尺寸超过4096像素");b.Freeze();return b;}
    public static byte[] Encode(BitmapSource b){using var stream=new MemoryStream();var encoder=new PngBitmapEncoder();// Do not copy metadata from a frozen frame whose decoder belongs to the loader thread.
        encoder.Frames.Add(BitmapFrame.Create(b,null,null,null));encoder.Save(stream);return stream.ToArray();}
    private static Brush Checker(){var group=new DrawingGroup();group.Children.Add(new GeometryDrawing(Brushes.LightGray,null,new RectangleGeometry(new Rect(0,0,20,20))));group.Children.Add(new GeometryDrawing(Brushes.WhiteSmoke,null,new RectangleGeometry(new Rect(0,0,10,10))));group.Children.Add(new GeometryDrawing(Brushes.WhiteSmoke,null,new RectangleGeometry(new Rect(10,10,10,10))));return new DrawingBrush(group){Viewport=new Rect(0,0,20,20),ViewportUnits=BrushMappingMode.Absolute,TileMode=TileMode.Tile,Stretch=Stretch.None};}
}
