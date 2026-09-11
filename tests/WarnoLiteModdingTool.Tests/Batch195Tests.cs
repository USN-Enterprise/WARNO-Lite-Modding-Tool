using System.IO;
using System.Text;
using WarnoLiteModdingTool.Core.Divisions;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Images;
using WarnoLiteModdingTool.Core.Indexing;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;
namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static async Task DivisionIdentity195()
    {
        foreach(var nl in new[]{"\n","\r\n"})
        {
            var root=CreateTemporaryFixtureCopy("p5-division");
            try
            {
                var loc=Path.Combine(root,"GameData/Localisation/Test195");Directory.CreateDirectory(loc);File.WriteAllText(Path.Combine(loc,"LocalisationDicos.ndf"),"unnamed TLocalisationDicoResource ( DicoToken = ~/LocalisationConstantes/dico_units FileName = 'GameData:/Localisation/Test195/UNITS.csv' CanBeMissing = true )",new UTF8Encoding(false));File.WriteAllText(Path.Combine(loc,"UNITS.csv"),"TOKEN;REFTEXT\nP5DIV00001;原师\n",new UTF8Encoding(false));
                var file=Path.Combine(root,"GameData/Generated/Gameplay/Decks/Divisions.ndf");
                File.WriteAllText(file,File.ReadAllText(file).Replace("    TypeToken", "    EmblemTexture = \"Texture_Division_Emblem_Test\"\n    DescriptionHintTitleToken = 'P5DIV00001'\n    TypeToken").Replace("\r\n","\n").Replace("\n",nl),new UTF8Encoding(false));
                var textures=Path.Combine(root,"GameData/Generated/UserInterface/Textures");Directory.CreateDirectory(textures);
                File.WriteAllText(Path.Combine(textures,"DivisionTextures.ndf"),"Texture_Division_Emblem_Test is TUIResourceTexture_Common(FileName = \"GameData:/Assets/test.png\")\nTexture_Division_Emblem_Second is TUIResourceTexture_Common(FileName = \"GameData:/Assets/second.png\")",new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(root,UnitCreation.SerializerPath),"unnamed TDeckSerializerEntries ( DivisionIds = MAP [(Descriptor_Deck_Division_P5_Test, 21)] UnitIds = MAP [] )",new UTF8Encoding(false));
                var (context,units,data)=await LoadP5Async(root);var mother=data.Divisions.Single();var original=DivisionIdentity.Source(mother);var state=DivisionIdentity.New(data,mother,true,[]) with {Name="新师；\"测试\"",Emblem="Texture_Division_Emblem_Second"};
                TestAssert.Equal(22,state.SerializerId,"师编号取全部编号之后");var op=DivisionIdentity.Operation(mother,state);var next=DivisionIdentity.New(data,mother,true,[op]);TestAssert.Equal(23,next.SerializerId,"待创建师编号互不冲突");
                using var store=new DraftStore(root);await store.LoadAsync();await store.UpsertAsync(op);using(var reopened=new DraftStore(root)){await reopened.LoadAsync();TestAssert.Equal(state,DivisionIdentity.Read(reopened.Operations.Single()) with {Baselines=state.Baselines},"师草稿持久化");}
                var service=new UnitTransactionService();var before=Directory.GetFiles(Path.Combine(root,"GameData"),"*",SearchOption.AllDirectories).ToDictionary(p=>p,File.ReadAllBytes);
                var preview=await service.PrepareApplyAsync(root,store.Operations);foreach(var pair in before)TestAssert.True(pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key)),"创建预览不写正式文件");
                var csv=preview.Files.Single(f=>f.Kind==FormalTextFileKind.Csv);if(csv.Existed){File.SetAttributes(csv.FullPath,FileAttributes.ReadOnly);try{await TestAssert.ThrowsAsync<IOException>(()=>service.CommitApplyAsync(preview,store),"师创建失败回滚");}finally{File.SetAttributes(csv.FullPath,FileAttributes.Normal);}foreach(var pair in before)TestAssert.True(pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key)),"师创建回滚保持正式文件");preview=await service.PrepareApplyAsync(root,store.Operations);}
                await service.CommitApplyAsync(preview,store);var (_,_,after)=await LoadP5Async(root);var created=after.Divisions.Single(d=>d.Name==state.Id);TestAssert.Equal(state.Name,created.DisplayName,"师名从Mod CSV解析");TestAssert.Equal("Texture_Division_Emblem_Second",DivisionIdentity.Field(created,"EmblemTexture"),"师徽引用回读");TestAssert.Equal(state.Token,DivisionIdentity.Field(created,"DescriptionHintTitleToken"),"新师标题同步");TestAssert.Equal(original,DivisionIdentity.Source(after.Division(mother.Name)!),"师母版逐字保持");TestAssert.True(created.CanEdit,created.EditReason);TestAssert.True(created.DivisionRuleName!=mother.DivisionRuleName&&created.CostMatrixName!=mother.CostMatrixName&&created.DefaultDeckName!=mother.DefaultDeckName,"新师规则费用默认牌组独立");
                var rename=DivisionIdentity.New(after,created,false,[]) with {Name="改名后",Emblem="Texture_Division_Emblem_Test"};await store.UpsertAsync(DivisionIdentity.Operation(created,rename));await service.CommitApplyAsync(await service.PrepareApplyAsync(root,store.Operations),store);var (_,_,renamed)=await LoadP5Async(root);TestAssert.Equal("改名后",renamed.Division(state.Id)!.DisplayName,"已有师改名回读");TestAssert.Equal(original,DivisionIdentity.Source(renamed.Division(mother.Name)!),"改名不影响原师");
                TestAssert.Equal(DraftResolutionStatus.Conflict,DivisionIdentity.Resolve(renamed,op).Status,"重复创建拒绝");
                var invalid=DivisionIdentity.New(renamed,renamed.Division(state.Id)!,false,[]) with {Name="拒绝",Emblem="Texture_Missing"};await TestAssert.ThrowsAsync<TransactionValidationException>(()=>service.PrepareApplyAsync(root,[DivisionIdentity.Operation(renamed.Division(state.Id)!,invalid)]),"未知师徽拒绝");
                foreach(var candidate in preview.Files.Where(f=>f.Kind==FormalTextFileKind.Ndf))TestAssert.True(!candidate.CandidateBytes.Take(3).SequenceEqual(new byte[]{239,187,191}),"师NDF无BOM");var result=File.ReadAllText(file);TestAssert.True(nl=="\n"?!result.Contains('\r'):!result.Replace("\r\n","").Contains('\n'),"师原换行保持");
            }
            finally{DeleteTemporaryFixture(root);}
        }
    }
    private static Task ImageDecoder195()
    {
        var rgba=new byte[]{255,0,0,255,0,255,0,128,0,0,255,255,255,255,255,255};using var compressor=new ZstdSharp.Compressor();var zipped=compressor.Wrap(rgba).ToArray();var tgv=new byte[64+zipped.Length];BitConverter.GetBytes(3).CopyTo(tgv,0);BitConverter.GetBytes(2).CopyTo(tgv,8);BitConverter.GetBytes(2).CopyTo(tgv,12);BitConverter.GetBytes((ushort)1).CopyTo(tgv,24);Encoding.ASCII.GetBytes("A8B8G8R8_LIN").CopyTo(tgv,28);BitConverter.GetBytes(56).CopyTo(tgv,48);BitConverter.GetBytes(zipped.Length+8).CopyTo(tgv,52);Encoding.ASCII.GetBytes("ZSTD").CopyTo(tgv,56);BitConverter.GetBytes(16).CopyTo(tgv,60);zipped.CopyTo(tgv,64);var pixels=GameImageDecoder.ReadTgv(tgv);TestAssert.True(pixels.Rgba.SequenceEqual(rgba),"TGV颜色和透明度保持");var crop=GameImageDecoder.Crop(pixels,new("test","test",1,0,1,1));TestAssert.True(crop.Rgba.SequenceEqual(new byte[]{0,255,0,128}),"按坐标裁切透明像素");
        try{GameImageDecoder.Crop(pixels,new("test","test",2,0,1,1));throw new Exception("越界应拒绝");}catch(InvalidDataException){}
        TestAssert.True(ModTextures.LocalPath(Path.GetTempPath(),"GameData:/../../outside.png") is null,"Mod图片不得逃逸根目录");
        return Task.CompletedTask;
    }
    private static void Verify195Ui(System.Windows.Window main,string root,string divisionRoot)
    {
        var (context,units,data)=Task.Run(()=>LoadP5Async(divisionRoot)).GetAwaiter().GetResult();
        var wizard=new WarnoLiteModdingTool.App.Controls.DivisionIdentityWindow(data,[],true,data.Divisions.First());SaveUiSnapshot(wizard,"division-create195.png");Assert(FindVisualChildren<WarnoLiteModdingTool.App.Controls.SearchPicker>((System.Windows.DependencyObject)wizard.Content).Count()==2,"新师母版与师徽选择器");wizard.Close();
        var assets=Path.Combine(root,"GameData/Assets");Directory.CreateDirectory(assets);File.Copy(Path.Combine(root,"test-background.png"),Path.Combine(assets,"portrait.png"),true);var textures=Path.Combine(root,"GameData/Generated/UserInterface/Textures");Directory.CreateDirectory(textures);File.WriteAllText(Path.Combine(textures,"PortraitTest.ndf"),"SyntheticBank is TBUCKToolAdditionalTextureBank ( Textures = MAP [(\"Texture_Test_Portrait\", MAP [(~/ComponentState/Normal, TUIResourceTexture(FileName = \"GameData:/Assets/portrait.png\"))])])",new UTF8Encoding(false));
        var sourcePath=Path.Combine(root,"GameData/Generated/Gameplay/Gfx/UniteDescriptor.ndf");var before=File.ReadAllText(sourcePath);try
        {
            File.WriteAllText(sourcePath,before.Replace("TUnitUIModuleDescriptor", "TUnitUIModuleDescriptor").Replace("NameToken =", "ButtonTexture = 'Texture_Test_Portrait' NameToken ="),new UTF8Encoding(false));var (_,imageUnits,_)=Task.Run(()=>LoadP4Async(root)).GetAwaiter().GetResult();var unit=imageUnits.Units.First();var portrait=new WarnoLiteModdingTool.App.Controls.UnitPortrait{Root=root,Unit=unit};var dock=new System.Windows.Controls.DockPanel();System.Windows.Controls.DockPanel.SetDock(portrait,System.Windows.Controls.Dock.Right);dock.Children.Add(portrait);dock.Children.Add(new System.Windows.Controls.TextBlock{Text=unit.DisplayName});var host=new System.Windows.Window{Width=660,Height=180,Content=dock};host.Show();var deadline=DateTime.UtcNow.AddSeconds(5);while(portrait.Content is not System.Windows.Controls.Image&&DateTime.UtcNow<deadline){DrainDispatcher(main.Dispatcher);Thread.Sleep(5);}Assert(portrait.Content is System.Windows.Controls.Image,"宽栏显示Mod图片");SaveUiSnapshot(host,"portrait-wide195.png");host.Width=420;DrainDispatcher(main.Dispatcher);SaveUiSnapshot(host,"portrait-narrow195.png");Assert(portrait.Content is System.Windows.Controls.TextBlock,"窄栏只显示图片按钮");portrait.Unit=null;DrainDispatcher(main.Dispatcher);Assert(portrait.Content is not System.Windows.Controls.Image,"切换单位清除旧图");host.Close();
        }
        finally{File.WriteAllText(sourcePath,before,new UTF8Encoding(false));}
    }}
