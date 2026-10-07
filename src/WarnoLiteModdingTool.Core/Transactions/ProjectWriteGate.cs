namespace WarnoLiteModdingTool.Core.Transactions;

public static class ProjectWriteGate
{
    public static IDisposable Enter(string root)
    {
        var path = Changes.ChangePaths.Resolve(root, ".warno-editor/project-write.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new IOException("项目正在写入或生成，请稍候 / Project is being written or generated; try again", ex); }
    }
}
