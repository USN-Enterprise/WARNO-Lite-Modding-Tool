using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarnoLiteModdingTool.Core.Localisation;
using WarnoLiteModdingTool.Core.Strategic;
using WarnoLiteModdingTool.App.Controls;
using WarnoLiteModdingTool.App.Localisation;

namespace WarnoLiteModdingTool.Tests;

internal static partial class Program
{
    private static Dictionary<string, Dictionary<string,string>> SyntheticNames() => GameNameCache.Keys.ToDictionary(k => k,
        k => new Dictionary<string,string> { [TokenKey192("TESTNAME01").ToString()] = k.StartsWith("SC") ? "测试名称" : "Test name" });
    private static ulong TokenKey192(string token)
    {
        ulong value = 0; foreach (var c in token) value = (value << 6) | (uint)(c >= 'A' ? c - 54 : c - 47); return value;
    }
    private static byte[] Trad192(string text)
    {
        using var memory = new MemoryStream(); using var writer = new BinaryWriter(memory);
        writer.Write("TRAD"u8); writer.Write(1); writer.Write(TokenKey192("TESTNAME01")); writer.Write(24); writer.Write(text.Length); writer.Write(Encoding.Unicode.GetBytes(text)); return memory.ToArray();
    }
    private static void Archive192(string path, string text, int version = 2)
    {
        using var directory = new MemoryStream(); using var writer = new BinaryWriter(directory);
        writer.Write(new byte[] {9,0,0,0,0,0,0,0,0}); var payload = Trad192(text); ulong position = 0;
        foreach (var key in GameNameCache.Keys)
        {
            var name = Encoding.UTF8.GetBytes(version == 2 ? "AllPlatforms/Localisation/" + key + ".dic" : "Localisation/" + key.Split('/')[1] + "-" + key.Split('/')[0] + ".dic");
            writer.Write(0); writer.Write(40 + name.Length + 1); writer.Write(position); writer.Write((ulong)payload.Length);
            if(version==2) writer.Write(MD5.HashData(payload)); else { writer.Write(GameNameCache.Crc32(payload)); writer.Write(new byte[12]); } writer.Write(name); writer.Write((byte)0); position += (ulong)payload.Length;
        }
        var header = new byte[80]; "edat\u0002\0\0\0"u8.CopyTo(header);
        BitConverter.GetBytes(version).CopyTo(header,4); BitConverter.GetBytes(80).CopyTo(header,version==2?25:8); BitConverter.GetBytes((int)directory.Length).CopyTo(header,version==2?29:12); BitConverter.GetBytes(80+(int)directory.Length).CopyTo(header,version==2?33:16);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); using var file = File.Create(path); file.Write(header); file.Write(directory.ToArray());
        foreach (var _ in GameNameCache.Keys) file.Write(payload);
    }
    private static Task LocalNames192()
    {
        var root = Path.Combine(Path.GetTempPath(), "warno-names-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var game = Path.Combine(root,"Game"); var archive = Path.Combine(game,"Data/PC/100/ZZ_1.dat");
            Archive192(archive,"Old"); Archive192(Path.Combine(game,"Data/PC/100/200/ZZ_1.dat"),"测试名称 🚁");
            var empty = new byte[80]; "edat\u0002\0\0\0"u8.CopyTo(empty); BitConverter.GetBytes(80).CopyTo(empty,25);
            var emptyPath=Path.Combine(game,"Data/PC/150/ZZ_1.dat");Directory.CreateDirectory(Path.GetDirectoryName(emptyPath)!);File.WriteAllBytes(emptyPath,empty);
            var cachePath = Path.Combine(root,"cache.json"); var cache = new GameNameCache(cachePath);
            var snapshot = cache.Load(game); VanillaNames.Replace(snapshot.Names);
            TestAssert.Equal("测试名称 🚁",VanillaNames.Lookup("UNITS","TESTNAME01"),"补丁覆盖及UTF16代理对解码");
            var stamp = File.GetLastWriteTimeUtc(cachePath); cache.Load(game); TestAssert.Equal(stamp,File.GetLastWriteTimeUtc(cachePath),"未变化命中缓存不重写");
            TestAssert.Equal(0xCBF43926u,GameNameCache.Crc32(Encoding.ASCII.GetBytes("123456789")),"标准CRC32向量");
            var before = File.ReadAllBytes(cachePath); TestAssert.True(!before.Take(3).SequenceEqual(new byte[]{239,187,191}),"缓存无BOM");
            var bad = File.ReadAllBytes(archive); bad[^1] ^= 1; File.WriteAllBytes(archive,bad);
            bool rejected = false; try { cache.Load(game); } catch(InvalidDataException) { rejected=true; }
            TestAssert.True(rejected && before.SequenceEqual(File.ReadAllBytes(cachePath)),"坏包失败保留原缓存");
            Archive192(archive,"Base"); var patch=Path.Combine(game,"Data/PC/100/200/ZZ_1.dat"); Archive192(patch,"Updated",3);
            File.SetLastWriteTimeUtc(patch,DateTime.UtcNow.AddSeconds(2));
            TestAssert.Equal("Updated",cache.Load(game).Names["US/UNITS"].Values.Single(),"包更新触发重建");
            var goodV3=File.ReadAllBytes(patch);var badV3=(byte[])goodV3.Clone();badV3[^1]^=1;File.WriteAllBytes(patch,badV3);
            rejected=false;try{cache.Load(game,true);}catch(InvalidDataException){rejected=true;}TestAssert.True(rejected,"拒绝v3 CRC损坏");File.WriteAllBytes(patch,goodV3);
            TestAssert.Equal(game,cache.Load(null).Root,"游戏暂不可用仍可读取已有缓存");
            rejected=false;try { cache.Load(Path.Combine(root,"other")); }catch(InvalidDataException){rejected=true;}
            TestAssert.True(rejected,"切换来源不借用其他安装的缓存");
            foreach(var corrupt in new[]{new byte[2],Trad192("x")[..^1]})
            { rejected=false;try{GameNameCache.ReadTrad(corrupt);}catch(InvalidDataException){rejected=true;}TestAssert.True(rejected,"拒绝TRAD截断"); }
            var next=SyntheticNames(); VanillaNames.Replace(next); TestAssert.Equal("测试名称",VanillaNames.Chinese("UNITS","Test name"),"派生翻译");
            next=SyntheticNames(); next["SC/UNITS"][TokenKey192("TESTNAME01").ToString()]="新名称";VanillaNames.Replace(next);
            TestAssert.Equal("新名称",VanillaNames.Chinese("UNITS","Test name"),"替换缓存清除派生映射");
            VanillaNames.Replace(new());rejected=false;try{VanillaNames.RequireAvailable();}catch(InvalidOperationException){rejected=true;}
            TestAssert.True(rejected && VanillaNames.Lookup("UNITS","TESTNAME01") is null,"缺词典可回退但不假称token检查成功");
            TestAssert.True(!typeof(VanillaNames).Assembly.GetManifestResourceNames().Any(n=>n.Contains("vanilla-names")),"正式程序集无游戏名称资源");
        }
        finally { VanillaNames.Replace(SyntheticNames()); Directory.Delete(root,true); }
        return Task.CompletedTask;
    }
    private static Task BattalionTypes192()
    {
        foreach(var nl in new[]{"\n","\r\n"})
        {
            var text="TEntityDescriptor ( ModulesDescriptors = [ TBUCKToolAlternativeValues_TUIValueTextureNameFromTEugBMutableInteger ( Values = [\n\"Texture_STRATEGIC_RTS_H_Armor\", // \"fake\"\n\"Texture_STRATEGIC_Armor\", ] ) ] )".Replace("\n",nl);
            TestAssert.Equal("Armor",StrategicType.Read(text),"两种图标样式归并为同一类别");
            TestAssert.Equal("Custom",StrategicType.Read(text.Replace("Armor","Custom")),"未知类别保留");
        }
        TestAssert.Equal("",StrategicType.Read(null),"无棋子无猜测");return Task.CompletedTask;
    }
    private static void Verify192Ui(WarnoLiteModdingTool.App.ViewModels.MainViewModel vm, WarnoLiteModdingTool.App.MainWindow window)
    {
        vm.SelectedModule=vm.Modules.Single(m=>m.Key=="strategic");DrainDispatcher(window.Dispatcher);
        var source=FindVisualChildren<StrategicWorkspaceView>(window).Single();
        var grid=FindVisualChildren<DataGrid>(source).Single();
        var binding=System.Windows.Data.BindingOperations.GetBindingBase(grid,ItemsControl.ItemsSourceProperty)!;
        var record=vm.StrategicWorkspace!.Data.Records.First() with { DisplayName="Example",Country="POL",BattalionType="Armor",HasCustomName=true, Baseline=vm.StrategicWorkspace.Data.Records.First().Baseline with { Name="Example" } };
        grid.ItemsSource=new[]{record};DrainDispatcher(window.Dispatcher);
        TestAssert.Equal(3,grid.Columns.Count,"将军列表三列");
        var row=(DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
        var texts=FindVisualChildren<TextBlock>(row).Where(t=>t.Text is "Example" or "波兰" or "坦克").ToArray();
        TestAssert.Equal(3,texts.Length,"名称国家类型正确绑定");
        var centers=texts.Select(t=>t.TransformToAncestor(row).Transform(new Point(0,t.ActualHeight/2)).Y).ToArray();
        TestAssert.True(centers.Max()-centers.Min()<2,"三列实际垂直中心对齐");
        UiText.Current.SetLanguage("en");DrainDispatcher(window.Dispatcher);
        TestAssert.Equal("Type",grid.Columns[2].Header.ToString(),"类型表头随语言切换");
        TestAssert.True(FindVisualChildren<TextBlock>(row).Any(t=>t.Text=="Armor"),"英文营类型");
        UiText.Current.SetLanguage("zh-CN");DrainDispatcher(window.Dispatcher);SaveUiSnapshot(window,"strategic-1.9.2.png");
        System.Windows.Data.BindingOperations.SetBinding(grid,ItemsControl.ItemsSourceProperty,binding);
        vm.SelectedModule=vm.Modules.First(m=>m.Key=="units");DrainDispatcher(window.Dispatcher);
    }
}
