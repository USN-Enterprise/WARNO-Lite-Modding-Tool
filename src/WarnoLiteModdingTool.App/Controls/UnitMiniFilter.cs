using System.Collections;
using System.Windows;
using System.Windows.Controls;
using WarnoLiteModdingTool.App.Localisation;
namespace WarnoLiteModdingTool.App.Controls;

public sealed class UnitMiniFilter : Expander
{
    private readonly Dictionary<string, HashSet<string>> _selected = new();
    private readonly List<(ContentControl Control,string Kind,string Raw)> _labels=[];
    public event EventHandler? FilterChanged;
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(UnitMiniFilter), new PropertyMetadata(null,(o,_)=>((UnitMiniFilter)o).Rebuild()));
    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set=>SetValue(ItemsSourceProperty,value); }
    public UnitMiniFilter() { Header=UiText.T("筛选"); Margin=new Thickness(0,5,0,5); Loaded+=(_,_)=>UiText.Current.PropertyChanged+=LanguageChanged; Unloaded+=(_,_)=>UiText.Current.PropertyChanged-=LanguageChanged; }
    private void LanguageChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e) { if(e.PropertyName!=nameof(UiText.Version))return; foreach(var (control,kind,raw) in _labels)control.Content=kind.Length==0?UiText.T(raw):GameText.Display(kind,raw);Header=UiText.T("筛选")+" · "+_selected.Values.Sum(v=>v.Count); }
    private static string Read(object item,string key)
    {
        var p=item.GetType().GetProperty(key); if(p!=null)return p.GetValue(item)?.ToString()??"";
        var unit=item.GetType().GetProperty("Unit")?.GetValue(item);return unit==null?"":Read(unit,key);
    }
    public bool Matches(object item) => _selected.All(p=>p.Value.Count==0 || p.Value.Contains(Read(item,p.Key)));
    private void Rebuild()
    {
        var panel=new StackPanel();Content=new ScrollViewer { Content=panel,MaxHeight=200,VerticalScrollBarVisibility=ScrollBarVisibility.Auto }; _selected.Clear(); _labels.Clear();
        var items=ItemsSource?.Cast<object>().ToArray()??[];
        foreach(var (key,label) in new[]{("Country","国家"),("Coalition","阵营"),("Factory","栏位"),("Role","角色")})
        {
            var values=items.Select(i=>Read(i,key)).Where(s=>s.Length>0).Distinct().Order().ToArray(); if(values.Length==0)continue;
            var selected=new HashSet<string>();_selected[key]=selected;
            var head=new StackPanel { Orientation=Orientation.Horizontal };panel.Children.Add(head);head.Children.Add(new TextBlock { Text=UiText.T(label),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0) });
            var choices=new WrapPanel();var checks=new List<CheckBox>();
            foreach(var (text,all) in new[]{("全选",true),("取消全选",false)}) { var b=new Button { Content=UiText.T(text),Padding=new Thickness(5,2,5,2),Margin=new Thickness(2) };b.Click+=(_,_)=>{foreach(var c in checks)c.IsChecked=all;};head.Children.Add(b);_labels.Add((b,"",text)); }
            foreach(var value in values) { var c=new CheckBox { Content=GameText.Display(key.ToLowerInvariant(),value),Margin=new Thickness(2,4,8,4),Tag=value }; checks.Add(c);_labels.Add((c,key.ToLowerInvariant(),value)); c.Checked+=(_,_)=>{selected.Add(value);Changed();};c.Unchecked+=(_,_)=>{selected.Remove(value);Changed();};choices.Children.Add(c); }
            panel.Children.Add(choices);
        }
        var clear=new Button { Content=UiText.T("清除筛选"),HorizontalAlignment=HorizontalAlignment.Left };clear.Click+=(_,_)=>{Rebuild();Changed();};panel.Children.Add(clear);_labels.Add((clear,"","清除筛选"));
    }
    private void Changed() { Header=UiText.T("筛选")+" · "+_selected.Values.Sum(v=>v.Count);FilterChanged?.Invoke(this,EventArgs.Empty); }
}
