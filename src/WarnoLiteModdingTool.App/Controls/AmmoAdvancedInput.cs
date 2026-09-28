using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.ViewModels.Weapons;
using WarnoLiteModdingTool.Core.Images;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.App.Controls;

/// <summary>Typed choices and ordered tags shared by Ammo and local weapon editors.</summary>
public sealed class AmmoAdvancedInput : ContentControl
{
    private int _generation;
    public AmmoAdvancedInput() { DataContextChanged += (_, _) => Build(); }
    private void Build()
    {
        var generation = ++_generation;
        Content = null;
        if (DataContext is not WeaponFieldViewModel vm || !vm.IsAdvancedEditor) return;
        var panel = new StackPanel();
        panel.SetBinding(IsEnabledProperty, new Binding(nameof(vm.IsEditable)));
        if (vm.Field.Definition.ValueKind == WeaponValueKind.Tags)
        {
            List<string> selected;
            try { selected = JsonSerializer.Deserialize<List<string>>(vm.EditValue) ?? []; } catch (JsonException) { selected = []; }
            var tags = new WrapPanel();
            foreach (var value in vm.Choices)
            {
                var check = new CheckBox { Content = value, IsChecked = selected.Contains(value), Margin = new Thickness(0, 4, 12, 4) };
                check.Click += (_, _) => { if (check.IsChecked == true) { if (!selected.Contains(value)) selected.Add(value); } else selected.Remove(value); vm.EditValue = JsonSerializer.Serialize(selected); };
                tags.Children.Add(check);
            }
            panel.Children.Add(new Expander { Header = UiText.T("选择标签（保留原顺序）"), Content = tags });
        }
        else
        {
            var picker = new SearchPicker { ItemsSource = vm.ReferenceChoices, DisplayMemberPath = "Label", SecondaryMemberPath = "Search", Placeholder = UiText.T("搜索当前 Mod 候选") };
            picker.SetBinding(SearchPicker.SelectedItemProperty, new Binding(nameof(vm.SelectedReference)) { Mode = BindingMode.TwoWay });
            panel.Children.Add(picker);
            if (vm.Field.Definition.FieldName == "InterfaceWeaponTexture")
            {
                var image = new Image { Height = 96, Stretch = System.Windows.Media.Stretch.Uniform, Margin = new Thickness(0, 6, 0, 0) };
                var status = new TextBlock { TextWrapping = TextWrapping.Wrap }; status.SetResourceReference(StyleProperty, "SecondaryGridText");
                var preview = new StackPanel(); preview.Children.Add(image); preview.Children.Add(status);
                var expand = new Expander { Header = UiText.T("图片预览"), Content = preview };
                async Task Load()
                {
                    var key = vm.EditValue;
                    try
                    {
                        var texture = await Task.Run(() => ModTextures.Read(vm.ProjectRoot, true).FirstOrDefault(t => t.Key == key));
                        var result = texture is null ? null : await LocalGameImages.LoadAsync(vm.ProjectRoot, texture.Source, CancellationToken.None);
                        if (_generation != generation || vm.EditValue != key) return;
                        image.Source = result;
                        status.Text = image.Source is null ? UiText.T("暂无图片") : "";
                    }
                    catch (Exception e) when (e is System.IO.IOException or InvalidOperationException or ArgumentException or NotSupportedException)
                    { image.Source = null; status.Text = UiText.T("暂无图片"); }
                }
                expand.Expanded += async (_, _) => await Load();
                picker.SelectedItemChanged += async (_, _) => { if (expand.IsExpanded) await Load(); };
                panel.Children.Add(expand);
            }
        }
        Content = panel;
    }
}
