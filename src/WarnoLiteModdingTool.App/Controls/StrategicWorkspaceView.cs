using MessageBox = WarnoLiteModdingTool.App.Localisation.LocalizedMessageBox;
using WarnoLiteModdingTool.App.Localisation;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WarnoLiteModdingTool.App.ViewModels;
using WarnoLiteModdingTool.Core.Strategic;

namespace WarnoLiteModdingTool.App.Controls;

public sealed class StrategicWorkspaceView : UserControl
{
    private readonly TreeView _tree = new();
    private readonly StackPanel _editor = new() { Margin = new Thickness(12) };
    private readonly StackPanel _pawn = new() { Margin = new Thickness(12) };
    private readonly Grid _body = new();
    private StrategicWorkspaceViewModel? _vm;
    private StrategicNode? _node;
    public StrategicWorkspaceView()
    {
        Loaded += (_,_) => UiText.Current.PropertyChanged += LanguageChanged;
        Unloaded += (_,_) => UiText.Current.PropertyChanged -= LanguageChanged;
        var root = new DockPanel(); Content = root;
        _tree.SetResourceReference(BackgroundProperty, "ControlBrush");
        _tree.SetResourceReference(ForegroundProperty, "TextBrush");
        _tree.SetResourceReference(BorderBrushProperty, "BorderBrush");
        if (Application.Current is { } app)
        {
            _tree.Resources[SystemColors.HighlightBrushKey] = app.FindResource("SelectionBrush");
            _tree.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = app.FindResource("SelectionBrush");
            _tree.Resources[SystemColors.HighlightTextBrushKey] = app.FindResource("TextBrush");
            _tree.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = app.FindResource("TextBrush");
        }
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10) };
        error.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        error.SetBinding(TextBlock.TextProperty, new Binding("Error")); DockPanel.SetDock(error, Dock.Bottom); root.Children.Add(error);
        _body.ColumnDefinitions.Add(new() { Width = new GridLength(340), MinWidth = 250 });
        _body.ColumnDefinitions.Add(new() { Width = new GridLength(5) });
        _body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = 250 });
        _body.ColumnDefinitions.Add(new() { Width = new GridLength(5) });
        _body.ColumnDefinitions.Add(new() { Width = new GridLength(310), MinWidth = 245 });
        root.Children.Add(_body);
        var left = new DockPanel { Margin = new Thickness(10) };
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 8), ToolTip = "搜索战略营" };
        search.SetBinding(TextBox.TextProperty, new Binding("Search") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        DockPanel.SetDock(search, Dock.Top); left.Children.Add(search);
        var filter=new FacetFilter { UseTags=true };filter.SetBinding(FacetFilter.RowsProperty,new Binding("FilterRows"));filter.FilterChanged+=(_,_)=>{if(DataContext is StrategicWorkspaceViewModel vm){vm.ListFilter=filter.Matches;vm.RefreshList();}};DockPanel.SetDock(filter,Dock.Top);left.Children.Add(filter);DockPanel.SetDock(filter.SelectionSummary,Dock.Top);left.Children.Add(filter.SelectionSummary);
        var list = new DataGrid { AutoGenerateColumns=false, IsReadOnly=true, SelectionMode=DataGridSelectionMode.Single, EnableRowVirtualization=true, CanUserAddRows=false, HeadersVisibility=DataGridHeadersVisibility.Column, GridLinesVisibility=DataGridGridLinesVisibility.Horizontal, SelectionUnit=DataGridSelectionUnit.FullRow };
        var recordTemplate=new DataTemplate();var labelFactory=new FrameworkElementFactory(typeof(TextBlock));
        labelFactory.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryGridText");
        var recordBinding=new MultiBinding { Converter=new StrategicRecordNameConverter() };recordBinding.Bindings.Add(new Binding());recordBinding.Bindings.Add(new Binding(nameof(UiText.Version)){Source=UiText.Current});labelFactory.SetBinding(TextBlock.TextProperty,recordBinding);labelFactory.SetBinding(ToolTipProperty,recordBinding);recordTemplate.VisualTree=labelFactory;list.Columns.Add(new DataGridTemplateColumn { Header="名称",CellTemplate=recordTemplate,Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=95 });
        var countryBinding=new MultiBinding {Converter=new GameConverter("country",false)};countryBinding.Bindings.Add(new Binding("Country"));countryBinding.Bindings.Add(new Binding(nameof(UiText.Version)){Source=UiText.Current});list.Columns.Add(new DataGridTextColumn {Header="国家",Binding=countryBinding,Width=85});
        var typeBinding = new MultiBinding { Converter = new GameConverter("battalion", false) }; typeBinding.Bindings.Add(new Binding("BattalionType")); typeBinding.Bindings.Add(new Binding(nameof(UiText.Version)) { Source = UiText.Current });
        list.Columns.Add(new DataGridTextColumn { Header = "类型", Binding = typeBinding, Width = 110 });
        foreach (var column in list.Columns) UiText.Bind(column, DataGridColumn.HeaderProperty, (string)column.Header);
        list.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("VisibleRecords"));
        list.SetBinding(DataGrid.SelectedItemProperty, new Binding("Selected") { Mode = BindingMode.TwoWay });
        VirtualizingPanel.SetIsVirtualizing(list, true); VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        left.Children.Add(list); _body.Children.Add(left);
        var center = new DockPanel { Margin = new Thickness(10) }; Grid.SetColumn(center, 2); _body.Children.Add(center);
        var toolbar = new WrapPanel(); DockPanel.SetDock(toolbar, Dock.Top); center.Children.Add(toolbar);
        Button(toolbar, "Pack 与索引", () => { if (_vm?.CanEditRoster == true) new StrategicMappingWindow(_vm) { Owner = Window.GetWindow(this) }.ShowDialog(); });
        Button(toolbar, "新增连", () => _vm?.Add(null));
        Button(toolbar, "新增下级", () => _vm?.Add(_node));
        Button(toolbar, "上移", () => { if (_node is not null) _vm?.Move(_node, -1); });
        Button(toolbar, "下移", () => { if (_node is not null) _vm?.Move(_node, 1); });
        Button(toolbar, "删除", () =>
        {
            if (_node is null || _vm is null) return;
            if (MessageBox.Show(Window.GetWindow(this), $"删除 {_node.Display} 及其下级编制？", "删除编组", MessageBoxButton.YesNo) == MessageBoxResult.Yes) _vm.Delete(_node);
        });
        Button(toolbar, "撤销本营", async () => { if (_vm is not null) await _vm.UndoAsync(); });
        var template = new HierarchicalDataTemplate(typeof(StrategicNode)) { ItemsSource = new Binding("Children") };
        var factory = new FrameworkElementFactory(typeof(TextBlock)); factory.SetBinding(TextBlock.TextProperty, new Binding("Display"));
        template.VisualTree = factory; _tree.ItemTemplate = template;
        VirtualizingPanel.SetIsVirtualizing(_tree, true); VirtualizingPanel.SetVirtualizationMode(_tree, VirtualizationMode.Recycling);
        _tree.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Roots"));
        _tree.SelectedItemChanged += (_, args) => { _node = args.NewValue as StrategicNode; ShowNode(); };
        var rootStyle = new Style(typeof(TreeViewItem)); var rootTrigger = new DataTrigger { Binding = new Binding("Kind"), Value = "root" }; rootTrigger.Setters.Add(new Setter(TreeViewItem.IsExpandedProperty,true)); rootStyle.Triggers.Add(rootTrigger); _tree.ItemContainerStyle=rootStyle;
        center.Children.Add(_tree);
        var tabs = new TabControl(); Grid.SetColumn(tabs, 4); _body.Children.Add(tabs);
        var formationTab = new TabItem { Content = new ScrollViewer { Content = _editor, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        var pawnTab = new TabItem { Content = new ScrollViewer { Content = _pawn, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        UiText.Bind(formationTab, HeaderedContentControl.HeaderProperty, "编制属性"); UiText.Bind(pawnTab, HeaderedContentControl.HeaderProperty, "棋子属性");
        tabs.Items.Add(formationTab); tabs.Items.Add(pawnTab);
        foreach (var column in new[] { 1, 3 }) { var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch }; splitter.SetResourceReference(BackgroundProperty, "BorderBrush"); Grid.SetColumn(splitter, column); _body.Children.Add(splitter); }
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.SelectionRestored -= ShowPawn;
            _vm = DataContext as StrategicWorkspaceViewModel;
            if (_vm is not null) _vm.SelectionRestored += ShowPawn;
            ShowPawn();
        };
    }
    private void LanguageChanged(object? sender, PropertyChangedEventArgs e) { if(e.PropertyName == nameof(UiText.Version)) { _vm?.RefreshLabels(); ShowNode(); } }
    private static void Button(Panel panel, string label, Action action)
    {
        var button = new Button { Content = label, Margin = new Thickness(2), Padding = new Thickness(8, 5, 8, 5) };
        UiText.Bind(button, ContentControl.ContentProperty, label); button.Click += (_, _) => action(); panel.Children.Add(button);
    }
    private void ShowNode()
    {
        _editor.Children.Clear();
        if (_node is null || _vm is null) return;
        if (_node.Kind == "root") { Label(_editor, _vm.PawnName); Label(_editor, $"{_vm.Companies.Count} 连 / {_vm.AllNodes.Where(n=>n.Kind=="unit").Sum(n=>n.Count)} 单位"); ShowPawn(); return; }
        _editor.DataContext = _node;
        _editor.SetBinding(IsEnabledProperty, new Binding("CanEditRoster") { Source = _vm });
        if (_node.Kind != "unit")
        {
            Label(_editor, "名称"); _editor.Children.Add(new ParameterNote { Text = _node.Kind == "company" ? "Name → COMPANIES.csv.REFTEXT" : "Name → PLATOONS.csv.REFTEXT" });
            var name = new TextBox(); name.SetBinding(TextBox.TextProperty, new Binding("EditableName") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); _editor.Children.Add(name);
            var hq = new CheckBox { Margin = new Thickness(0, 10, 0, 8) }; UiText.Bind(hq, ContentControl.ContentProperty, "HQ 指挥编组"); hq.SetBinding(CheckBox.IsCheckedProperty, new Binding("IsHQ")); _editor.Children.Add(hq); _editor.Children.Add(new ParameterNote { Text = "IsHQ" });
        }
        else
        {
            Label(_editor, "单位"); _editor.Children.Add(new ParameterNote { Text = "DeckPackDescriptor.Unit" });
            UnitPicker(_editor, false);
            Label(_editor, "运输"); _editor.Children.Add(new ParameterNote { Text = "DeckPackDescriptor.Transport" }); UnitPicker(_editor, true);
            Button(_editor, "取消运输", () => { if (_node is not null) _node.Transport = ""; ShowNode(); });
            Label(_editor, "数量"); _editor.Children.Add(new ParameterNote { Text = "PackIndexUnitNumberList · " + UiText.T("元组数量") }); var number = new TextBox(); number.SetBinding(TextBox.TextProperty, new Binding("Count") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged, ValidatesOnExceptions = true }); _editor.Children.Add(number);
            Label(_editor, "老练度"); _editor.Children.Add(new ParameterNote { Text = "DeckPackDescriptor.Xp" }); var xp = new ComboBox { ItemsSource = new[] { 0, 1, 2, 3 } }; xp.SetBinding(ComboBox.SelectedItemProperty, new Binding("Xp")); _editor.Children.Add(xp);
        }
        if (_node.Kind != "company")
        {
            Label(_editor, "移动到"); _editor.Children.Add(new ParameterNote { Text = _node.Kind == "unit" ? "PackIndexUnitNumberList" : "SmartGroupList" });
            var target = new SearchPicker { ItemsSource = _vm.AllNodes.Where(n => n.Kind == (_node.Kind == "unit" ? "group" : "company")).ToArray(), DisplayMemberPath = "Display" };
            _editor.Children.Add(target);
            Button(_editor, "移动", () => { if (_node is not null && target.SelectedItem is StrategicNode parent) _vm.MoveTo(_node, parent); });
        }
    }
    private void UnitPicker(Panel panel, bool transport)
    {
        if (_vm is null || _node is null) return;
        var selectedNode = _node;
        var items = _vm.Data.Units.Units.Where(u => !transport || u.HasUniqueTransporterModule || u.Name == selectedNode.Transport).ToArray();
        var picker = new SearchPicker { ItemsSource = items, DisplayMemberPath = "DisplayName", SecondaryMemberPath = "Name", SelectedItem = items.FirstOrDefault(u => u.Name == (transport ? selectedNode.Transport : selectedNode.Unit)) };
        picker.SelectedItemChanged += (_, _) =>
        {
            if (picker.SelectedItem is WarnoLiteModdingTool.Core.Units.UnitRecord unit)
            { if (transport) selectedNode.Transport = unit.Name; else selectedNode.Unit = unit.Name; }
        };
        panel.Children.Add(picker);
    }
    private void ShowPawn()
    {
        _node = null; _editor.Children.Clear(); _pawn.Children.Clear();
        if (_vm?.Selected is not { } record) return;
        _pawn.SetBinding(IsEnabledProperty, new Binding("CanEdit") { Source = _vm });
        if (record.Pawn is null) { Label(_pawn, "目标没有关联战略棋子"); return; }
        Label(_pawn, "棋子名称"); _pawn.Children.Add(new ParameterNote { Text = "NameToken → UNITS.csv.REFTEXT" });
        var name = new TextBox(); name.SetBinding(TextBox.TextProperty, new Binding("PawnName") { Source = _vm, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); _pawn.Children.Add(name);
        foreach (var field in StrategicFields.All.Where(f => record.Baseline.PawnValues.ContainsKey(f.Key)))
        {
            Label(_pawn, field.Label); _pawn.Children.Add(new ParameterNote { Text = field.Key == "Movement" ? "ModulesDescriptors → StrategicMovementDescriptor_*" : field.Constructor + "." + field.Field });
            var vm = _vm;
            if (field.Kind is "bool" or "choice")
            {
                var choices = field.Kind == "bool" ? new[] { "True", "False" } : vm.Data.Choices[field.Key].Order().ToArray();
                var choice = new SearchPicker { ItemsSource = choices, SelectedItem = vm.PawnValue(field.Key) };
                choice.SelectedItemChanged += (_, _) => { if (choice.SelectedItem is string value) vm.SetPawnValue(field.Key, value); };
                _pawn.Children.Add(choice);
            }
            else
            {
                var value = new TextBox { Text = vm.PawnValue(field.Key) };
                value.TextChanged += (_, _) => vm.SetPawnValue(field.Key, value.Text); _pawn.Children.Add(value);
            }
        }
    }
    private static void Label(Panel panel, string text) => AddLabel(panel, text);
    private static void AddLabel(Panel panel, string text) { var label = new TextBlock { Margin = new Thickness(0, 12, 0, 5), TextWrapping = TextWrapping.Wrap }; UiText.Bind(label, TextBlock.TextProperty, text); panel.Children.Add(label); }
}

public sealed class StrategicRecordNameConverter : IMultiValueConverter
{
    public object Convert(object[] values,Type targetType,object parameter,System.Globalization.CultureInfo culture)
    {
        if(values[0] is not StrategicRecord record)return "";
        if(record.HasCustomName)return record.Baseline.Name;
        return WarnoLiteModdingTool.Core.Localisation.VanillaNames.Lookup("UNITS",record.Baseline.Name,UiText.Current.English?"US":"SC") ?? record.DisplayName;
    }
    public object[] ConvertBack(object value,Type[] targetTypes,object parameter,System.Globalization.CultureInfo culture)=>throw new NotSupportedException();
}
