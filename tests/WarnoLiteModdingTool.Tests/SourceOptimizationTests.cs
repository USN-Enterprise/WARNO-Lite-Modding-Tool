using System.IO;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using WarnoLiteModdingTool.Core.SourceWorkspace;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static async Task SourceOptimization()
    {
        var insert = SourceDiff.Compare("a\nb\nc\n", "a\nnew\nb\nc\n");
        Assert(insert.Blocks.Count == 1 && insert.Blocks[0] == new SourceDiffBlock(2,0,2,1),"insert aligns remainder");
        var random = new Random(42);
        for(var run=0;run<300;run++)
        {
            var before=Enumerable.Range(0,random.Next(1,70)).Select(_=>random.Next(8).ToString()).ToArray();
            var after=before.ToList(); for(var i=0;i<8;i++) { var p=random.Next(after.Count+1); if(p<after.Count && random.Next(2)==0)after.RemoveAt(p);else after.Insert(p,random.Next(10).ToString()); }
            var a=string.Join('\n',before);var b=string.Join('\n',after); var diff=SourceDiff.Compare(a,b); var result=before.ToList();
            foreach(var block in diff.Blocks.Reverse()) { result.RemoveRange(block.BeforeLine-1,block.BeforeCount); result.InsertRange(block.BeforeLine-1,b.Split('\n').Skip(block.AfterLine-1).Take(block.AfterCount)); }
            Assert(string.Join('\n',result)==b,"diff reconstructs exact changed line sequence");
        }
        var dense=string.Concat(Enumerable.Repeat("Value = 1\n",1203)); var count=SourceSearch.Count(dense,"Value",true,default);
        var files=new[]{new SourceSearchFile("Custom.ndf",dense,count)};
        var all=Enumerable.Range(0,3).SelectMany(i=>SourceSearch.Page(files,"Value",true,i*500,500,default)).ToArray();
        Assert(count==1203 && all.Length==count && all.Select(h=>h.Offset).Distinct().Count()==count,"pages retain all hits exactly once");
        Assert(SourceLanguage.Replace(dense,all,"Other")==dense.Replace("Value","Other"),"linear selected replacement");
        using(var cancellation=new CancellationTokenSource()) { cancellation.Cancel(); await TestAssert.ThrowsAsync<OperationCanceledException>(()=>Task.Run(()=>SourceSearch.Count(dense,"x",true,cancellation.Token)),"search cancel inside file"); }
        var root=SourceFixture();
        try
        {
            new SourceSession([new("Root.ndf",12,24,16),new(".hidden/Values.ndf")],"Root.ndf",201,277,122).Save(root);
            var session=SourceSession.Load(root); Assert(session.Tabs.Count==2 && session.Active=="Root.ndf" && session.TreeWidth==201,"session metadata roundtrip");
            File.WriteAllText(Path.Combine(root,".warno-editor/source-session.json"),"{\"Tabs\":null}"); Assert(SourceSession.Load(root).Tabs.Count==0,"damaged optional session does not affect drafts");
            using var store=new DraftStore(root); await store.LoadAsync(); var data=SourceFiles.Open(root,"Root.ndf"); await store.UpsertAsync(SourceEditing.Operation("Root.ndf",data with {Text=data.Text+"// draft\n"}));
            var json=File.ReadAllBytes(store.DraftPath); File.WriteAllBytes(store.DraftPath,Encoding.UTF8.GetPreamble().Concat(json).ToArray());
            using var reload=new DraftStore(root); Assert(!(await reload.LoadAsync()).IsBlocked && reload.Operations.Count==1,"streamed schema6 supports BOM legacy JSON");
            SourceValidation.CheckLexicalDelta("Root.ndf","Bad is T(\n","Bad is T(\n// same old issue\n");
            const string badBefore="A is TBaseDamageModuleDescriptor(MaxPhysicalDamages = -1)\nB is TBaseDamageModuleDescriptor(MaxPhysicalDamages = 2)\n";
            File.WriteAllText(Path.Combine(root,"Bad.ndf"),badBefore);
            await reload.UpsertAsync(SourceEditing.Operation("Bad.ndf",SourceFiles.Open(root,"Bad.ndf") with {Text=badBefore.Replace("MaxPhysicalDamages = 2","MaxPhysicalDamages = -2")}));
            await TestAssert.ThrowsAsync<InvalidDataException>(()=>SourceEditing.Prepare(root,reload.Operations,default),"invalid baseline object does not exempt new invalid object");
            await reload.RemoveAsync(reload.Operations.Single(o=>o.RelativeSourceFile=="Bad.ndf").Id);
            await TestAssert.ThrowsAsync<InvalidDataException>(()=>Task.Run(()=>SourceValidation.CheckLexicalDelta("Root.ndf","Bad is T(\n","Bad is T(\n]\n")),"old issue does not excuse new rule violation");
            var preview=await SourceEditing.Prepare(root,reload.Operations,default); Assert(preview.SourceSnapshot!.Values.All(v=>v is null || v.Length==32),"snapshot stores full-content fingerprints");
            preview.SourceAcknowledged=true; var committed=await SourceTransactions.Commit(preview,reload);
            var journal=SourceTransactions.List(root).Single(); var payload=Path.Combine(root,".warno-editor/source-transactions",committed.BackupId,journal.Files[0].After); File.Move(payload,payload+".missing");
            Assert(SourceTransactions.List(root).Count==1,"history lists metadata without loading payloads");
            await TestAssert.ThrowsAsync<IOException>(()=>Task.Run(()=>SourceTransactions.Restore(root,committed.BackupId)),"restore still verifies missing payload");
        }
        finally { DeleteTemporaryFixture(root); }
        root=AviationFixture();
        try
        {
            const string path="GameData/Generated/Gameplay/Gfx/UniteDescriptor.ndf";
            using var store=new DraftStore(root);await store.LoadAsync();var data=SourceFiles.Open(root,path);
            await store.UpsertAsync(SourceEditing.Operation(path,data with {Text=data.Text.Replace("AltitudeMinGRU = 100","AltitudeMinGRU = 800")}));
            await TestAssert.ThrowsAsync<TransactionValidationException>(()=>SourceEditing.Prepare(root,store.Operations,default),"source final aviation height combination blocked");
            await store.UpsertAsync(SourceEditing.Operation(path,data with {Text=data.Text.Replace("AltitudeGRU = 700","AltitudeGRU = 800")}));
            var valid=await SourceEditing.Prepare(root,store.Operations,default);Assert(valid.Files.Count==1,"valid source aviation combination uses mod ceiling");
        }
        finally { DeleteTemporaryFixture(root); }
        Console.WriteLine("PASS Source optimization: aligned diff fuzz, complete paging, replacement, cancellation, sessions, streaming, lexical delta, fingerprint snapshot, lazy history and aviation combinations");
    }
    private static async Task SourceOptimizationPerformance(string mode,string assemblyPath)
    {
        var context=new AssemblyLoadContext("source-perf",true); var assembly=context.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
        Type Type(string name)=>assembly.GetType("WarnoLiteModdingTool.Core."+name)!;
        object? Call(string type,string method,params object?[] args) { var member=Type(type).GetMethod(method)!;return member.Invoke(null,args.Take(member.GetParameters().Length).ToArray()); }
        var root=SourceFixture(); var measured=new List<object>();
        try
        {
            var watch=new Stopwatch(); GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            var startMemory=Process.GetCurrentProcess().WorkingSet64;var allocated=GC.GetTotalAllocatedBytes(true);
            if(mode=="search")
            {
                var text=string.Concat(Enumerable.Repeat("Value = 1\n",500_000)); watch.Start();
                object? result;
                if(assembly.GetType("WarnoLiteModdingTool.Core.SourceWorkspace.SourceSearch") is null)
                    result=Call("SourceWorkspace.SourceLanguage","Find","All.ndf",text,"Value",true,CancellationToken.None);
                else { var count=(int)Call("SourceWorkspace.SourceSearch","Count",text,"Value",true,CancellationToken.None)!; var file=Activator.CreateInstance(Type("SourceWorkspace.SourceSearchFile"),"All.ndf",text,count)!;var list=Array.CreateInstance(file.GetType(),1);list.SetValue(file,0); result=Call("SourceWorkspace.SourceSearch","Page",list,"Value",true,0,500,CancellationToken.None); Assert(count==500_000,"same search count"); }
                watch.Stop(); GC.KeepAlive(result);
            }
            else
            {
                var text="// "+new string('x',50_000_000)+"\nExample is T(Value = 1)\n"; File.WriteAllText(Path.Combine(root,"Large.ndf"),text);
                var data=Call("SourceWorkspace.SourceFiles","Open",root,"Large.ndf",null)!; data.GetType().GetProperty("Text")!.SetValue(data,text+"// edited\n");
                using var store=(IDisposable)Activator.CreateInstance(Type("Drafts.DraftStore"),root)!;
                await (Task)store.GetType().GetMethod("LoadAsync")!.Invoke(store,[CancellationToken.None])!;
                var operation=Call("SourceWorkspace.SourceEditing","Operation","Large.ndf",data);
                watch.Start(); await (Task)store.GetType().GetMethod("UpsertAsync")!.Invoke(store,[operation,CancellationToken.None])!; watch.Stop();
                measured.Add(new {DraftBytes=new FileInfo(Path.Combine(root,".warno-editor/draft-v1.json")).Length});
            }
            measured.Add(new {Milliseconds=watch.Elapsed.TotalMilliseconds,AllocatedBytes=GC.GetTotalAllocatedBytes(true)-allocated,StartWorkingSet=startMemory,PeakWorkingSet=Process.GetCurrentProcess().PeakWorkingSet64,ManagedHeap=GC.GetTotalMemory(false)});
            Directory.CreateDirectory("publish/qa-source-optimization"); var label=assemblyPath.Contains("preview.1")?"preview1":"optimized";
            var report=JsonSerializer.Serialize(new {Mode=mode,Build=label,OS=Environment.OSVersion.ToString(),Results=measured},new JsonSerializerOptions{WriteIndented=true});File.WriteAllText($"publish/qa-source-optimization/{label}-{mode}-{DateTime.UtcNow:HHmmssfff}.json",report);Console.WriteLine(report);
        }
        finally { DeleteTemporaryFixture(root);context.Unload(); }
    }
}
