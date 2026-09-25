using System.Text;

namespace WarnoLiteModdingTool.Core.Transactions;

// Reads the accumulated candidate, while retaining the original snapshot for commit/rollback.
internal sealed class CandidateTextFiles(string root, List<PlannedFileChange> files)
{
    private readonly Dictionary<string, (TextFileSnapshot Snapshot, string Text)> _items = new(StringComparer.OrdinalIgnoreCase);
    public string Get(string path, FormalTextFileKind kind = FormalTextFileKind.Ndf)
    {
        path = path.Replace('\\', '/');
        if (!_items.TryGetValue(path, out var item))
        {
            var snapshot = TextFileSnapshot.Load(root, path, kind, allowMissing: kind == FormalTextFileKind.Csv);
            var prior = files.SingleOrDefault(f => f.RelativePath.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (prior?.Action == PlannedFileAction.Delete) throw new TransactionValidationException("目标文件已计划删除：" + path);
            using var reader = prior is null ? null : new StreamReader(new MemoryStream(prior.CandidateBytes), true);
            _items[path] = item = (snapshot, reader?.ReadToEnd() ?? snapshot.Text);
        }
        return item.Text;
    }
    public string NewLine(string path) => _items[path.Replace('\\', '/')].Snapshot.NewLine;
    public void Set(string path, string text, FormalTextFileKind kind = FormalTextFileKind.Ndf)
    {
        path = path.Replace('\\', '/'); Get(path, kind);
        _items[path] = (_items[path].Snapshot, text);
    }
    public void Complete(string summary)
    {
        foreach (var (path, item) in _items)
        {
            if (Get(path) == item.Snapshot.Text && !files.Any(f => f.RelativePath.Equals(path, StringComparison.OrdinalIgnoreCase))) continue;
            files.RemoveAll(f => f.RelativePath.Equals(path, StringComparison.OrdinalIgnoreCase));
            files.Add(UnitApplyPlanner.ToWriteChange(item.Snapshot, item.Text, [summary]));
        }
    }
}
