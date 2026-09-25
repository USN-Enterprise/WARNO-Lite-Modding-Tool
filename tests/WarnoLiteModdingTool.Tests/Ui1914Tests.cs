using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarnoLiteModdingTool.App.Controls;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.Theming;
using WarnoLiteModdingTool.App.ViewModels;

namespace WarnoLiteModdingTool.Tests;

internal static partial class Program
{
    private static void Verify1914Style(MainViewModel main, Window window, string root)
    {
        RulesFixture184(root, "\n");
        var width = window.Width; var height = window.Height;
        var theme = ThemeManager.CurrentTheme;
        var content = (FrameworkElement)window.Content;
        var transform = content.LayoutTransform;
        try
        {
            RunWithDispatcher(main.OpenProjectAsync(root), window.Dispatcher);
            main.SelectedModule = main.Modules.Single(m => m.Key == "rules");
            foreach (var advanced in new[] { false, true }) foreach (var language in new[] { "zh-CN", "en" })
            {
                main.AdvancedMode = advanced; UiText.Current.SetLanguage(language); main.RulesWorkspace!.Category = "全部";
                ThemeManager.ApplyTheme(language == "en" ? AppTheme.DarkBlue : AppTheme.LightBlue, false);
                window.Width = 1420; window.Height = 1120; DrainDispatcher(window.Dispatcher);
                var rules = FindVisualChildren<RulesView>(window).Single();
                var expanders = FindVisualChildren<Expander>(rules).ToArray();
                foreach (var e in expanders) e.IsExpanded = e.Tag?.ToString()?.StartsWith("terrain") == true;
                DrainDispatcher(window.Dispatcher);
                var headings = FindVisualChildren<TextBlock>(rules).Where(h => ReferenceEquals(h.Style, h.TryFindResource("RuleCategoryHeading"))).ToArray();
                Assert(headings.Length >= 5, "规则、经验与地形大类同屏参与格式检查");
                var first = headings[0]; var left = first.TransformToAncestor(rules).Transform(new Point()).X;
                foreach (var h in headings)
                    Assert(h.FontSize == first.FontSize && h.FontWeight == first.FontWeight && h.FontFamily.Equals(first.FontFamily) && Math.Abs(h.TransformToAncestor(rules).Transform(new Point()).X - left) < 1,
                        "同级规则大类字体和左起点一致：" + h.Text);
                var terrain = FindVisualChildren<Expander>(rules).Single(e => e.Tag?.ToString() == "terrain");
                Assert(((TextBlock)terrain.Header).FontSize == 15, "地形大类采用正式15号标题");
                var obj = FindVisualChildren<Expander>(terrain).Single(e => e.Header is TextBlock h && h.Text == "CustomWood");
                Assert(((TextBlock)obj.Header).FontSize == 14 && ((TextBlock)obj.Header).FontWeight == FontWeights.SemiBold, "地形对象层级14号半粗体");
                SaveUiSnapshot(window, $"1914-same-page-{language}-{advanced}.png");
                main.RulesWorkspace.Category = "地形规则"; DrainDispatcher(window.Dispatcher);
                foreach (var e in FindVisualChildren<Expander>(rules).Where(e => e.Tag?.ToString()?.StartsWith("terrain") == true)) e.IsExpanded = true;
                DrainDispatcher(window.Dispatcher);
                foreach (var panel in FindVisualChildren<RuleFieldPanel>(rules))
                {
                    var grids = panel.Children.Cast<Grid>().ToArray();
                    if (grids.Length > 1)
                    {
                        ((TextBlock)grids[0].Children[0]).Text += " — multiline label / 多行标签验证，检查同组输入框对齐";
                        ((ParameterNote)grids[0].Children[1]).Text += "/VeryLongSyntheticParameterName_ForWrapping_Only/SecondSegment_WithoutTruncation";
                    }
                }
                foreach (var scale in new[] { 1d, 1.25, 1.5 })
                {
                    // Preserve the app's supported 1200-DIP minimum while simulating larger pixels.
                    window.Width = (scale == 1 ? 1420 : 1200) * scale; window.Height = 1000 * scale;
                    content.LayoutTransform = new ScaleTransform(scale, scale); DrainDispatcher(window.Dispatcher); window.UpdateLayout();
                    foreach (var panel in FindVisualChildren<RuleFieldPanel>(rules))
                    {
                        var cells = panel.Children.Cast<Grid>().ToArray();
                        foreach (var cell in cells)
                        {
                            var origin = cell.TransformToAncestor(panel).Transform(new Point());
                            Assert(origin.X + cell.ActualWidth <= panel.ActualWidth + 1, "窄窗口字段不越过面板右边界");
                            var input = cell.Children.Cast<FrameworkElement>().Single(c => Grid.GetRow(c) == 2);
                            foreach (var other in cells.Where(c => Math.Abs(c.TransformToAncestor(panel).Transform(new Point()).Y - origin.Y) < 1))
                            {
                                var peer = other.Children.Cast<FrameworkElement>().Single(c => Grid.GetRow(c) == 2);
                                Assert(Math.Abs(input.TranslatePoint(new Point(), panel).Y - peer.TranslatePoint(new Point(), panel).Y) < 1 && Math.Abs(input.ActualHeight - peer.ActualHeight) < 1,
                                    "同排长短注记、数值/布尔输入顶部和高度一致");
                            }
                        }
                    }
                    SaveUiSnapshot(window, $"1914-wrap-{language}-{advanced}-{scale}.png");
                }
                content.LayoutTransform = transform;
            }
        }
        finally
        {
            content.LayoutTransform = transform; window.Width = width; window.Height = height;
            ThemeManager.ApplyTheme(theme, false); UiText.Current.SetLanguage("zh-CN");
        }
    }
}
