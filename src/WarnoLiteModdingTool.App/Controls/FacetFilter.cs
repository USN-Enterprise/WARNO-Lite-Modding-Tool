using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.Core.Units;
namespace WarnoLiteModdingTool.App.Controls;

public sealed record FilterRow(string Id, IReadOnlyDictionary<string,string[]> Values);
public static class FilterRows
{
    public static FilterRow Unit(UnitRecord u) => new(u.Name,new Dictionary<string,string[]> {
        ["国家"]=[u.Country], ["阵营"]=[u.Coalition], ["栏位"]=[u.Factory], ["角色"]=[u.Role], ["所属师"]=u.Divisions.ToArray() });
}
public sealed class FacetFilter : Expander
{
    public static readonly DependencyProperty RowsProperty=DependencyProperty.Register(nameof(Rows),typeof(IEnumerable<FilterRow>),typeof(FacetFilter),new PropertyMetadata(null,(o,_)=>((FacetFilter)o).Rebuild()));
    public IEnumerable<FilterRow>? Rows {get=>(IEnumerable<FilterRow>?)GetValue(RowsProperty);set=>SetValue(RowsProperty,value);}
    private Dictionary<string,FilterRow[]> _rows=[];
    private readonly Dictionary<string,HashSet<string>> _selected=[];
    public bool UseTags { get; set; }
    public WrapPanel SelectionSummary { get; } = new();
    private bool _batch;
    private int _generation;
    public event EventHandler? FilterChanged;
    public FacetFilter(){UiText.Bind(this,HeaderProperty,"筛选");Margin=new Thickness(0,4,0,4);Loaded+=(_,_)=>UiText.Current.PropertyChanged+=LanguageChanged;Unloaded+=(_,_)=>UiText.Current.PropertyChanged-=LanguageChanged;}
    private void LanguageChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e){if(e.PropertyName==nameof(UiText.Version))Rebuild();}
    public bool Matches(string id) => _selected.All(p=>p.Value.Count==0) || _rows.TryGetValue(id,out var variants) && variants.Any(row=>_selected.All(p=>p.Value.Count==0 || row.Values.TryGetValue(p.Key,out var values)&&values.Any(p.Value.Contains)));
    private async void Rebuild()
    {
        var generation=++_generation;var rows=Rows?.ToArray()??[];
        var previous=_selected.ToDictionary(p=>p.Key,p=>p.Value.ToHashSet());Content=null;
        var cache=await Task.Run(()=> (Rows:rows.GroupBy(r=>r.Id).ToDictionary(g=>g.Key,g=>g.ToArray()),Options:rows.SelectMany(r=>r.Values).GroupBy(p=>p.Key).ToDictionary(g=>g.Key,g=>g.SelectMany(p=>p.Value).Where(v=>v.Length>0).Distinct().Order().ToArray())));
        if(generation!=_generation)return;_rows=cache.Rows;_selected.Clear();
        var panel=new StackPanel();Content=new ScrollViewer {Content=panel,MaxHeight=250,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalAlignment=VerticalAlignment.Top};
        foreach(var (key,values) in cache.Options)
        {
            var section=new Expander { IsExpanded=UseTags && key is not ("所属师" or "战斗角色") };UiText.Bind(section,HeaderProperty,key);panel.Children.Add(section);
            var body=new DockPanel();section.Content=body;var actions=new StackPanel {Orientation=Orientation.Horizontal};DockPanel.SetDock(actions,Dock.Top);body.Children.Add(actions);
            var chosen=previous.GetValueOrDefault(key)??new HashSet<string>();_selected[key]=chosen;
            FacetOption[] options=[];options=(key=="草稿"?new[]{"有草稿","无草稿"}:values).Select(v=>new FacetOption(key,v,()=>Changed())).ToArray();
            _batch=true;foreach(var option in options)option.Selected=chosen.Contains(option.Value);_batch=false;
            var list=new ListBox {ItemsSource=options,MaxHeight=160};VirtualizingPanel.SetIsVirtualizing(list,true);VirtualizingPanel.SetVirtualizationMode(list,VirtualizationMode.Recycling);
            var template=new DataTemplate();var check=new FrameworkElementFactory(typeof(CheckBox));check.SetBinding(ContentControl.ContentProperty,new Binding(nameof(FacetOption.Label)));check.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,new Binding(nameof(FacetOption.Selected)){Mode=BindingMode.TwoWay});if(UseTags)check.SetResourceReference(FrameworkElement.StyleProperty,"FilterPillCheckBox");template.VisualTree=check;list.ItemTemplate=template;body.Children.Add(list);
            if(UseTags && options.Length<=20){var wrap=new FrameworkElementFactory(typeof(WrapPanel));list.ItemsPanel=new ItemsPanelTemplate(wrap);}
            if(UseTags){var search=new TextBox {Margin=new Thickness(0,3,0,3)};DockPanel.SetDock(search,Dock.Top);body.Children.Insert(1,search);search.TextChanged+=(_,_)=>{var view=CollectionViewSource.GetDefaultView(list.ItemsSource);view.Filter=o=>o is FacetOption f && (f.Label.Contains(search.Text,StringComparison.OrdinalIgnoreCase)||f.Value.Contains(search.Text,StringComparison.OrdinalIgnoreCase));};}
            foreach(var (label,all) in new[]{("全选",true),("取消全选",false)}){var b=new Button {Padding=new Thickness(6,3,6,3)};UiText.Bind(b,ContentControl.ContentProperty,label);b.Click+=(_,_)=>{_batch=true;foreach(var o in options.Where(o=>!all||CollectionViewSource.GetDefaultView(list.ItemsSource).Contains(o)))o.Selected=all;_batch=false;Changed();};actions.Children.Add(b);}
            void Changed(){if(_batch)return;chosen.Clear();foreach(var o in options.Where(o=>o.Selected))chosen.Add(o.Value);if(UseTags){section.Header=UiText.T(key)+$" ({chosen.Count})";RefreshSummary();}FilterChanged?.Invoke(this,EventArgs.Empty);}
            if(UseTags)section.Header=UiText.T(key)+$" ({chosen.Count})";
        }
        RefreshSummary();FilterChanged?.Invoke(this,EventArgs.Empty);
    }
    private void RefreshSummary()
    {
        SelectionSummary.Children.Clear();if(!UseTags)return;
        foreach(var (key,value) in _selected.SelectMany(p=>p.Value.Select(v=>(p.Key,v))).Take(12)){var values=_selected[key];var label=new FacetOption(key,value,()=>{}).Label;var button=new Button {Content=label+" ×",Margin=new Thickness(2),Padding=new Thickness(5,2,5,2)};button.Click+=(_,_)=>{values.Remove(value);Rebuild();FilterChanged?.Invoke(this,EventArgs.Empty);};SelectionSummary.Children.Add(button);}
        if(_selected.Sum(p=>p.Value.Count)>12){var clear=new Button {Content=UiText.T("取消全选")+$" ({_selected.Sum(p=>p.Value.Count)})"};clear.Click+=(_,_)=>{_selected.Clear();Rebuild();FilterChanged?.Invoke(this,EventArgs.Empty);};SelectionSummary.Children.Add(clear);}
    }
    private sealed class FacetOption(string key,string value,Action changed):ViewModels.ObservableObject
    {private bool _selected;public string Value=>value;public string Label=>key switch {"国家"=>GameText.Display("country",value),"角色"=>GameText.Display("role",value),"栏位"=>GameText.Display("factory",value),"所属师"=>GameText.Display("division",value),_=>UiText.T(value)};public bool Selected {get=>_selected;set{if(SetProperty(ref _selected,value))changed();}}}
}
