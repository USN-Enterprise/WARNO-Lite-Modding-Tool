using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace WarnoLiteModdingTool.App.Localisation;

public sealed class UiText : INotifyPropertyChanged
{
    public static UiText Current { get; } = new();
    private readonly Dictionary<string, string> _english = new(StringComparer.Ordinal);
    private readonly List<(System.Text.RegularExpressions.Regex Pattern, string Format)> _templates = [];
    public string Language { get; private set; } = "system";
    public bool English => Language == "en" || Language == "system" && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";
    public int Version { get; private set; }
    private UiText()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WarnoLiteModdingTool.App.Localisation.en.tsv");
        if (stream is null) return;
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            var split = line.IndexOf('|');
            if (split > 0) _english[line[..split].Replace("\\n", "\n")] = line[(split + 1)..].Replace("\\n", "\n");
        }
        foreach (var pair in _english.Where(p => p.Key.Contains("{0}", StringComparison.Ordinal)))
        {
            var pattern = System.Text.RegularExpressions.Regex.Escape(pair.Key);
            for (var i = 0; i < 16; i++) pattern = pattern.Replace("\\{" + i + "}", "(.*)", StringComparison.Ordinal);
            _templates.Add((new("^" + pattern + "$", System.Text.RegularExpressions.RegexOptions.Singleline), pair.Value));
        }
    }
    public string this[string key] => T(key);
    public static string T(string text)
    {
        if (!Current.English) return text;
        if (Current._english.TryGetValue(text, out var exact)) return exact;
        foreach (var (pattern, format) in Current._templates)
        {
            var match = pattern.Match(text);
            if (match.Success)
            {
                try { return string.Format(CultureInfo.InvariantCulture, format, match.Groups.Cast<System.Text.RegularExpressions.Group>().Skip(1).Select(g => (object)T(g.Value)).ToArray()); }
                catch (FormatException) { return text; }
            }
        }
        if (text.Contains('\n')) return string.Join("\n", text.Split('\n').Select(T));
        if (text.StartsWith("• ", StringComparison.Ordinal)) return "• " + T(text[2..]);
        return text;
    }
    public void SetLanguage(string language)
    {
        Language = language is "en" or "zh-CN" ? language : "system";
        Version++; PropertyChanged?.Invoke(this, new("Item[]")); PropertyChanged?.Invoke(this, new(nameof(Version)));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public static void Bind(DependencyObject target, DependencyProperty property, string text) =>
        BindingOperations.SetBinding(target, property, new Binding(nameof(Version)) { Source = Current, Mode = BindingMode.OneWay, Converter = new StaticTextConverter(), ConverterParameter = text });
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; } = "";
    public override object ProvideValue(IServiceProvider serviceProvider) => new Binding(nameof(UiText.Version))
        { Source = UiText.Current, Mode = BindingMode.OneWay, Converter = new StaticTextConverter(), ConverterParameter = Key }.ProvideValue(serviceProvider);
}

public sealed class StaticTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => UiText.T(parameter?.ToString() ?? "");
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

// Two inputs make translated labels update without resetting the project or editor bindings.
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocalizedBindingExtension : MarkupExtension
{
    public string Path { get; set; } = "";
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new MultiBinding { Converter = new LocalizedValueConverter(), Mode = BindingMode.OneWay };
        binding.Bindings.Add(new Binding(Path));
        binding.Bindings.Add(new Binding(nameof(UiText.Version)) { Source = UiText.Current });
        return binding.ProvideValue(serviceProvider);
    }
}
public sealed class LocalizedValueConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) => UiText.T(values[0]?.ToString() ?? "");
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
