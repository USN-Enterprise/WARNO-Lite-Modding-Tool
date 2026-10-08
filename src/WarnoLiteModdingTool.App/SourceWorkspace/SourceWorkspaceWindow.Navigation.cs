using System.Windows;
using System.IO;
using WarnoLiteModdingTool.App.Controls;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.SourceWorkspace;

namespace WarnoLiteModdingTool.App.SourceWorkspace;

public partial class SourceWorkspaceWindow
{
    private Action? _returnToBusiness;
    private Func<string>? _navigationNoticeText;
    public async Task NavigateBusinessAsync(BusinessSourceTarget target, Action returnToBusiness)
    {
        if (_busy) throw new InvalidOperationException(T("工作台正在处理操作，请稍后重试。", "Workspace is busy; please retry shortly."));
        await Run(async () =>
        {
            var path = Path.GetRelativePath(_root, SourceFiles.Resolve(_root, target.Path)).Replace('\\', '/');
            NavigationNotice.Visibility = Visibility.Collapsed; _navigationNoticeText = null;
            await OpenFileAsync(target.Path, restoreView: false);
            if (_closed || Current is not {} document || !document.Path.Equals(path, StringComparison.OrdinalIgnoreCase)) return;
            var text = document.Editor.Text; var revision = document.Revision;
            var ranges = await Task.Run(() => target.Locate(text));
            if (_closed || document.Revision != revision) return;
            _returnToBusiness = returnToBusiness; ReturnToBusiness.Visibility = Visibility.Visible;
            string Describe(SourceRange r)
            {
                var line = document.Editor.Document.GetLineByOffset(r.Offset);
                return $"{r.Label} · L{line.LineNumber}\n{document.Editor.Document.GetText(line.Offset, Math.Min(180, line.Length))}";
            }
            if (ranges.Count == 0)
            {
                NavigationNotice.Text = T("无直接源码位置；目标可能已更名、删除或尚未生成。当前仅打开文件，可返回来源页面。", "No direct source location: the target may be renamed, deleted or not generated yet. Only the file is open; you can return to the source page.");
                NavigationNotice.Visibility = Visibility.Visible; return;
            }
            var range = ranges.Count == 1 ? ranges[0] : SourceLink.Pick(this, ranges, r => target.Path + "\n" + Describe(r));
            if (range is null) return;
            foreach (var folding in document.Folding.AllFoldings.Where(f => f.StartOffset <= range.Offset + range.Length && f.EndOffset > range.Offset)) folding.IsFolded = false;
            document.Editor.Select(range.Offset, range.Length);
            document.Editor.ScrollToLine(document.Editor.Document.GetLineByOffset(range.Offset).LineNumber); document.Editor.Focus();
            var location = target.Path + " · " + Describe(range).Split('\n')[0];
            var hasForms = _store.Operations.Any(o => o.TargetKind != DraftTargetKind.SourceFile);
            var diskPath = SourceFiles.Resolve(_root, path);
            var externalChange = document.Data.Baseline is {} baseline && (!File.Exists(diskPath) || !File.ReadAllBytes(diskPath).SequenceEqual(baseline));
            _navigationNoticeText = () => location + " · " + T("当前工作台文本", "Current workspace text")
                + (target.Shared ? "\n" + T("共享源码：编辑影响共享本体；引用范围以已识别数据为限。", "Shared source: edits affect the shared object; reference coverage is limited to recognized data.") : "")
                + (hasForms ? "\n" + T("存在未应用表单草稿；源码未合并这些草稿。", "Pending form drafts exist; they are not merged into this source.") : "")
                + (externalChange ? "\n" + T("正式文件已在外部变化；保留当前缓冲，请使用三方核对。", "The file changed externally; this buffer is preserved. Use three-way review.") : "");
            RefreshNavigationNotice(); NavigationNotice.Visibility = Visibility.Visible;
        });
        if (!_closed) Current?.Editor.Focus();
    }
    private void RefreshNavigationNotice() { if (_navigationNoticeText is not null) NavigationNotice.Text = _navigationNoticeText(); }
    private async void ReturnToBusiness_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _returnToBusiness is null) return;
        await Run(async () => { await SaveAllAsync(); _returnToBusiness(); }, freeze: false);
    }
}
