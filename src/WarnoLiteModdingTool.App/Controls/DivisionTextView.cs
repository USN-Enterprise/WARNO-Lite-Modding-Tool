using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.Settings;
using WarnoLiteModdingTool.App.ViewModels.Divisions;

namespace WarnoLiteModdingTool.App.Controls;

public sealed class DivisionTextView : UserControl
{
    public DivisionTextView()
    {
        DataContextChanged += async (_, _) => { Build(); if (IsLoaded && DataContext is DivisionTextEditorViewModel vm && !Core.Localisation.VanillaNames.UnitsAvailable) await vm.ExtractAsync(false); };
        Loaded += async (_, _) => { if (DataContext is DivisionTextEditorViewModel vm && !Core.Localisation.VanillaNames.UnitsAvailable) await vm.ExtractAsync(false); };
        System.ComponentModel.PropertyChangedEventManager.AddHandler(UiText.Current, (_, _) => Build(), nameof(UiText.Version));
    }
    private void Build()
    {
        if (DataContext is not DivisionTextEditorViewModel vm) { Content = null; return; }
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(RulePresentation.Heading(vm.DivisionName, "EditorSectionHeading"));
        panel.Children.Add(Text("自定义正文作为默认文本使用，不会自动翻译；旧token的其他语言译文不会自动继承。"));
        var tools = new WrapPanel(); panel.Children.Add(tools);
        var choose = Button("选择游戏目录"); tools.Children.Add(choose);
        choose.Click += async (_, _) =>
        {
            if (vm.Busy) return;
            var dialog = new OpenFolderDialog { Title = UiText.T("选择WARNO游戏目录") };
            if (dialog.ShowDialog() != true) return;
            try { var settings = new UiSettings(); settings.Save(settings.Load() with { GameDirectory = dialog.FolderName }); await vm.ExtractAsync(true); }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        };
        var extract = Button("从本机提取 / 刷新原版简介"); extract.Click += async (_, _) => await vm.ExtractAsync(true); tools.Children.Add(extract);
        var cancel = Button("取消提取"); cancel.Click += (_, _) => vm.CancelExtraction(); tools.Children.Add(cancel);
        var status = Text(""); BindText(status, vm, nameof(vm.Status)); panel.Children.Add(status);
        var language = new ComboBox { ItemsSource = new[] { new { Value = "SC", Label = "中文" }, new { Value = "US", Label = "English" } }, DisplayMemberPath = "Label", SelectedValuePath = "Value", HorizontalAlignment = HorizontalAlignment.Left, Width = 150, Margin = new Thickness(0, 8, 0, 8) };
        language.SetBinding(ComboBox.SelectedValueProperty, new Binding(nameof(vm.ReferenceLanguage)) { Source = vm, Mode = BindingMode.TwoWay });
        panel.Children.Add(Text("原版参考语言（切换不替换编辑内容）")); panel.Children.Add(language);
        var editors = new StackPanel(); editors.SetBinding(IsEnabledProperty, new Binding(nameof(vm.CanEdit)) { Source = vm }); panel.Children.Add(editors);
        foreach (var part in vm.Parts)
        {
            editors.Children.Add(RulePresentation.Heading(UiText.T(part.Label), "EditorSectionHeading")); editors.Children.Add(new ParameterNote { Text = part.Field });
            var source = Text(""); BindText(source, part, nameof(part.Source)); editors.Children.Add(source);
            var input = new TextBox { AcceptsReturn = true, AcceptsTab = true, VerticalContentAlignment = VerticalAlignment.Top, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 140, MaxHeight = 380, Margin = new Thickness(0, 4, 0, 4) };
            input.SetBinding(TextBox.TextProperty, new Binding(nameof(part.Text)) { Source = part, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            input.SetBinding(IsEnabledProperty, new Binding(nameof(part.CanEdit)) { Source = part }); editors.Children.Add(input);
            var error = Text(""); BindText(error, part, nameof(part.Error)); editors.Children.Add(error);
            var referencePanel = new StackPanel(); referencePanel.Children.Add(new ParameterNote { Text = part.Token });
            var reference = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            reference.SetBinding(TextBox.TextProperty, new Binding(nameof(part.Reference)) { Source = part, Mode = BindingMode.OneWay }); referencePanel.Children.Add(reference);
            var adopt = Button("采用当前参考文字"); adopt.Click += (_, _) => part.AdoptReference(); referencePanel.Children.Add(adopt);
            editors.Children.Add(new Expander { Header = UiText.T("原版参考与token"), Content = referencePanel, Margin = new Thickness(0, 0, 0, 12) });
        }
        var actions = new WrapPanel(); editors.Children.Add(actions);
        var save = Button("加入草稿"); save.Click += async (_, _) => await vm.SaveAsync(); actions.Children.Add(save);
        var revert = Button("撤销本页输入"); revert.Click += (_, _) => vm.RevertInput(); actions.Children.Add(revert);
        panel.Children.Add(Text("招牌单位在“师级设置与费用 → 特色单位”中修改。"));
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    private static Button Button(string label) => new() { Content = UiText.T(label), Margin = new Thickness(0, 0, 8, 6), Padding = new Thickness(9, 5, 9, 5) };
    private static TextBlock Text(string text) => new() { Text = UiText.T(text), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 5) };
    private static void BindText(TextBlock block, object source, string property)
    {
        var binding = new MultiBinding { Converter = new LocalizedValueConverter() };
        binding.Bindings.Add(new Binding(property) { Source = source }); binding.Bindings.Add(new Binding(nameof(UiText.Version)) { Source = UiText.Current });
        block.SetBinding(TextBlock.TextProperty, binding);
    }
}
