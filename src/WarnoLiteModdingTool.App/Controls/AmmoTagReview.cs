using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.ViewModels.Weapons;

namespace WarnoLiteModdingTool.App.Controls;

/// <summary>Optional, explicit tag changes; no implicit guidance reverse mapping.</summary>
public sealed class AmmoTagReview : ContentControl
{
    public AmmoTagReview() { Loaded += (_, _) => Build(); DataContextChanged += (_, _) => Build(); }
    private void Build()
    {
        Content = null;
        if (DataContext is not WeaponFieldViewModel vm || !vm.IsGuidanceReview) return;
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var note = new TextBlock { Text = UiText.T("标签默认保留；能力开关不会自动改写说明。"), TextWrapping = TextWrapping.Wrap };
        note.SetResourceReference(StyleProperty, "SecondaryGridText"); panel.Children.Add(note);
        var button = new Button { Content = UiText.T("核对说明标签"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0) };
        button.SetResourceReference(StyleProperty, "SecondaryButton");
        button.Click += (_, _) =>
        {
            var tags = vm.LinkedTags;
            if (tags is null || !tags.IsEditable) { note.Text = UiText.T("当前弹药没有可编辑的标签列表"); return; }
            var body = new StackPanel { Margin = new Thickness(18) };
            body.Children.Add(new TextBlock { Text = UiText.T("关闭射后不理不会猜测manual或semiAuto；其他标签保持原顺序。"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            body.Children.Add(new AmmoAdvancedInput { DataContext = tags });
            var selected = JsonSerializer.Deserialize<List<string>>(tags.EditValue) ?? [];
            var proposed = selected.ToList();
            if (vm.EditValue == "是" && tags.Choices.Contains("F&F"))
            { proposed.RemoveAll(t => t is "manual" or "semiAuto"); if (!proposed.Any(t => t is "F&F" or "F&F_boresight")) proposed.Add("F&F"); }
            if (vm.EditValue == "否") proposed.RemoveAll(t => t is "F&F" or "F&F_boresight");
            var added = proposed.Except(selected).ToArray(); var removed = selected.Except(proposed).ToArray();
            var dialog = new Window { Owner = Window.GetWindow(this), Title = UiText.T("核对说明标签"), Width = 560, SizeToContent = SizeToContent.Height, MaxHeight = 650, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            dialog.SetResourceReference(BackgroundProperty, "SurfaceBrush"); dialog.SetResourceReference(ForegroundProperty, "TextBrush");
            if (added.Length + removed.Length > 0)
            {
                var suggest = new Button { Content = UiText.T("采用标签建议") + "  +[" + string.Join(", ", added) + "]  −[" + string.Join(", ", removed) + "]", Margin = new Thickness(0, 10, 0, 0) };
                suggest.Click += async (_, _) =>
                {
                    try { tags.EditValue = JsonSerializer.Serialize(proposed); await tags.FlushAsync(); dialog.Close(); }
                    catch (Exception e) when (e is System.IO.IOException or InvalidOperationException) { note.Text = e.Message; }
                };
                body.Children.Add(suggest);
            }
            var keep = new Button { Content = UiText.T("保留当前标签并关闭"), Margin = new Thickness(0, 10, 0, 0) };
            keep.SetResourceReference(StyleProperty, "SecondaryButton"); keep.Click += (_, _) => dialog.Close(); body.Children.Add(keep);
            dialog.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; dialog.ShowDialog();
        };
        panel.Children.Add(button); Content = panel;
    }
}
