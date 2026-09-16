using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Indexing;
using WarnoLiteModdingTool.Core.Divisions;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.App.Controls;
namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static async Task Emblem197BackgroundImage()
    {
        var root=Path.Combine(Path.GetTempPath(),"warno-emblem-thread-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"GameData/Assets"));
        try
        {
            var original=new byte[]{10,20,30,255,40,50,60,0,70,80,90,128,100,110,120,255};
            var source=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,original,8);
            File.WriteAllBytes(Path.Combine(root,"GameData/Assets/image.png"),EmblemEditorWindow.Encode(source));
            var loaded=await Task.Run(()=>LocalGameImages.LoadAsync(root,"GameData:/Assets/image.png",CancellationToken.None));
            Assert(loaded is not null&&loaded.IsFrozen,"后台图片已冻结，可供UI显示");
            var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var ui=new Thread(()=>{try{var encoded=EmblemEditorWindow.Encode(loaded!);var decoded=EmblemEditorWindow.Decode(encoded);var pixels=new byte[16];new FormatConvertedBitmap(decoded,PixelFormats.Bgra32,null,0).CopyPixels(pixels,8,0);Assert(pixels.SequenceEqual(original),"跨线程导入保持像素及透明度");completion.SetResult();}catch(Exception ex){completion.SetException(ex);}});
            ui.SetApartmentState(ApartmentState.STA);ui.Start();await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));ui.Join();
        }
        finally{Directory.Delete(root,true);}
    }
    private static async Task Experience197()
    {
        foreach(var nl in new[]{"\n","\r\n"})
        {
            var root=CreateTemporaryFixtureCopy("p2-unit-complete");try{
                var file=Path.Combine(root,"GameData/Generated/Gameplay/Gfx/UniteDescriptor.ndf");var text=File.ReadAllText(file).Replace("ModulesDescriptors = [","ModulesDescriptors = [\n TExperienceModuleDescriptor(ExperienceLevelsPackDescriptor = ~/XP_A ExperienceMultiplierBonusOnKill = 1),").Replace("\r\n","\n").Replace("\n",nl);File.WriteAllText(file,text,new UTF8Encoding(false));
                string Pack(string name)=>$"export {name} is TExperienceLevelsPackDescriptor ( ExperienceLevelsDescriptors = ["+string.Join(",",Enumerable.Range(0,4).Select(i=>$"TExperienceLevelDescriptor(ThresholdAdditionalValue = 0 ThresholdPriceMultiplier = {i} LevelEffectsPacks = [$/GFX/EffectCapacity/Effect197])"))+"] )";
                var xp=Path.Combine(root,"GameData/Generated/Gameplay/Gfx/CustomXp.ndf");File.WriteAllText(xp,Pack("XP_A")+nl+Pack("XP_B")+nl+"export Effect197 is TEffectsPackDescriptor(EffectsDescriptors = [])",new UTF8Encoding(false));
                var context=new ModProjectDetector().Detect(root);var data=await new UnitProjectLoader().LoadAsync(context,await new ProjectIndexer().IndexAsync(context));var unit=data.Units.First();var field=unit.Field("experience.type")!;Assert(field.Choices.Count==2,"未引用的经验配置也可选");var choice=field.Choices.Single(c=>c.RawValue=="~/XP_B");var op=CreateFieldDraft(unit,field,choice.Display,choice.RawValue);using var store=new DraftStore(root);await store.LoadAsync();await store.UpsertAsync(op);var service=new UnitTransactionService();var preview=await service.PrepareApplyAsync(root,store.Operations);
                var candidate=Encoding.UTF8.GetString(preview.Files.Single(f=>f.RelativePath.EndsWith("UniteDescriptor.ndf")).CandidateBytes);Assert(candidate.Count(c=>c=='\n')==text.Count(c=>c=='\n'),"原换行保持");Assert(candidate.Contains("~/XP_B"),"目标引用进入预览");
                File.AppendAllText(xp,nl+"// changed");await TestAssert.ThrowsAsync<TransactionValidationException>(()=>service.CommitApplyAsync(preview,store),"经验依赖预览后变化拒绝");Assert(File.ReadAllText(file)==text,"拒绝未写正式文件");
                preview=await service.PrepareApplyAsync(root,store.Operations);await service.CommitApplyAsync(preview,store);Assert(File.ReadAllText(file)==candidate,"正式只替换引用");
                File.AppendAllText(xp,nl+Pack("XP_B"));Assert(ExperienceCatalog.Load(root).Packs.Single(p=>p.Name=="XP_B").Error is not null,"重复配置拒绝");
            }finally{Directory.Delete(root,true);}
        }
    }
    private static async Task Emblem197()
    {
        var root=CreateTemporaryFixtureCopy("p5-division");try{
            var file=Path.Combine(root,"GameData/Generated/Gameplay/Decks/Divisions.ndf");File.WriteAllText(file,File.ReadAllText(file).Replace("    TypeToken","    EmblemTexture = \"Texture_Division_Emblem_Test\"\n    TypeToken"),new UTF8Encoding(false));
            var path="GameData/Generated/UserInterface/Textures/DivisionTextures.ndf";Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root,path))!);File.WriteAllText(Path.Combine(root,path),"Texture_Division_Emblem_Test is TUIResourceTexture_Common(FileName = 'GameData:/Assets/base.png')",new UTF8Encoding(false));
            var (_,_,data)=await LoadP5Async(root);var mother=data.Divisions.First();var state=DivisionIdentity.New(data,mother,false,[]);var png="iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==";var asset=new EmblemAsset("Texture_Division_Emblem_mod_test197",png,path);state=state with {Emblem=asset.Key,Asset=asset};var op=DivisionIdentity.Operation(mother,state);
            using var store=new DraftStore(root);await store.LoadAsync();await store.UpsertAsync(op);var service=new UnitTransactionService();var preview=await service.PrepareApplyAsync(root,store.Operations);Assert(preview.Files.Count(f=>f.Kind==FormalTextFileKind.Binary)==1,"PNG纳入同一事务");Assert(!preview.Files.Any(f=>f.Kind==FormalTextFileKind.Csv),"只换图不修改名称CSV");var binary=preview.Files.Single(f=>f.Kind==FormalTextFileKind.Binary);await service.CommitApplyAsync(preview,store);Assert(File.ReadAllBytes(binary.FullPath).SequenceEqual(Convert.FromBase64String(png)),"PNG字节保持");Assert(File.ReadAllText(file).Contains(asset.Key),"师引用写入");Assert(File.ReadAllText(Path.Combine(root,path)).Contains("GameData:/Assets/2D/"),"资源路径合法");
            var restore=service.PrepareRestore(root,preview.BackupId);await service.CommitRestoreAsync(restore);Assert(!File.Exists(binary.FullPath),"恢复移除新增PNG");Assert(!File.ReadAllText(file).Contains(asset.Key),"恢复师引用");
            await store.UpsertAsync(op);var failurePreview=await service.PrepareApplyAsync(root,store.Operations);Directory.CreateDirectory(binary.FullPath);var failed=false;
            try{await service.CommitApplyAsync(failurePreview,store);}catch(Exception){failed=true;}
            Assert(failed,"PNG路径被占用拒绝提交");foreach(var change in failurePreview.Files.Where(f=>f.Existed&&f.Kind!=FormalTextFileKind.Log))Assert(File.ReadAllBytes(change.FullPath).SequenceEqual(change.OriginalBytes),"图片写入失败恢复所有正式NDF");Assert(store.Operations.Count==1,"图片失败保留草稿");
        }finally{Directory.Delete(root,true);}
    }
    private static void Verify197Ui(Window main)
    {
        var grid=new System.Windows.Controls.Primitives.UniformGrid{Columns=5};
        for(var i=0;i<10;i++){var image=EmblemTemplates.Render(i,i>=8?"303":"35",i%4);Assert(image.PixelWidth==512,"模板尺寸");var stack=new StackPanel();stack.Children.Add(new Image{Source=image,Width=140,Height=140});stack.Children.Add(new TextBlock{Text=EmblemTemplates.Names[i]});grid.Children.Add(stack);}
        var gallery=new Window{Width=800,Height=440,Content=grid,Background=Brushes.LightGray};SaveUiSnapshot(gallery,"197-templates.png");gallery.Close();
        var editor=new EmblemEditorWindow(templateMode:true);editor.Show();DrainDispatcher(main.Dispatcher);SaveUiSnapshot(editor,"197-emblem-editor.png");
        var type=typeof(EmblemEditorWindow);type.GetField("_selection",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(editor,new Rect(100,100,300,300));var combo=(ComboBox)type.GetField("_tool",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(editor)!;combo.SelectedIndex=1;type.GetField("_selection",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(editor,new Rect(100,100,300,300));type.GetMethod("ApplySelection",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(editor,[1]);
        var current=(BitmapSource)type.GetField("_current",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(editor)!;var pixels=new byte[512*512*4];new FormatConvertedBitmap(current,PixelFormats.Bgra32,null,0).CopyPixels(pixels,512*4,0);Assert(pixels[3]==0,"圈外透明");SaveUiSnapshot(editor,"197-circle.png");
        var recipe=(string)type.GetMethod("BuildRecipe",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(editor,null)!;
        var reopened=new EmblemEditorWindow(EmblemEditorWindow.Encode(current),recipe);var again=(BitmapSource)type.GetField("_current",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(reopened)!;Assert(EmblemEditorWindow.Encode(current).SequenceEqual(EmblemEditorWindow.Encode(again)),"草稿重开保持透明处理");reopened.Close();
        var solid=BitmapSource.Create(8,8,96,96,PixelFormats.Bgra32,null,Enumerable.Repeat((byte)255,8*8*4).ToArray(),32);
        var inside=EmblemEditorWindow.Process(solid,new(2,.25,.25,.5,.5));var buf=new byte[8*8*4];inside.CopyPixels(buf,32,0);Assert(buf[(4*8+4)*4+3]==0&&buf[3]==255,"圈内透明保留外部");
        var crop=EmblemEditorWindow.Process(solid,new(0,.25,.25,.5,.5));Assert(crop.PixelWidth==4&&crop.PixelHeight==4,"矩形裁剪实际尺寸");editor.Close();
    }
    private static void Verify197Panels(WarnoLiteModdingTool.App.MainWindow main)
    {
        var prefs=new WarnoLiteModdingTool.App.Settings.UiPreferences(FloatingPanels:true);
        FloatingPanels.Install(main,()=>prefs);main.Show();DrainDispatcher(main.Dispatcher);
        var title=FindVisualChildren<TextBlock>(main).First(t=>Equals(t.ToolTip, "双击标题独立弹出")&&t.IsVisible);
        var slot=(DockPanel)title.Parent;var view=slot.Children.OfType<UserControl>().Single();var context=view.DataContext;
        var click=new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0,System.Windows.Input.MouseButton.Left){RoutedEvent=UIElement.MouseLeftButtonDownEvent};
        typeof(System.Windows.Input.MouseButtonEventArgs).GetProperty("ClickCount")!.SetValue(click,2);title.RaiseEvent(click);DrainDispatcher(main.Dispatcher);
        var detached=main.OwnedWindows.Cast<Window>().Single(w=>ReferenceEquals(w.Content,view));Assert(ReferenceEquals(view.DataContext,context),"大面板沿用上下文");Assert(FloatingPanels.Host(view)==main,"独立页仍找到主窗口");SaveUiSnapshot(detached,"197-floating-large.png");detached.Close();Assert(view.Parent==slot,"关闭大面板归位");
        prefs=prefs with {FloatSmallPanels=true};
        var content=new TextBlock{Text="197 group"};var group=new Expander{Header="197 group",Content=content,DataContext=new object(),IsExpanded=true};slot.Children.Add(group);main.UpdateLayout();
        var smallClick=new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0,System.Windows.Input.MouseButton.Left){RoutedEvent=System.Windows.Input.Mouse.PreviewMouseDownEvent,Source=group};typeof(System.Windows.Input.MouseButtonEventArgs).GetProperty("ClickCount")!.SetValue(smallClick,2);group.RaiseEvent(smallClick);DrainDispatcher(main.Dispatcher);
        Assert(main.OwnedWindows.Cast<Window>().Any(w=>ReferenceEquals(w.Content,content)),"小分组可弹出");group.DataContext=new object();DrainDispatcher(main.Dispatcher);Assert(ReferenceEquals(group.Content,content),"切换对象小分组归位");slot.Children.Remove(group);main.Hide();
    }

}
