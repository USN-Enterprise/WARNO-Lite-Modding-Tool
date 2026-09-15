using System.Windows;
using System.Windows.Controls;
using WarnoLiteModdingTool.Core.Images;
namespace WarnoLiteModdingTool.App.Controls;
public sealed class DivisionEmblem : Image
{
    public static readonly DependencyProperty RootProperty = DependencyProperty.Register(nameof(Root), typeof(string), typeof(DivisionEmblem), new PropertyMetadata("", Changed));
    public static readonly DependencyProperty TextureKeyProperty = DependencyProperty.Register(nameof(TextureKey), typeof(string), typeof(DivisionEmblem), new PropertyMetadata("", Changed));
    public string Root { get => (string)GetValue(RootProperty); set => SetValue(RootProperty,value); }
    public string TextureKey { get => (string)GetValue(TextureKeyProperty); set => SetValue(TextureKeyProperty,value); }
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,Task<IReadOnlyList<TextureChoice>>> Textures=new(StringComparer.OrdinalIgnoreCase);
    public static void Reset(string root)=>Textures.TryRemove(root,out _);
    private CancellationTokenSource? _cancellation;
    private static void Changed(DependencyObject o, DependencyPropertyChangedEventArgs e) => ((DivisionEmblem)o).Refresh();
    public DivisionEmblem() { Stretch=System.Windows.Media.Stretch.Uniform; Loaded+=(_,_)=>Refresh(); Unloaded+=(_,_)=>_cancellation?.Cancel(); }
    private async void Refresh()
    {
        _cancellation?.Cancel(); _cancellation?.Dispose(); _cancellation=new(); var token=_cancellation.Token;
        Source=null; if(!IsLoaded || Root.Length==0 || TextureKey.Length==0)return;
        var root=Root;var key=TextureKey;
        try { var texture=(await Textures.GetOrAdd(root,r=>Task.Run(()=>ModTextures.Read(r,true)))).FirstOrDefault(t=>t.Key==key);
            if(texture is null)return;var image=await LocalGameImages.LoadAsync(root,texture.Source,token);if(!token.IsCancellationRequested)Source=image;
        } catch(OperationCanceledException) { } catch(Exception ex) when(ex is System.IO.IOException or ArgumentException or InvalidOperationException or NotSupportedException or System.IO.InvalidDataException or IndexOutOfRangeException or KeyNotFoundException or OverflowException or ZstdSharp.ZstdException) { if(!token.IsCancellationRequested)ToolTip=Localisation.UiText.T("暂无图片"); }
    }
}
