using System.Windows;
using System.Windows.Controls;
using WarnoLiteModdingTool.Core.Units;
namespace WarnoLiteModdingTool.App.Controls;

/// <summary>Read-only source parameter annotation, visible in both editor modes.</summary>
public sealed class ParameterNote : TextBlock
{
    public ParameterNote()
    {
        FontSize = 10; TextWrapping = TextWrapping.Wrap;
        Margin = new Thickness(0, 2, 0, 5);
        SetResourceReference(ForegroundProperty, "MutedTextBrush");
    }
    public static string ForUnit(UnitFieldDefinition definition, bool merged = false)
    {
        var s = definition.Selector;
        if (merged && definition.Key == "armor.front.family")
            return "BlindageProperties.{ResistanceFront, ResistanceSides, ResistanceRear, ResistanceTop}.Family";
        if (merged && !Advanced.EditorMode.IsAdvanced && s.MapKey is {} mapKey && definition.Key is "recon.vision.standard" or "recon.optics.standard")
            return s.FieldName + "[" + mapKey[..mapKey.LastIndexOf('/')] + ": Standard / LowAltitude / HighAltitude]";
        return s.FieldName + (s.MapKey is null ? "" : "[" + s.MapKey + "]") +
            (s.NestedField is null ? "" : "." + s.NestedField) + (s.ArgumentName is null ? "" : "." + s.ArgumentName);
    }
    public static StackPanel Header(string label, string parameter)
    {
        var panel = new StackPanel(); var title = new TextBlock();
        Localisation.UiText.Bind(title, TextProperty, label);
        panel.Children.Add(title); panel.Children.Add(new ParameterNote { Text = parameter });
        return panel;
    }
}
