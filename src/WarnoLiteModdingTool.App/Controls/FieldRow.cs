using System.Windows;
using System.Windows.Controls;

namespace WarnoLiteModdingTool.App.Controls;

/// <summary>Shared readable label, source parameter and editor layout.</summary>
public sealed class FieldRow : HeaderedContentControl
{
    public static readonly DependencyProperty ParameterProperty = DependencyProperty.Register(
        nameof(Parameter), typeof(string), typeof(FieldRow), new PropertyMetadata(string.Empty));

    public string Parameter
    {
        get => (string)GetValue(ParameterProperty);
        set => SetValue(ParameterProperty, value);
    }
}
