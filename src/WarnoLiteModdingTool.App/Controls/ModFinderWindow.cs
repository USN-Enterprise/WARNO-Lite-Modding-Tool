using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.Core.Projects;

namespace WarnoLiteModdingTool.App.Controls;

public static class ModFinder
{
    public static IReadOnlyList<string> DistinctRoots(IEnumerable<string> paths) => paths
        .Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase).ToArray();

    public static IReadOnlyList<string> FindRoots(string? saved, CancellationToken cancellationToken)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(saved)) roots.Add(Path.GetFullPath(saved!));
        foreach (var key in new[] { @"HKEY_CURRENT_USER\Software\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam" })
            foreach (var value in new[] { "SteamPath", "InstallPath" })
                try { if (Registry.GetValue(key,value,null) is string path && Directory.Exists(path)) libraries.Add(path); } catch (System.Security.SecurityException) { }
        libraries.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam"));
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        { cancellationToken.ThrowIfCancellationRequested(); foreach (var folder in new[] { "SteamLibrary", "Steam", "Games/Steam" }) libraries.Add(Path.Combine(drive.RootDirectory.FullName, folder)); }
        foreach (var library in libraries.ToArray())
            foreach (var config in new[] { "steamapps/libraryfolders.vdf", "config/libraryfolders.vdf" })
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { var file = Path.Combine(library,config); if (File.Exists(file)) foreach (Match m in Regex.Matches(File.ReadAllText(file), "\"(?:path|[0-9]+)\"\\s+\"([^\"]+)\"")) { var path=m.Groups[1].Value.Replace("\\\\","\\"); if(Path.IsPathRooted(path)) libraries.Add(path); } }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        foreach (var library in libraries)
        {
            cancellationToken.ThrowIfCancellationRequested(); var folder = "WARNO";
            try { var manifest=Path.Combine(library,"steamapps/appmanifest_1611600.acf"); if(File.Exists(manifest)) { var m=Regex.Match(File.ReadAllText(manifest),"\"installdir\"\\s+\"([^\"]+)\""); if(m.Success && Path.GetFileName(m.Groups[1].Value)==m.Groups[1].Value) folder=m.Groups[1].Value; } } catch (Exception ex) when(ex is IOException or UnauthorizedAccessException) { }
            var game=Path.Combine(library,"steamapps/common",folder); if(File.Exists(Path.Combine(game,"WARNO.exe"))) roots.Add(Path.Combine(game,"Mods"));
        }
        return DistinctRoots(roots);
    }
}

public sealed class ModFinderWindow : Window
{
    public string? SelectedPath { get; private set; }
    public ModFinderWindow(string? saved)
    {
        UiText.Bind(this,TitleProperty,"自动查找 Mod"); Width=780; Height=450; WindowStartupLocation=WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"BackgroundBrush"); SetResourceReference(ForegroundProperty,"TextBrush");
        var panel=new DockPanel { Margin=new Thickness(16) }; Content=panel;
        var status=new TextBlock { Text=UiText.T("正在查找 WARNO Mod…"), TextWrapping=TextWrapping.Wrap }; DockPanel.SetDock(status,Dock.Top); panel.Children.Add(status);
        var buttons=new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(0,12,0,0) }; DockPanel.SetDock(buttons,Dock.Bottom);panel.Children.Add(buttons);
        var list=new ListBox { Margin=new Thickness(0,12,0,0) };panel.Children.Add(list);
        void Button(string name,Action action) { var b=new Button { Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,8,0) };UiText.Bind(b,ContentControl.ContentProperty,name);b.Click+=(_,_)=>action();buttons.Children.Add(b); }
        Button("打开",()=> { if(list.SelectedItem is ModChoice item) { SelectedPath=item.Path;new WarnoModsRootStore().Save(Path.GetDirectoryName(item.Path)!);DialogResult=true; } });
        Button("手动选择文件夹",()=> { var d=new OpenFolderDialog { InitialDirectory=saved };if(d.ShowDialog(this)==true){SelectedPath=d.FolderName;DialogResult=true;} });
        Button("取消",Close);
        var cancel=new CancellationTokenSource();Closed+=(_,_)=>cancel.Cancel();
        Loaded+=async(_,_)=> {
            try {
                var result=await Task.Run(()=> { var roots=ModFinder.FindRoots(saved,cancel.Token);var mods=new List<ModChoice>();foreach(var root in roots){cancel.Token.ThrowIfCancellationRequested();if(!Directory.Exists(root))continue;try { foreach(var child in Directory.EnumerateDirectories(root)){cancel.Token.ThrowIfCancellationRequested();if(new ModProjectDetector().Detect(child).Modules.Any(m=>m.CanScan))mods.Add(new(Path.GetFileName(child),child));} }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){} }return(roots,mods); },cancel.Token);
                if(cancel.IsCancellationRequested)return;list.ItemsSource=result.mods;
                if(result.roots.Count>0)new WarnoModsRootStore().Save(result.roots[0]);
                status.Text=result.mods.Count>0?UiText.T($"找到 {result.mods.Count} 个 Mod，请选择后打开"):result.roots.Count>0?UiText.T("已找到 WARNO，但没有可编辑的 Mod。关闭后可使用“创建新 Mod”。"):UiText.T("未找到 WARNO，请手动选择 Mod 文件夹。");
            } catch(OperationCanceledException){} catch(Exception ex){if(!cancel.IsCancellationRequested)status.Text=ex.Message;}
        };
    }
    private sealed record ModChoice(string Name,string Path) { public override string ToString()=>Name+"\n"+Path; }
}
