using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using WarnoLiteModdingTool.App.Controls;
using WarnoLiteModdingTool.App.ViewModels;
using WarnoLiteModdingTool.Core.Ndf;
namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static Task ScannerConstants185()
    {
        foreach(var nl in new[]{"\n","\r\n"})
        {
            var source = """
DefaultTransportLoadRadius is 78 // GRU
WreckUnloadDamageBonus_Default_Physical is 20
export Flag is True
Flag2 is false
ExperienceMultiplierBonusOnKill is 1
ZocRadiusGRU is 1.9 * ~/ActionPointConsumptionRefs/CaseSizeGRU
Formula is ~/Base * (2 + 3)
OtherFormula is Radius * 2
EnumValue is EValue/SomeOption
Text is 'literal \' quotation ( ]'
Array is [
  1, MAP [(0, [2,3])],
  'Fake is TType(', // ] ignored
  (4 + 5)
]
Map is MAP [(0,[1,2])]
Good is TEntityDescriptor
(
 Value = [1,2]
 Text = 'NotAnObject is TType('
)
Bad is TEntityDescriptor // missing opening paren must not be suppressed
Second is TEntityDescriptor ( Value = 2 )
Broken is TEntityDescriptor ( Value = [1]
""";
            source=source.Replace("\n",nl);
            var result=Scanner.Scan(source,"constants.ndf","rules");
            Assert(result.Objects.Count==2,"常量不作为对象且后续对象仍被索引");
            Assert(result.Diagnostics.Count==2&&result.Diagnostics.Any(d=>d.Message.Contains("Bad"))&&result.Diagnostics.Any(d=>d.Message.Contains("Broken")),"只报告真正构造器错误");
            Assert(result.Objects.All(o=>Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(source).AsSpan((int)o.ByteOffset,(int)o.ByteLength))==source.Substring(o.CharacterOffset,o.CharacterLength)),"跳过常量仍保留字节偏移与原区段");
            foreach(var invalid in new[]{"X is [1,2", "X is 'unclosed", "X is (1 + 2]"})
                Assert(Scanner.Scan(invalid,"broken.ndf","rules").Diagnostics.Count==1,"损坏的常量结构不能被忽略");
        }
        return Task.CompletedTask;
    }
    private static void Verify185Ui(MainViewModel vm,Window window)
    {
        vm.AdvancedMode=false;
        vm.SelectedModule=vm.Modules.Single(m=>m.Key=="ammo");
        foreach(var section in vm.AmmoWorkspace!.FieldSections)section.IsExpanded=section.Groups.Any(g=>g.Fields.Any(f=>f.OriginalParameter=="AffichageMunitionParSalve" || f.OriginalParameter=="ShotsCountPerSalvo"));
        DrainDispatcher(window.Dispatcher);SaveUiSnapshot(window,"185-ammo-basic.png");
        var notes=FindVisualChildren<ParameterNote>((DependencyObject)window.Content).Where(n=>n.IsVisible || n.Visibility==Visibility.Visible).ToArray();
        Assert(notes.Any(n=>n.Text=="AffichageMunitionParSalve")&&notes.Any(n=>n.Text=="ShotsCountPerSalvo"),"两个弹药字段直接显示原参数备注");
        Assert(vm.UnitWorkspace!.Fields.All(f=>!string.IsNullOrWhiteSpace(f.OriginalParameter)),"所有单位字段有原参数名");
        vm.SelectedModule=vm.Modules.Single(m=>m.Key=="rules");DrainDispatcher(window.Dispatcher);
        var rules=FindVisualChildren<RulesView>((DependencyObject)window.Content).Single();
        var first=FindVisualChildren<Expander>(rules).First();first.IsExpanded=true;
        DrainDispatcher(window.Dispatcher);SaveUiSnapshot(window,"185-rules-basic.png");
        Assert(FindVisualChildren<ParameterNote>(rules).Any(n=>n.Text=="DefaultArgentInitial"),"规则数值旁显示原参数");
        Assert(!FindVisualChildren<TextBlock>(rules).Any(n=>n.Text.Contains("32 项")||n.Text.Contains("50 项")),"规则页不显示模式数量");
        WarnoLiteModdingTool.App.Localisation.UiText.Current.SetLanguage("en");vm.SelectedModule=vm.Modules.Single(m=>m.Key=="ammo");
        foreach(var section in vm.AmmoWorkspace!.FieldSections)section.IsExpanded=section.Groups.Any(g=>g.Fields.Any(f=>f.OriginalParameter=="AffichageMunitionParSalve" || f.OriginalParameter=="ShotsCountPerSalvo"));
        DrainDispatcher(window.Dispatcher);SaveUiSnapshot(window,"185-ammo-english.png");
        Assert(FindVisualChildren<ParameterNote>((DependencyObject)window.Content).Any(n=>n.Text=="AffichageMunitionParSalve"),"英文模式不翻译原参数");
        WarnoLiteModdingTool.App.Localisation.UiText.Current.SetLanguage("zh-CN");vm.SelectedModule=vm.Modules.Single(m=>m.Key=="units");
    }
}
