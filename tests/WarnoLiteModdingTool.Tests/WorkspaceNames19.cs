using System.Windows;
using System.Windows.Controls;
namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static object FindWorkspaceName(FrameworkElement window, string name)
    {
        if (window.FindName(name) is { } item) return item;
        foreach (var page in new[] { "UnitPage", "WeaponPage", "AmmoPage", "DivisionPage", "DraftPage", "ProblemPage", "ObjectPage" })
            if (window.FindName(page) is UserControl view && view.FindName(name) is { } child) return child;
        throw new System.InvalidOperationException($"Missing workspace element: {name}");
    }
}
