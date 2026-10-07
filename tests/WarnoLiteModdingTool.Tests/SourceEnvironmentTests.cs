using System.IO;
using System.Diagnostics;
using System.Text.Json;
using WarnoLiteModdingTool.Core.SourceWorkspace;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static async Task SourceEnvironment()
    {
        var root=SourceFixture();var records=new List<object>();
        try
        {
            var relative=string.Join('/',Enumerable.Repeat("long-path-segment-0123456789",10))+"/All.ndf";
            var path=SourceFiles.Resolve(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,"Long is 1\n");
            Assert(path.Length>260 && SourceFiles.Discover(root).Files.Any(f=>f.Path==relative),"actual long NTFS path discovery");records.Add(new {Test="long-path",Characters=path.Length,Result="pass"});
            using var store=new DraftStore(root);await store.LoadAsync();var data=SourceFiles.Open(root,"Root.ndf");await store.UpsertAsync(SourceEditing.Operation("Root.ndf",data with{Text=data.Text+"// changed\n"}));
            var preview=await SourceEditing.Prepare(root,store.Operations,default);preview.SourceAcknowledged=true;
            var full=Path.Combine(root,"Root.ndf");File.SetAttributes(full,FileAttributes.ReadOnly);
            try { await SourceTransactions.Commit(preview,store);throw new Exception("Read-only file unexpectedly applied"); }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { records.Add(new{Test="read-only-target",Result="blocked",Type=ex.GetType().Name}); }
            finally { File.SetAttributes(full,FileAttributes.Normal); }
            Assert(File.ReadAllText(full)==data.Text && store.Operations.Count==1,"read-only failure preserves formal text and draft");
            using(var locked=new FileStream(full,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            {
                await TestAssert.ThrowsAsync<IOException>(()=>SourceEditing.Prepare(root,store.Operations,default),"exclusive file lock blocks candidate without changing draft");
            }
            records.Add(new{Test="exclusive-lock",Result="pass"});
            var text=new string('a',20_000_000);var samples=new List<double>();
            for(var round=0;round<20;round++)
            {
                using var cancel=new CancellationTokenSource();var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var task=Task.Run(()=>{started.SetResult();if(round%2==0)SourceSearch.Count(text,"a",true,cancel.Token);else _=new NdfSyntaxDocument(text,cancellationToken:cancel.Token);});
                await started.Task;await Task.Delay(2);var watch=Stopwatch.StartNew();cancel.Cancel();try{await task;}catch(OperationCanceledException){}samples.Add(watch.Elapsed.TotalMilliseconds);
            }
            records.Add(new{Test="cpu-cancellation",Samples=samples,P95=samples.Order().ElementAt(18),Max=samples.Max(),Note="Count search and tokenization, after cancel request; excludes blocking IO"});
            Directory.CreateDirectory("publish/qa-source-optimization");var report=JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true});File.WriteAllText("publish/qa-source-optimization/environment.json",report);Console.WriteLine(report);
        }
        finally {DeleteTemporaryFixture(root);}
    }
}
