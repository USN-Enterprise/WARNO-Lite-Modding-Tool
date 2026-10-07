using System.IO;
using System.Windows;
using System.Windows.Controls;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.SourceWorkspace;

namespace WarnoLiteModdingTool.App.SourceWorkspace;

public partial class SourceWorkspaceWindow
{
    private readonly Dictionary<string, SourceTabState> _pendingTabs = new(StringComparer.OrdinalIgnoreCase);
    private SourceSession _session = new([],null);
    private void RestoreTabs()
    {
        _session = SourceSession.Load(_root);
        foreach (var state in _session.Tabs.Concat(_store.Operations.Where(o => o.TargetKind == DraftTargetKind.SourceFile).Select(o => new SourceTabState(o.RelativeSourceFile))))
        {
            try { _ = SourceFiles.Resolve(_root, state.Path); } catch (Exception ex) when (ex is IOException or ArgumentException) { continue; }
            if (!_pendingTabs.TryAdd(state.Path,state)) continue;
            Documents.Items.Add(new TabItem { Header = state.Path, ToolTip = state.Path, Tag = state.Path, Content = new TextBlock { Text = T("选择标签以加载文件", "Select tab to load this file"), Margin = new Thickness(12) } });
        }
        Body.ColumnDefinitions[0].Width = new GridLength(Math.Clamp(_session.TreeWidth,150,500));
        DetailsColumn.Width = new GridLength(Details.Visibility == Visibility.Collapsed ? 0 : Math.Clamp(_session.DetailWidth,180,500));
        OutputRow.Height = new GridLength(Math.Clamp(_session.OutputHeight,65,350));
    }
    private void SaveSession()
    {
        if (_closed) return;
        var tabs = Documents.Items.OfType<TabItem>().Select(t => t.Tag is DocumentSession d ? new SourceTabState(d.Path,d.Editor.CaretOffset,d.Editor.VerticalOffset,d.Editor.FontSize) : _pendingTabs.GetValueOrDefault(t.Tag?.ToString() ?? "")).Where(s => s is not null).Cast<SourceTabState>().ToArray();
        try { new SourceSession(tabs, Current?.Path ?? (Documents.SelectedItem is TabItem { Tag: string path } ? path : null), Body.ColumnDefinitions[0].ActualWidth, Details.Visibility == Visibility.Collapsed ? _session.DetailWidth : DetailsColumn.ActualWidth,OutputRow.ActualHeight).Save(_root); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SetOutput(T("布局未保存；源码草稿不受影响：", "Layout not saved; source drafts are unaffected: ") + ex.Message); }
    }
    private async Task IndexInBackground()
    {
        try { await IndexAsync(_scan?.Token ?? default); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed) SetOutput(T("索引未完成：", "Index incomplete: ") + ex.Message); }
    }
    private Dictionary<string,(string Before,string After)>? _replacementUndo;
    private async void UndoReplacement_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (_replacementUndo is not { } undo) return;
        foreach (var pair in undo) { await OpenFileAsync(pair.Key); if (Current!.Editor.Text != pair.Value.After) throw new InvalidOperationException(T("替换后已有其他编辑，请先处理这些编辑再撤销替换。", "Text changed after replacement; resolve later edits before undoing the batch.")); }
        foreach (var pair in undo) _documents[pair.Key].Editor.Document.Replace(0,_documents[pair.Key].Editor.Document.TextLength,pair.Value.Before);
        await SaveAllAsync(); _replacementUndo = null;
    });
}
