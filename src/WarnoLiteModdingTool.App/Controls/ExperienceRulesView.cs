using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.ViewModels;

namespace WarnoLiteModdingTool.App.Controls;

public static class ExperienceRulesView
{
    public static UIElement Build(RulesWorkspaceViewModel vm, Dictionary<string, bool> expanded)
    {
        bool Match(string text) => text.Contains(vm.Search, StringComparison.OrdinalIgnoreCase) || UiText.T(text).Contains(vm.Search, StringComparison.OrdinalIgnoreCase);
        var content = new StackPanel { Margin = new Thickness(12, 8, 0, 0) };
        content.Children.Add(Text("修改共享路线，影响所有引用者。游戏提示文本未同步。"));
        if (vm.Experience.Count == 0) content.Children.Add(Text("当前 Mod 未找到经验路线"));
        foreach (var group in vm.Experience)
        {
            var route = group.Route;
            if (vm.Search.Length > 0 && !Match("经验与老练度") && !Match(route.Name) && !Match(route.Alias) &&
                !group.Levels.Any(l => l.Cells.Any(c => Match(c.Cell.Label) || Match(c.Cell.Field)))) continue;
            var routeContent = new StackPanel { Margin = new Thickness(12, 8, 0, 0) };
            routeContent.Children.Add(new ParameterNote { Text = route.Name });
            routeContent.Children.Add(Text("共享使用者：{0}".Replace("{0}", route.Users.Count.ToString())));
            var details = new StackPanel();
            details.Children.Add(Text(route.File));
            details.Children.Add(Text(route.Users.Count == 0 ? "当前未发现显式使用者" : string.Join("\n", route.Users)));
            routeContent.Children.Add(new Expander { Header = UiText.T("使用者与来源"), Content = details, Margin = new Thickness(0, 3, 0, 8) });
            if (route.Error.Length > 0) routeContent.Children.Add(Text(route.Error));
            foreach (var level in group.Levels)
            {
                var key = "experience:" + route.File + ":" + route.Name + ":" + level.Level.Index;
                var expander = new Expander { Header = UiText.T("等级 {0}".Replace("{0}", level.Level.Index.ToString())), Tag = key, Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(7) };
                expander.Expanded += (_, e) => { if (!ReferenceEquals(e.OriginalSource, expander)) return; expander.Content ??= Editor(level); expanded[key] = true; };
                expander.Collapsed += (_, e) => { if (ReferenceEquals(e.OriginalSource, expander)) expanded[key] = false; };
                expander.IsExpanded = vm.Search.Length > 0 || expanded.GetValueOrDefault(key);
                routeContent.Children.Add(expander);
            }
            content.Children.Add(Expand(UiText.T(route.Alias) + (route.Alias == "自定义" ? " · " + route.Name : ""), "experience:" + route.File + ":" + route.Name, routeContent));
        }
        var parent = Expand(UiText.T("经验与老练度"), "experience", content);
        parent.Header = RulePresentation.Heading(UiText.T("经验与老练度"), "RuleCategoryHeading");
        parent.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
        return parent;

        Expander Expand(string title, string key, UIElement child)
        {
            var e = new Expander { Header = RulePresentation.Heading(title, "RuleObjectHeading"), Tag = key, Content = child, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(8), IsExpanded = vm.Search.Length > 0 || expanded.GetValueOrDefault(key) };
            e.Expanded += (_, args) => { if (ReferenceEquals(args.OriginalSource, e) && vm.Search.Length == 0) expanded[key] = true; };
            e.Collapsed += (_, args) => { if (ReferenceEquals(args.OriginalSource, e) && vm.Search.Length == 0) expanded[key] = false; };
            return e;
        }
    }
    private static UIElement Editor(ExperienceLevelViewModel level)
    {
        var panel = new StackPanel();
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var binding = new MultiBinding { Converter = new LocalizedValueConverter() };
        binding.Bindings.Add(new Binding(nameof(level.Status)) { Source = level });
        binding.Bindings.Add(new Binding(nameof(UiText.Version)) { Source = UiText.Current });
        status.SetBinding(TextBlock.TextProperty, binding); panel.Children.Add(status);
        var cells = new StackPanel(); cells.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(level.CanEdit)) { Source = level }); panel.Children.Add(cells);
        foreach (var category in level.Cells.GroupBy(c => c.Cell.Key.StartsWith("threshold.") ? "升级门槛" : "等级加成"))
        {
            cells.Children.Add(Text(category.Key));
            var wrap = new WrapPanel(); cells.Children.Add(wrap);
            foreach (var cell in category)
            {
                var item = new StackPanel { Width = 240, Margin = new Thickness(0, 5, 12, 8) };
                item.Children.Add(Text(cell.Cell.Label));
                item.Children.Add(new ParameterNote { Text = cell.Cell.Field, ToolTip = cell.Cell.Source + "\n" + cell.Cell.File });
                var input = new TextBox { IsReadOnly = cell.Cell.Error.Length > 0, ToolTip = cell.Cell.Error.Length > 0 ? UiText.T(cell.Cell.Error) : cell.Cell.Source };
                input.SetBinding(TextBox.TextProperty, new Binding(nameof(cell.Value)) { Source = cell, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                item.Children.Add(input);
                if (cell.Cell.Error.Length > 0) item.Children.Add(Text(cell.Cell.Error));
                wrap.Children.Add(item);
            }
        }
        if (level.Level.Notes.Count > 0)
            panel.Children.Add(new Expander { Header = UiText.T("结构说明与保留效果"), Content = Text(string.Join("\n", level.Level.Notes.Select(UiText.T))), Margin = new Thickness(0, 4, 0, 8) });
        var undo = new Button { Content = UiText.T("撤销此等级"), HorizontalAlignment = HorizontalAlignment.Left };
        undo.Click += async (_, _) => await level.UndoAsync(); panel.Children.Add(undo);
        return panel;
    }
    private static TextBlock Text(string text) => new() { Text = UiText.T(text), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4) };
}
