using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WarnoLiteModdingTool.App.Controls;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Images;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private const string Picture1910Path="GameData/Custom/UI/UnitImages.ndf";
    private const string Picture1910Key="Custom_SharedPortrait";
    private static byte[] Png1910()
    {
        var pixels=Enumerable.Range(0,12*6).SelectMany(i=>new byte[]{(byte)(i*3),90,200,(byte)(i%4==0?0:255)}).ToArray();
        return EmblemEditorWindow.Encode(BitmapSource.Create(12,6,96,96,PixelFormats.Bgra32,null,pixels,48));
    }
    private static string Fixture1910(string nl="\n")
    {
        var root=Fixture199(nl);var unit=Path.Combine(root,Unit199Path);
        File.WriteAllText(unit,File.ReadAllText(unit).Replace("NameToken =", "ButtonTexture = '"+Picture1910Key+"'"+nl+"            NameToken ="),new UTF8Encoding(false));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root,Picture1910Path))!);
        File.WriteAllText(Path.Combine(root,Picture1910Path),("// preserve outside text\nPortraitBank is TBUCKToolAdditionalTextureBank\n(\n    Textures = MAP [\n        (\""+Picture1910Key+"\", MAP [(~/ComponentState/Normal, TUIResourceTexture(FileName = 'GameData:/Assets/base1910.png'))]),\n        (\"Custom_UnusedPortrait\", MAP [(~/ComponentState/Normal, TUIResourceTexture(FileName = 'GameData:/Assets/other1910.png'))]) /* end comment */\n    ]\n)\n// trailing keep\n").Replace("\n",nl),new UTF8Encoding(false));
        Directory.CreateDirectory(Path.Combine(root,"GameData/Assets"));File.WriteAllBytes(Path.Combine(root,"GameData/Assets/base1910.png"),Png1910());File.WriteAllBytes(Path.Combine(root,"GameData/Assets/other1910.png"),Png1910());
        return root;
    }
    private static async Task Unit1910Existing()
    {
        foreach(var nl in new[]{"\n","\r\n"})
        {
            var root=Fixture1910(nl);
            try
            {
                var (_,units,_)=await LoadP4Async(root);var unit=units.Units.First();var other=units.Units.Last();var body=UnitCreation.Source(other);
                var source=File.ReadAllText(Path.Combine(root,Unit199Path));var catalog=UnitPictures.Catalog(root);
                Assert(catalog.Count==2 && ModTextures.Read(root).Count==2,"发现当前Mod非标准目录的图片库");
                var target=UnitPictures.Require(catalog,"Custom_UnusedPortrait");var op=UnitPictures.Operation(unit,new(target.Key,target));
                using var store=new DraftStore(root);await store.LoadAsync();await store.UpsertAsync(op);
                var service=new UnitTransactionService();var preview=await service.PrepareApplyAsync(root,store.Operations);
                Assert(!preview.Files.Any(f=>f.Kind is FormalTextFileKind.Binary or FormalTextFileKind.Csv),"已有图只改单位引用");
                await service.CommitApplyAsync(preview,store);
                var (_,reloaded,_)=await LoadP4Async(root);
                Assert(UnitCreation.Source(reloaded.Units.Single(u=>u.Name==other.Name))==body,"共享原图的另一单位保持");
                Assert(UnitCreation.Source(reloaded.Units.Single(u=>u.Name==unit.Name))==UnitPictures.Apply(UnitCreation.Source(unit),target.Key),"仅目标字段变化");
                await service.CommitRestoreAsync(service.PrepareRestore(root,preview.BackupId));
                Assert(File.ReadAllText(Path.Combine(root,Unit199Path))==source,"恢复引用及原文换行");
            }
            finally{DeleteTemporaryFixture(root);}
        }
    }
    private static async Task Unit1910ImportRestore()
    {
        foreach(var nl in new[]{"\n","\r\n"})
        {
            var root=Fixture1910(nl);
            try
            {
                var (_,units,_)=await LoadP4Async(root);var unit=units.Units.First();var catalog=UnitPictures.Catalog(root);var template=UnitPictures.Require(catalog,Picture1910Key);
                var original=File.ReadAllBytes(Path.Combine(root,Picture1910Path));var png=Png1910();var state=UnitPictures.Import(template,png,"saved recipe");var op=UnitPictures.Operation(unit,state);
                using var store=new DraftStore(root);await store.LoadAsync();await store.UpsertAsync(op);
                using(var reopened=new DraftStore(root)){await reopened.LoadAsync();Assert(UnitPictures.Read(reopened.Operations.Single()).PngBase64==state.PngBase64,"草稿携带图像，无外部路径依赖");}
                var rename=UnitIdentityEditing.Operation(unit,unit.Name+"_picture",false);await store.UpsertAsync(rename);
                var service=new UnitTransactionService();var preview=await service.PrepareApplyAsync(root,[op]);
                Assert(preview.Operations.Count==2,"改名与图片草稿联合应用");
                var asset=preview.Files.Single(f=>f.Kind==FormalTextFileKind.Binary);Assert(!File.Exists(asset.FullPath),"预览不落正式PNG");
                var candidate=Encoding.UTF8.GetString(preview.Files.Single(f=>f.RelativePath==Picture1910Path).CandidateBytes);
                Assert(candidate.Contains("/* end comment */") && candidate.EndsWith("// trailing keep"+nl),"新增映射保持注释及换行");
                await service.CommitApplyAsync(preview,store);Assert(File.ReadAllBytes(asset.FullPath).SequenceEqual(png),"提交PNG保持原字节");
                Assert(ModTextures.Read(root).Single(t=>t.Key==state.Key).Source=="GameData:/"+asset.RelativePath[9..],"最终资源链一致");
                var (_,after,_)=await LoadP4Async(root);Assert(ModTextures.UnitKey(after.Units.Single(u=>u.Name==unit.Name+"_picture"))==state.Key,"改名和图片组合成功");
                Assert(ModTextures.UnitKey(after.Units.Single(u=>u.Name==units.Units.Last().Name))==Picture1910Key,"共享原图单位保持");
                await service.CommitRestoreAsync(service.PrepareRestore(root,preview.BackupId));Assert(!File.Exists(asset.FullPath) && File.ReadAllBytes(Path.Combine(root,Picture1910Path)).SequenceEqual(original),"恢复移除PNG和新增映射");
                await store.UpsertAsync(op);preview=await service.PrepareApplyAsync(root,store.Operations);Directory.CreateDirectory(asset.FullPath);
                var failed=false;try{await service.CommitApplyAsync(preview,store);}catch(Exception){failed=true;}
                Assert(failed && store.Operations.Count==1,"PNG路径占用失败保留草稿");
                foreach(var file in preview.Files.Where(f=>f.Existed && f.Kind!=FormalTextFileKind.Log))Assert(File.ReadAllBytes(file.FullPath).SequenceEqual(file.OriginalBytes),"失败恢复全部已有正式文件");
            }
            finally{DeleteTemporaryFixture(root);}
        }
    }
    private static async Task Unit1910Creation()
    {
        var root=Fixture1910();try
        {
            var (_,units,_)=await LoadP4Async(root);var mother=units.Units.First();var template=UnitPictures.Require(UnitPictures.Catalog(root),Picture1910Key);
            var picture=UnitPictures.Import(template,Png1910());var state=UnitCreation.New(mother,units,[]) with {Picture=picture};
            var projected=UnitCreation.Project(mother,state,UnitCreation.Source(mother));Assert(ModTextures.UnitKey(projected)==picture.Key,"待创建预览使用新图片键");
            using var store=new DraftStore(root);await store.LoadAsync();var op=UnitCreation.Operation(mother,state);await store.UpsertAsync(op);
            state=state with{Id=state.Id+"_renamed"};await UnitDraftLinks.ReplaceCreationAsync(store,op,UnitCreation.Operation(mother,state));
            var second=units.Units.Last();var secondPicture=UnitPictures.Import(template,Png1910());await store.UpsertAsync(UnitPictures.Operation(second,secondPicture));
            var service=new UnitTransactionService();var preview=await service.PrepareApplyAsync(root,store.Operations);
            Assert(preview.Files.Count(f=>f.Kind==FormalTextFileKind.Binary)==2,"同批两张新图");await service.CommitApplyAsync(preview,store);
            var (_,after,_)=await LoadP4Async(root);
            Assert(ModTextures.UnitKey(after.Units.Single(u=>u.Name==state.Id))==picture.Key,"新建并改名后使用草稿图片");
            Assert(ModTextures.UnitKey(after.Units.Single(u=>u.Name==mother.Name))==Picture1910Key,"母版保持原图");
            Assert(UnitPictures.Catalog(root).Count==4,"同文件两个映射合并保留");
        }finally{DeleteTemporaryFixture(root);}
    }
    private static async Task Unit1910Guards()
    {
        var root=Fixture1910();try
        {
            var (_,units,_)=await LoadP4Async(root);var unit=units.Units.First();var template=UnitPictures.Require(UnitPictures.Catalog(root),Picture1910Key);
            using var store=new DraftStore(root);await store.LoadAsync();var op=UnitPictures.Operation(unit,new(template.Key,template));await store.UpsertAsync(op);var service=new UnitTransactionService();
            var preview=await service.PrepareApplyAsync(root,store.Operations);File.WriteAllBytes(Path.Combine(root,"GameData/Assets/base1910.png"),[1,2]);
            await TestAssert.ThrowsAsync<TransactionValidationException>(()=>service.CommitApplyAsync(preview,store),"预览后本地图变更拒绝");
            preview=await service.PrepareApplyAsync(root,store.Operations);var added=Path.Combine(root,"GameData/duplicate.ndf");File.Copy(Path.Combine(root,Picture1910Path),added);
            await TestAssert.ThrowsAsync<TransactionValidationException>(()=>service.CommitApplyAsync(preview,store),"新增重复纹理声明拒绝");File.Delete(added);
            var state=UnitPictures.Import(template,Png1910());op=UnitPictures.Operation(unit,state);await store.UpsertAsync(op);
            var path=Path.Combine(root,UnitPictures.AssetPath(state.Key));Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,[4,5]);
            await TestAssert.ThrowsAsync<TransactionValidationException>(()=>service.PrepareApplyAsync(root,store.Operations),"已有PNG不得覆盖");File.Delete(path);
            File.AppendAllText(Path.Combine(root,Picture1910Path),"\nDuplicate is TBUCKToolAdditionalTextureBank(Textures = MAP [(\""+state.Key+"\", MAP [])])");
            await TestAssert.ThrowsAsync<TransactionValidationException>(()=>service.PrepareApplyAsync(root,store.Operations),"不支持的纹理仍占用新键");
            var rejected=false;try{UnitPictures.Import(template with{States="MAP [(~/ComponentState/Normal,TUIResourceTexture(FileName='x')), (~/ComponentState/Selected,TUIResourceTexture(FileName='y'))]"},Png1910());}catch(InvalidDataException){rejected=true;}Assert(rejected,"自定义图不猜测多状态映射");
            rejected=false;try{PngAssets.Decode(Convert.ToBase64String(Png1910()[..30]));}catch(InvalidDataException){rejected=true;}Assert(rejected,"截断PNG拒绝");
        }finally{DeleteTemporaryFixture(root);}
    }
    private static void Verify1910Ui(Window main)
    {
        var root=Fixture1910();try
        {
            var canvas=BitmapSource.Create(300,153,96,96,PixelFormats.Bgra32,null,Enumerable.Range(0,300*153).SelectMany(i=>new byte[]{(byte)(i%300*255/300),130,50,(byte)(i%300<25?0:255)}).ToArray(),1200);
            var uiPng=EmblemEditorWindow.Encode(canvas);File.WriteAllBytes(Path.Combine(root,"GameData/Assets/base1910.png"),uiPng);File.WriteAllBytes(Path.Combine(root,"GameData/Assets/other1910.png"),uiPng);
            var load=LoadP4Async(root);RunWithDispatcher(load,main.Dispatcher);var units=load.Result.Item2;var unit=units.Units.First();var catalog=UnitPictures.Catalog(root);
            foreach(var language in new[]{"zh-CN","en"})
            {
                UiText.Current.SetLanguage(language);var chooser=new UnitPictureWindow(root,unit,units.Units,catalog,null);chooser.Show();DrainDispatcher(main.Dispatcher);
                var list=FindVisualChildren<ListBox>(chooser).Single();list.SelectedIndex=1;DrainDispatcher(main.Dispatcher);
                Assert(chooser.State is not null,"选择已有图更新预览状态");
                var loadedUntil=DateTime.UtcNow.AddSeconds(5);
                while(!FindVisualChildren<Image>(chooser).Any(i=>i.Source is not null) && DateTime.UtcNow<loadedUntil){DrainDispatcher(main.Dispatcher);Thread.Sleep(10);}
                Assert(FindVisualChildren<Image>(chooser).Any(i=>i.Source is not null),"已有图异步预览完成");
                SaveUiSnapshot(chooser,"1910-picture-"+language+".png");chooser.Close();
                var editor=new EmblemEditorWindow(uiPng,unitPicture:true);editor.Show();DrainDispatcher(main.Dispatcher);
                Assert(!FindVisualChildren<TextBlock>(editor).Any(t=>t.IsVisible && t.Text==UiText.T("固定模板")),"单位图片不显示师徽模板");
                Assert(editor.Title==(language=="en"?"Unit picture editor":"单位图片编辑"),"编辑器标题按入口和语言显示");
                if(language=="en")Assert(FindVisualChildren<Button>(editor).Any(b=>Equals(b.Content,"Import custom image")),"英文按钮实际翻译");SaveUiSnapshot(editor,"1910-editor-"+language+".png");editor.Close();
            }
            using(var store=new DraftStore(root))
            {
                var opened=store.LoadAsync();RunWithDispatcher(opened,main.Dispatcher);
                var vm=new WarnoLiteModdingTool.App.ViewModels.Units.UnitWorkspaceViewModel(units,load.Result.Item3,null,store,opened.Result,_=>{},()=>Task.CompletedTask);
                vm.SelectedUnit=vm.Units.First();
                var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(40)};
                timer.Tick+=(_,_)=>
                {
                    var dialog=main.OwnedWindows.Cast<Window>().OfType<UnitPictureWindow>().FirstOrDefault();if(dialog is null)return;
                    timer.Stop();FindVisualChildren<ListBox>(dialog).Single().SelectedIndex=1;
                    FindVisualChildren<Button>(dialog).Single(b=>Equals(b.Tag,"保存草稿")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                };
                timer.Start();try{RunWithDispatcher(vm.EditPictureAsync(main),main.Dispatcher);}finally{timer.Stop();}
                Assert(store.Operations.Single().TargetKind==DraftTargetKind.UnitPicture && vm.SelectedPicture?.Key=="Custom_UnusedPortrait","实际窗口保存接入草稿和标题预览");
                RunWithDispatcher(store.RemoveAsync(store.Operations.Single().Id),main.Dispatcher);vm.RefreshExternalDraftState();Assert(vm.SelectedPicture is null,"撤销草稿回到正式图片");
            }
            var picture=UnitPictures.Import(UnitPictures.Require(catalog,Picture1910Key),Png1910());var portrait=new UnitPortrait{Unit=unit,Root=root,Picture=picture};
            var host=new Window{Width=700,Height=260,Content=new Grid{Children={portrait}}};host.Show();
            var until=DateTime.UtcNow.AddSeconds(5);while(portrait.Content is not Image && DateTime.UtcNow<until){DrainDispatcher(main.Dispatcher);Thread.Sleep(10);}
            Assert(portrait.Content is Image,"单位标题可预览草稿PNG");host.Close();UiText.Current.SetLanguage("zh-CN");
        }finally{DeleteTemporaryFixture(root);}
    }
}
