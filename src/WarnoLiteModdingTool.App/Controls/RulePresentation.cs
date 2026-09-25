using System.Windows;
using System.Windows.Controls;

namespace WarnoLiteModdingTool.App.Controls;

internal static class RulePresentation
{
    public static TextBlock Heading(string text, string style)
    {
        var header = new TextBlock { Text = text };
        header.SetResourceReference(FrameworkElement.StyleProperty, style);
        return header;
    }
}

/// <summary>Wraps field grids while preserving their shared label, annotation and editor rows.</summary>
public sealed class RuleFieldPanel : Panel
{
    private const double FieldWidth = 320, Gap = 12;
    public RuleFieldPanel() => Grid.SetIsSharedSizeScope(this, true);
    private static (int Columns, double Width) Layout(double width)
    {
        if (double.IsInfinity(width)) return (1, FieldWidth);
        return (Math.Max(1, (int)Math.Floor((width + Gap) / (FieldWidth + Gap))), Math.Max(0, Math.Min(FieldWidth, width)));
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        var (columns, width) = Layout(availableSize.Width);
        double total = 0, row = 0;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i]; child.Measure(new Size(width, double.PositiveInfinity));
            row = Math.Max(row, child.DesiredSize.Height);
            if ((i + 1) % columns == 0 || i == InternalChildren.Count - 1) { total += row; row = 0; }
        }
        var used = Math.Min(columns, InternalChildren.Count);
        return new Size(used == 0 ? 0 : used * width + (used - 1) * Gap, total);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var (columns, width) = Layout(finalSize.Width);
        double top = 0;
        for (var start = 0; start < InternalChildren.Count; start += columns)
        {
            var count = Math.Min(columns, InternalChildren.Count - start);
            double height = 0;
            for (var j = 0; j < count; j++) height = Math.Max(height, InternalChildren[start + j].DesiredSize.Height);
            for (var j = 0; j < count; j++) InternalChildren[start + j].Arrange(new Rect(j * (width + Gap), top, width, height));
            top += height;
        }
        return finalSize;
    }
}
