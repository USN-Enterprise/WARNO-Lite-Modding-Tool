using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using WarnoLiteModdingTool.App.Advanced;

namespace WarnoLiteModdingTool.App.Localisation;

public static class GameText
{
    private static readonly Dictionary<string, (string Zh, string En)> Countries = Parse("SOV|苏联;DDR|东德;RDA|东德;POL|波兰;TCH|捷克斯洛伐克;US|美国;UK|英国;RFA|西德;FR|法国;CAN|加拿大;BEL|比利时;NL|荷兰;DK|丹麦;ESP|西班牙");
    private static readonly Dictionary<string, (string Zh, string En)> Roles = Parse("AA|防空|Air Defense;AT|反坦克|Anti-Tank;appui|火力支援|Fire Support;armor|装甲|Armor;engineer|工兵|Engineers;howitzer|榴弹炮|Howitzer;mortar|迫击炮|Mortar;mlrs|火箭炮|MLRS;infantry|步兵|Infantry;ifv|步兵战车|IFV;reco|侦察|Recon;supply|补给|Supply;transport|运输|Transport;sead|防空压制|SEAD;uav|无人机|Drone;hq_helo|指挥直升机|CMD Helo;hq_inf|指挥步兵|CMD Infantry;hq_tank|指挥坦克|CMD Tank;hq_veh|指挥车辆|CMD Vehicle");
    private static readonly Dictionary<string, (string Zh, string En)> Factories = Parse("Logistic|后勤|Logistics;Infantry|步兵|Infantry;Art|火炮|Artillery;Tanks|坦克|Tanks;Recons|侦察|Recon;DCA|防空|Air Defense;Helis|直升机|Helicopters;Planes|飞机|Aircraft");
    private static readonly Dictionary<string, (string Zh, string En)> Categories = Parse("Air_CAS|近距空中支援;AirSup|制空战机;ArtShell|炮兵;CanonAA|高射炮;Command|指挥单位;Command_Infantry|指挥步兵;CommandVehicle|指挥车辆;Engineer|工兵;Gendarme|宪兵;GroundAtk|对地攻击机;GunArtillery|身管炮兵;HeliAttack|攻击直升机;HeliTransport|运输直升机;Inf|步兵;Inf_Elite|精锐步兵;Inf_Militia|民兵;KaJaPa|坦克歼击车;Logistic|后勤;MLRS|火箭炮;Multirole|多用途战机;Reco|侦察单位;Recon_INF|侦察步兵;Recon_Vehicle|侦察车辆;SAM|防空导弹;Tank|坦克;TankDestroyer|坦克歼击车;TankDestroyerMissile|反坦克导弹车;Transport|运输单位;Vehicle|车辆");
    private static Dictionary<string, (string Zh, string En)> Parse(string text) => text.Split(';').Select(x => x.Split('|')).ToDictionary(x => x[0], x => (x[1], x.Length > 2 ? x[2] : x[0]), StringComparer.OrdinalIgnoreCase);
    public static string CategoryKey(string value) => value.Split('/').Last().Replace("TAcknowUnitType_", "");
    public static bool VisibleCategory(string value) => EditorMode.IsAdvanced || !IsVariant(CategoryKey(value));
    private static bool IsVariant(string value) => value.Contains("Legion", StringComparison.OrdinalIgnoreCase) || value.EndsWith("_Marine") || value.EndsWith("_QUE") || value.EndsWith("_SK") || value is "Inf2" or "Transport2" or "Transport3" or "GroundAtk2";
    public static string Display(string kind, string raw)
    {
        if (EditorMode.IsAdvanced) return raw;
        kind = kind.Replace("structure.", "");
        if (kind == "division") return DivisionNames.Display(raw, UiText.Current.English);
        if (kind == "category" && !raw.Contains(" · ") && !VisibleCategory(raw)) return "";
        if (raw.Contains(" · ")) return string.Join(" · ", raw.Split(" · ").Where(v => kind != "category" || VisibleCategory(v)).Select(v=>Display(kind,v)));
        var key = kind == "category" ? CategoryKey(raw) : raw.Split('/').Last();
        var map = kind switch { "country" => Countries, "role" => Roles, "factory" => Factories, "category" => Categories, _ => null };
        if (kind == "category" && UiText.Current.English) return raw;
        return map?.TryGetValue(key, out var value) == true ? UiText.Current.English ? value.En : value.Zh : raw;
    }
}

public sealed class GameBindingExtension : MarkupExtension
{
    public string Path { get; set; } = "";
    public string Kind { get; set; } = "";
    public string KindPath { get; set; } = "";
    public bool Visibility { get; set; }
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var b = new MultiBinding { Converter = new GameConverter(Kind, Visibility), Mode = BindingMode.OneWay };
        b.Bindings.Add(new Binding(Path));
        b.Bindings.Add(new Binding(nameof(UiText.Version)) { Source = UiText.Current });
        b.Bindings.Add(KindPath.Length == 0 ? new Binding { Source = Kind } : new Binding(KindPath));
        return b.ProvideValue(serviceProvider);
    }
}
public sealed class GameConverter(string kind, bool visibility) : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var raw = values[0]?.ToString() ?? "";
        var k = values.Length > 2 ? values[2]?.ToString() ?? kind : kind;
        return visibility ? (k is "category" or "structure.category" && !GameText.VisibleCategory(raw) ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible) : GameText.Display(k, raw);
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
