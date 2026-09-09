using System.Windows;

namespace WarnoLiteModdingTool.App.Theming;

public static class ThemeManager
{
    private const string ThemePathMarker = "Themes/";
    private static UiThemeStore _store = new();

    public static event Action? ThemeChanged;
    public static AppTheme CurrentTheme { get; private set; } = AppTheme.LightBlue;

    public static void Initialize(UiThemeStore? store = null)
    {
        if (store is not null)
        {
            _store = store;
        }

        ApplyTheme(_store.Load(), false);
    }

    public static void ApplyTheme(AppTheme theme, bool persist = true)
    {
        var resources = Application.Current?.Resources
            ?? throw new InvalidOperationException("应用资源尚未初始化。");
        resources.Remove("SurfaceBrush"); resources.Remove("SurfaceAltBrush");
        var dictionaries = resources.MergedDictionaries;
        var replacement = new ResourceDictionary
        {
            Source = ThemeUri(theme)
        };
        var themeIndex = -1;
        for (var index = 0; index < dictionaries.Count; index++)
        {
            if (IsThemeDictionary(dictionaries[index]))
            {
                themeIndex = index;
                break;
            }
        }

        if (themeIndex >= 0)
        {
            dictionaries[themeIndex] = replacement;
        }
        else
        {
            dictionaries.Add(replacement);
        }

        CurrentTheme = theme;
        BackgroundAppearance.Apply();
        ThemeChanged?.Invoke();
        if (persist)
        {
            _store.Save(theme);
        }
    }

    private static bool IsThemeDictionary(ResourceDictionary dictionary) =>
        dictionary.Source?.OriginalString.Replace('\\', '/').Contains(ThemePathMarker, StringComparison.OrdinalIgnoreCase) == true;

    public static bool IsDark => CurrentTheme is not (AppTheme.LightBlue or AppTheme.WarmPaper or AppTheme.CoolWhite);
    public static string Name(AppTheme t) => t switch { AppTheme.DarkBlue => "黑蓝", AppTheme.LightBlue => "白蓝", AppTheme.BlackGold => "黑金", AppTheme.BlackGreen => "黑绿终端", AppTheme.GraphiteCyan => "石墨青", AppTheme.WarmPaper => "暖纸棕", _ => "冷白蓝" };
    private static Uri ThemeUri(AppTheme theme) => new($"/WarnoLiteModdingTool;component/Themes/{theme}.xaml", UriKind.RelativeOrAbsolute);
}
