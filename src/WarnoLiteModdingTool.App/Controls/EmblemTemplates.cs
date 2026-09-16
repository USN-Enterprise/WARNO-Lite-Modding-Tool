using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WarnoLiteModdingTool.App.Localisation;
namespace WarnoLiteModdingTool.App.Controls;
public static class EmblemTemplates
{
    public static string[] Names => new[]{"苏联近卫红旗","东德圆徽","苏联空降盾徽","苏联黑底盾徽","波兰三角","波兰圆形","波兰方形","波兰菱形","捷克菱形标记","捷克尖角标记"}.Select(UiText.T).ToArray();
    private static Brush Color(string value)=>new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(value));
    private static BitmapImage Asset(string name){var b=new BitmapImage(new Uri("pack://application:,,,/WarnoLiteModdingTool;component/Assets/Emblems/"+name+".png"));b.Freeze();return b;}
    public static BitmapSource Render(int template,string number,int palette)
    {
        if(template<0||template>=10||!Regex.IsMatch(number,@"^[0-9]{0,3}$"))throw new InvalidOperationException(UiText.T("番号只允许最多3位数字"));
        var v=new DrawingVisual();using(var dc=v.RenderOpen())
        {
            var gold=Color("#F5AE0C");var dark=Color("#470D04");
            Rect digits=new(108,358,296,148);Brush fill=dark;Pen? outline=new(gold,12){LineJoin=PenLineJoin.Round};
            if(template<=3){dc.DrawImage(Asset(new[]{"guards","ddr","airborne","black"}[template]),new Rect(0,0,512,512));}
            else if(template<=7)
            {
                fill=Brushes.White;outline=new Pen(Color("#625F4B"),3);var border=new Pen(Brushes.White,13);var under=new Pen(Color("#625F4B"),20);
                Geometry shape=template switch {4=>Geometry.Parse("M 45,100 L 467,100 256,440 Z"),5=>new EllipseGeometry(new Point(256,256),178,178),6=>new RectangleGeometry(new Rect(80,80,352,352)),_=>Geometry.Parse("M 256,30 L 430,256 256,482 82,256 Z")};
                var gaps=new GeometryGroup();
                foreach(var r in template switch {4=>new[]{new Rect(245,80,22,40),new Rect(130,244,35,34),new Rect(347,244,35,34)},5=>new[]{new Rect(245,65,22,30),new Rect(65,245,30,22),new Rect(417,245,30,22),new Rect(245,417,22,30)},6=>new[]{new Rect(246,62,20,36),new Rect(62,246,36,20),new Rect(414,246,36,20),new Rect(246,414,20,36)},_=>new[]{new Rect(151,132,29,28),new Rect(332,132,29,28),new Rect(151,352,29,28),new Rect(332,352,29,28)}})gaps.Children.Add(new RectangleGeometry(r));
                dc.PushClip(Geometry.Combine(new RectangleGeometry(new Rect(0,0,512,512)),gaps,GeometryCombineMode.Exclude,null));dc.DrawGeometry(null,under,shape);dc.DrawGeometry(null,border,shape);dc.Pop();
                // Reference insignia have small breaks, kept transparent using exclusions.
                digits=template==4?new Rect(155,172,202,124):template==7?new Rect(166,194,180,124):new Rect(116,184,280,144);
            }
            else
            {
                dc.DrawImage(Asset("czech"),new Rect(0,0,512,512));var colors=new[]{"#002653","#AC1314","#F5AE0C","#524D37"};palette=Math.Clamp(palette,0,3);fill=palette==2?Brushes.Black:Brushes.White;outline=null;
                var shield=Geometry.Parse("M 182,200 L 335,200 335,300 Q 335,365 257,382 Q 182,365 182,300 Z");dc.DrawGeometry(Color(colors[palette]),new Pen(gold,7),shield);dc.DrawGeometry(null,new Pen(fill,3),shield);
                var symbol=template==8?Geometry.Parse("M 211,341 L 258,318 306,341 258,364 Z"):Geometry.Parse("M 297,320 L 233,320 212,341 233,362 297,362 Z");dc.DrawGeometry(null,new Pen(fill,5),symbol);digits=new Rect(190,216,136,75);
            }
            if(number.Length>0)
            {
                var text=new FormattedText(number,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Arial Black"),160,fill,1);var geometry=text.BuildGeometry(new Point(0,0));var bounds=geometry.Bounds;
                var scale=Math.Min(digits.Width/bounds.Width,digits.Height/bounds.Height);var transform=new TransformGroup();transform.Children.Add(new TranslateTransform(-bounds.X,-bounds.Y));transform.Children.Add(new ScaleTransform(scale,scale));transform.Children.Add(new TranslateTransform(digits.X+(digits.Width-bounds.Width*scale)/2,digits.Y+(digits.Height-bounds.Height*scale)/2));geometry.Transform=transform;dc.DrawGeometry(fill,outline,geometry);
            }
        }
        var result=new RenderTargetBitmap(512,512,96,96,PixelFormats.Pbgra32);result.Render(v);result.Freeze();return result;
    }
}
