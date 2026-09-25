using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.ViewModels;
using WarnoLiteModdingTool.Core.Rules;

namespace WarnoLiteModdingTool.App.Controls;

public static class TerrainRulesView
{
    public static UIElement Build(RulesWorkspaceViewModel vm, Dictionary<string, bool> expanded)
    {
        var panel = new StackPanel { Margin = new Thickness(12, 8, 0, 0) };
        panel.Children.Add(Text("修改当前Mod的共享地形规则，影响双方符合条件的单位。仅编辑已有条目。"));
        var visible = 0;
        foreach (var terrain in vm.Terrains)
        {
            var advanced = Advanced.EditorMode.IsAdvanced;
            var fields = terrain.Fields.Where(f => advanced || f.Cell.Basic).ToArray();
            if (!advanced && fields.Length == 0) continue;
            bool Match(string text) => text.Contains(vm.Search, StringComparison.OrdinalIgnoreCase) || UiText.T(text).Contains(vm.Search, StringComparison.OrdinalIgnoreCase);
            if (vm.Search.Length > 0 && !Match("地形规则") && !Match(terrain.Terrain.Name) && !Match(TerrainWorkspace.Label(terrain.Terrain.Name)) && !fields.Any(f => Match(f.Cell.Key) || Match(f.Cell.Label))) continue;
            visible++;
            var content = new StackPanel { Margin = new Thickness(12, 8, 0, 0) };
            content.Children.Add(new ParameterNote { Text = terrain.Terrain.Name });
            content.Children.Add(new Expander { Header = UiText.T("来源与作用范围"), Content = Text(terrain.Terrain.File + "\n" + terrain.Terrain.Type + "\n" + terrain.Terrain.Error), Margin = new Thickness(0, 3, 0, 8) });
            foreach (var category in fields.GroupBy(f => f.Cell.Key.StartsWith("damage/") ? "地形承伤" : "专业地形参数"))
            {
                content.Children.Add(RulePresentation.Heading(UiText.T(category.Key), "EditorSectionHeading"));
                var wrap = new RuleFieldPanel(); content.Children.Add(wrap);
                foreach (var field in category)
                {
                    var item = new Grid { Margin = new Thickness(0, 5, 0, 8), VerticalAlignment = VerticalAlignment.Top };
                    foreach (var shared in new[] { "FieldLabel", "FieldParameter", "FieldInput" })
                        item.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, SharedSizeGroup = shared });
                    item.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var damage = field.Cell.Key.StartsWith("damage/");
                    item.Children.Add(Text(damage ? (advanced ? "承伤倍率" : "减伤（%）") : field.Cell.Label));
                    var parameter = new ParameterNote { Text = field.Cell.Key, ToolTip = terrain.Terrain.File };
                    Grid.SetRow(parameter, 1); item.Children.Add(parameter);
                    FrameworkElement input;
                    if (field.Cell.Boolean)
                    {
                        var choice = new ComboBox { ItemsSource = new[] { new { Label = UiText.T("是"), Value = "true" }, new { Label = UiText.T("否"), Value = "false" } }, DisplayMemberPath = "Label", SelectedValuePath = "Value" };
                        choice.SetBinding(ComboBox.SelectedValueProperty, new Binding(nameof(field.Value)) { Source = field, Mode = BindingMode.TwoWay }); input = choice;
                    }
                    else
                    {
                        var box = new TextBox();
                        box.SetBinding(TextBox.TextProperty, new Binding(damage && !advanced ? nameof(field.Reduction) : nameof(field.Value)) { Source = field, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); input = box;
                    }
                    input.SetBinding(UIElement.IsEnabledProperty, new Binding(damage && !advanced ? nameof(field.CanEditBasic) : nameof(field.CanEdit)) { Source = field }); input.VerticalAlignment = VerticalAlignment.Stretch;
                    Grid.SetRow(input, 2); item.Children.Add(input);
                    var details = new StackPanel(); Grid.SetRow(details, 3); item.Children.Add(details);
                    if (damage && !advanced && !field.CanEditBasic && field.CanEdit) details.Children.Add(Text("当前倍率超出普通范围，请在专业模式处理"));
                    var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
                    var binding = new MultiBinding { Converter = new LocalizedValueConverter() };
                    binding.Bindings.Add(new Binding(nameof(field.Status)) { Source = field }); binding.Bindings.Add(new Binding(nameof(UiText.Version)) { Source = UiText.Current });
                    status.SetBinding(TextBlock.TextProperty, binding); details.Children.Add(status);
                    details.Children.Add(new Expander { Header = UiText.T("说明"), Content = Text(field.Cell.Hint) });
                    var undo = new Button { Content = UiText.T("撤销此项"), HorizontalAlignment = HorizontalAlignment.Left }; undo.Click += async (_, _) => await field.UndoAsync(); details.Children.Add(undo);
                    wrap.Children.Add(item);
                }
            }
            var key = "terrain:" + terrain.Terrain.File + ":" + terrain.Terrain.Name;
            panel.Children.Add(Expand(TerrainWorkspace.Label(terrain.Terrain.Name), key, content));
        }
        if (visible == 0) panel.Children.Add(Text("当前模式没有匹配的可编辑地形条目"));
        foreach (var error in vm.TerrainDiagnostics) panel.Children.Add(Text(error));
        return Expand("地形规则", "terrain", panel);

        Expander Expand(string label, string key, UIElement content)
        {
            var result = new Expander { Header = RulePresentation.Heading(UiText.T(label), key == "terrain" ? "RuleCategoryHeading" : "RuleObjectHeading"), Tag = key, Content = content, Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 8), IsExpanded = vm.Search.Length > 0 || expanded.GetValueOrDefault(key) };
            result.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
            result.Expanded += (_, e) => { if (ReferenceEquals(e.OriginalSource, result)) expanded[key] = true; };
            result.Collapsed += (_, e) => { if (ReferenceEquals(e.OriginalSource, result)) expanded[key] = false; };
            return result;
        }
    }
    private static TextBlock Text(string text) => new() { Text = UiText.T(text), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4) };
}
