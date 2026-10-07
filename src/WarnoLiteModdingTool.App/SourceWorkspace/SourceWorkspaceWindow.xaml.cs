using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Search;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.Theming;
using WarnoLiteModdingTool.App.ViewModels;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.SourceWorkspace;
using WarnoLiteModdingTool.Core.Transactions;
using MessageBox = WarnoLiteModdingTool.App.Localisation.LocalizedMessageBox;

namespace WarnoLiteModdingTool.App.SourceWorkspace;

public partial class SourceWorkspaceWindow : Window
{
    private readonly MainViewModel _main;
    private readonly DraftStore _store;
    private readonly string _root;
    private readonly Dictionary<string, DocumentSession> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _saving = new(1);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private readonly Stack<(string Path, int Offset)> _navigation = new();
    private SourceCatalog _catalog = new([], []);
    private List<SourceSymbol> _symbols = [];
    private string[] _words = [];
    private CancellationTokenSource? _search;
    private CancellationTokenSource? _scan;
    private CancellationTokenSource? _preparing;
    private readonly DispatcherTimer _filterTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly DispatcherTimer _analysisTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private bool _allowClose, _busy, _closed;
    private DocumentSession? Current => (Documents.SelectedItem as TabItem)?.Tag as DocumentSession;
    public string ProjectRoot => _root;
    public TextEditor? ActiveEditor => Current?.Editor;
    public Task Initialization { get; private set; } = Task.CompletedTask;
    private static string T(string zh, string en) => UiText.Current.English ? en : zh;

    public SourceWorkspaceWindow(MainViewModel main)
    {
        _main = main; _store = main.SourceDraftStore; _root = _store.ProjectRoot;
        InitializeComponent(); ProjectLabel.Text = _root;
        _filterTimer.Tick += (_, _) => { _filterTimer.Stop(); FilterTree(); };
        _analysisTimer.Tick += (_, _) => { _analysisTimer.Stop(); UpdateSymbols(); };
        Width = Math.Min(Width, SystemParameters.WorkArea.Width - 30); Height = Math.Min(Height, SystemParameters.WorkArea.Height - 30);
        _main.SaveSourceInputs = async () => { Body.IsEnabled = false; try { await SaveAllAsync(); } finally { Body.IsEnabled = true; } };
        _main.PropertyChanged += MainChanged;
        _main.SourceProjectClosing += ProjectClosing;
        ThemeManager.ThemeChanged += ThemeChanged;
        UiText.Current.PropertyChanged += LanguageChanged;
        _timer.Tick += async (_, _) => { _timer.Stop(); try { await SaveAllAsync(); } catch (Exception ex) { Error(ex); } };
        Loaded += (_, _) => Initialization = Run(async () =>
        {
            await RefreshAsync(analyze: false);
            RestoreTabs();
            var selected = _main.IsAmmoModule ? _main.AmmoWorkspace?.SelectedAmmo?.Ammo.Source : _main.IsWeaponModule ? _main.WeaponWorkspace?.SelectedWeapon?.Source : _main.IsDivisionModule ? _main.DivisionWorkspace?.SelectedDivision?.Division.Source : _main.SelectedObject ?? _main.UnitWorkspace?.SelectedUnit?.Unit.Source;
            if (selected is not null) await OpenFileAsync(selected.RelativeSourceFile, selected.CharacterOffset);
            else if (_session.Active is { } active && _pendingTabs.ContainsKey(active)) await OpenFileAsync(active);
            else if (_pendingTabs.Keys.FirstOrDefault() is { } pendingPath) await OpenFileAsync(pendingPath);
            if (Current is null && _catalog.Files.FirstOrDefault() is { } first) await OpenFileAsync(first.Path);
            var pending = SourceTransactions.List(_root).Where(j => j.State is "Prepared" or "Committing" or "RecoveryRequired" or "Restoring").ToArray();
            _ = IndexInBackground();
            if (pending.Length > 0) SetOutput(T("存在未完成事务，请打开源码备份与恢复。", "Unfinished transactions: open Source backups and recovery.") + "\n" + string.Join("\n", pending.Select(j => j.Id)));
        });
        Closing += CloseAsync;
        Closed += (_, _) => { _closed = true; _timer.Stop(); _filterTimer.Stop(); _analysisTimer.Stop(); _preparing?.Cancel(); foreach (var doc in _documents.Values) doc.AnalysisCancellation?.Cancel(); _search?.Cancel(); _scan?.Cancel(); _indexCancellation?.Cancel(); _main.SaveSourceInputs = null; _main.PropertyChanged -= MainChanged; _main.SourceProjectClosing -= ProjectClosing; ThemeManager.ThemeChanged -= ThemeChanged; UiText.Current.PropertyChanged -= LanguageChanged; };
        Body.SizeChanged += (_, _) => { OutputRow.MaxHeight = Math.Max(140,Body.ActualHeight * .40); var narrow = Body.ActualWidth < 1060; if (narrow != (Details.Visibility == Visibility.Collapsed)) { if (narrow) _session = _session with { DetailWidth = DetailsColumn.ActualWidth }; DetailsColumn.Width = new GridLength(narrow ? 0 : Math.Clamp(_session.DetailWidth,180,500)); Details.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible; } if (Body.ActualWidth < 850) Body.ColumnDefinitions[0].Width = new GridLength(165); };
        PreviewKeyDown += Shortcuts;
    }
    private void MainChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(MainViewModel.ProjectPath) && !string.Equals(_root, _main.ProjectPath, StringComparison.OrdinalIgnoreCase)) { _allowClose = true; Close(); } }
    private void ProjectClosing() { SaveSession(); _main.SourceProjectClosing -= ProjectClosing; _allowClose = true; Close(); }
    private void ThemeChanged() { foreach (var document in _documents.Values) SourceEditorStyle.Apply(document.Editor); }
    private void LanguageChanged(object? sender, PropertyChangedEventArgs e) { UpdateStatus(); BuildTree(); }
    private void More_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var item in new (string Label, RoutedEventHandler Handler)[] { ("备份总览", HistoryOverview_Click), ("撤销上次替换", UndoReplacement_Click), ("三方核对", Conflict_Click), ("丢弃当前草稿", Discard_Click), ("源码备份与恢复", History_Click), ("生成 Mod", Generate_Click), ("引用与字段工具", Legacy_Click), ("定义", Definition_Click), ("引用", References_Click), ("返回", Back_Click), ("返回业务对象", Business_Click) })
        { var entry = new MenuItem { Header = UiText.T(item.Label) }; entry.Click += item.Handler; menu.Items.Add(entry); }
        menu.SetResourceReference(BackgroundProperty, "SurfaceBrush"); menu.SetResourceReference(ForegroundProperty, "TextBrush"); menu.PlacementTarget = (UIElement)sender; menu.IsOpen = true;
    }
    private async void CloseAsync(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return; e.Cancel = true;
        if (_busy) { Status.Text = T("操作进行中，请稍候或取消搜索。", "Operation in progress; wait or cancel search."); return; }
        _busy = true; Body.IsEnabled = false;
        try { await SaveAllAsync(); SaveSession(); _allowClose = true; Close(); } catch (Exception ex) { Error(ex); }
        finally { _busy = false; Body.IsEnabled = true; }
    }
    private void Error(Exception ex) { SetOutput(ex.Message); Status.Text = T("操作失败，输入已保留：", "Operation failed; input retained: ") + ex.Message; }
    private void SetOutput(string text) { Output.Text = text; Results.SelectedIndex = 1; }
    private async Task Run(Func<Task> action, bool freeze = true)
    {
        if (_busy) return; _busy = true; _main.SourceOperationBusy = true; Actions.IsEnabled = false; if (freeze) Body.IsEnabled = false;
        try { await action(); } catch (OperationCanceledException) { Status.Text = T("已取消", "Cancelled"); } catch (Exception ex) { Error(ex); }
        finally { _busy = false; _main.SourceOperationBusy = false; Actions.IsEnabled = true; Body.IsEnabled = true; }
    }
    public async Task RefreshAsync(bool analyze = true)
    {
        _scan?.Cancel(); var scan = _scan = new(); Status.Text = T("正在发现文件…", "Discovering files…");
        _catalog = await Task.Run(() => SourceFiles.Discover(_root, scan.Token), scan.Token); if (_closed) return;
        BuildTree(); if (_catalog.Errors.Count > 0) SetOutput(string.Join("\n", _catalog.Errors));
        if (analyze) await IndexAsync(scan.Token); Status.Text = T("文件扫描完成", "File scan complete") + $" · {_catalog.Files.Count}" + (_catalog.Errors.Count > 0 ? T(" · 部分路径无法访问", " · Some paths inaccessible") : "");
    }
    private readonly System.Collections.ObjectModel.ObservableCollection<SourceTreeNode> _tree = [];
    private readonly Dictionary<string, SourceTreeNode> _treeNodes = new(StringComparer.OrdinalIgnoreCase);
    private void BuildTree()
    {
        if (FileTree is null) return;
        FileTree.ItemsSource ??= _tree;
        var paths = _catalog.Files.Select(f => f.Path).Concat(_store.Operations.Where(o => o.TargetKind == DraftTargetKind.SourceFile).Select(o => o.RelativeSourceFile)).Concat(_documents.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);
        var alive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var parent = _tree; var parts = path.Split('/'); var key = "";
            for (var i = 0; i < parts.Length; i++)
            {
                key += parts[i] + (i < parts.Length - 1 ? "/" : ""); alive.Add(key);
                if (!_treeNodes.TryGetValue(key, out var node)) { node = new(key); _treeNodes.Add(key, node); parent.Add(node); }
                parent = node.Children;
            }
        }
        foreach (var key in _treeNodes.Keys.Where(k => !alive.Contains(k)).OrderByDescending(k => k.Length).ToArray())
        {
            var node = _treeNodes[key]; var trim = key.TrimEnd('/'); var slash = trim.LastIndexOf('/');
            (slash < 0 ? _tree : _treeNodes[trim[..(slash + 1)]].Children).Remove(node); _treeNodes.Remove(key);
        }
        FilterTree(); UpdateTreeCounts();
    }
    private void FilterTree()
    {
        if (PathFilter is null) return;
        bool Filter(SourceTreeNode node) { var child = false; foreach (var item in node.Children) child |= Filter(item); var visible = child || node.Path.Contains(PathFilter.Text, StringComparison.OrdinalIgnoreCase); node.Visibility = visible ? Visibility.Visible : Visibility.Collapsed; return visible; }
        foreach (var node in _tree) Filter(node);
    }
    private void UpdateTreeCounts()
    {
        var drafts = _store.Operations.Where(o => o.TargetKind == DraftTargetKind.SourceFile).Select(o => o.RelativeSourceFile).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in _treeNodes) pair.Value.Draft = drafts.Contains(pair.Key);
        FileCount.Text = $"{_catalog.Files.Count} NDF / CSV · {drafts.Count} " + T("源码草稿", "source drafts");
    }
    public async Task OpenFileAsync(string path, int offset = 0, bool remember = true)
    {
        double? restoredVertical = null;
        path = Path.GetRelativePath(_root, SourceFiles.Resolve(_root, path)).Replace('\\', '/');
        if (!_documents.TryGetValue(path, out var session))
        {
            var source = _store.Operations.FirstOrDefault(o => o.TargetKind == DraftTargetKind.SourceFile && o.RelativeSourceFile.Equals(path, StringComparison.OrdinalIgnoreCase))?.Source;
            if (source is null)
            {
                try { source = await Task.Run(() => SourceFiles.Open(_root, path)); }
                catch (DecoderFallbackException)
                {
                    var cp = SourceDialogs.Prompt(this, T("选择编码代码页（例如 936、1252）", "Choose encoding code page (e.g. 936, 1252)"), "936");
                    if (!int.TryParse(cp, out var value)) return; source = SourceFiles.Open(_root, path, value);
                }
            }
            if (_closed) return;
            var editor = SourceEditorStyle.Create(); editor.Text = source.Text; editor.WordWrap = Wrap.IsChecked == true;
            SourceEditorStyle.Apply(editor);
            SearchPanel.Install(editor.TextArea);
            var tab = Documents.Items.OfType<TabItem>().FirstOrDefault(t => t.Tag is string pendingPath && pendingPath.Equals(path,StringComparison.OrdinalIgnoreCase)) ?? new TabItem { ToolTip = path };
            tab.Content = editor;
            if (_pendingTabs.Remove(path, out var state)) { offset = state.Offset; editor.FontSize = Math.Clamp(state.FontSize,10,28); restoredVertical = Math.Max(0,state.Vertical); }
            session = new(path, source, editor, tab); tab.Tag = session;
            var captured = session;
            editor.TextChanged += (_, _) => { captured.Revision++; captured.Dirty = true; _indexCancellation?.Cancel(); UpdateHeader(captured); _analysisTimer.Stop(); _analysisTimer.Start(); _timer.Stop(); _timer.Start(); UpdateStatus(); };
            editor.TextArea.Caret.PositionChanged += (_, _) => { if (Current == captured) UpdateStatus(); };
            editor.PreviewMouseWheel += (_, e) => { if (Keyboard.Modifiers == ModifierKeys.Control) { editor.FontSize = Math.Clamp(editor.FontSize + Math.Sign(e.Delta), 10, 28); e.Handled = true; } };
            _documents.Add(path, session); if (!Documents.Items.Contains(tab)) Documents.Items.Add(tab); UpdateHeader(session);
        }
        if (remember && Current is { } old && old != session) _navigation.Push((old.Path, old.Editor.CaretOffset));
        Documents.SelectedItem = session.Tab;
        session.Editor.CaretOffset = Math.Clamp(offset, 0, session.Editor.Document.TextLength); session.Editor.ScrollToLine(session.Editor.TextArea.Caret.Line); session.Editor.Focus();
        if (restoredVertical is { } vertical) await Dispatcher.InvokeAsync(()=>session.Editor.ScrollToVerticalOffset(vertical),DispatcherPriority.Loaded);
        UpdateSymbols(); UpdateStatus();
    }
    private void UpdateHeader(DocumentSession session)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock { Text = session.Path + (session.Dirty ? " ●" : _store.Operations.Any(o => o.TargetKind == DraftTargetKind.SourceFile && o.RelativeSourceFile.Equals(session.Path, StringComparison.OrdinalIgnoreCase)) ? " ◇" : ""), VerticalAlignment = VerticalAlignment.Center });
        var close = new Button { Content = "×", Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(8, 0, 0, 0), ToolTip = T("关闭标签，保留草稿", "Close tab, keep draft") };
        close.SetResourceReference(StyleProperty, "SecondaryButton");
        close.Click += async (_, _) => { session.Editor.IsEnabled = false; try { await SaveAllAsync(); _documents.Remove(session.Path); session.AnalysisCancellation?.Cancel(); Documents.Items.Remove(session.Tab); SaveSession(); } catch (Exception ex) { Error(ex); } finally { session.Editor.IsEnabled = true; } };
        header.Children.Add(close); session.Tab.Header = header;
    }
    public async Task SaveAllAsync()
    {
        _timer.Stop(); await _saving.WaitAsync();
        try
        {
            if (!_documents.Values.Any(d => d.Dirty)) { UpdateStatus(); return; }
            await _main.FlushBusinessInputsAsync();
            var pending = _documents.Values.Where(d => d.Dirty).Select(d => (Session: d, Revision: d.Revision, Data: d.Data with { Text = d.Editor.Text })).ToArray();
            foreach (var item in pending) item.Session.Saving = true; UpdateStatus();
            var upserts = new List<DraftOperation>(); var removals = new List<string>();
            await Task.Run(() => {
            foreach (var item in pending)
            {
                var session = item.Session; var data = item.Data;
                var op = SourceEditing.Operation(session.Path, data);
                byte[]? encoded = null; try { encoded = SourceFiles.Encode(session.Path, data); } catch (InvalidDataException) { /* Encoding conversion may remain unconfirmed in a draft. */ }
                if (encoded is not null && SourceFiles.Equal(data.Baseline, encoded)) removals.Add(op.Id);
                else upserts.Add(op);
            }
            });
            await _store.ApplyBatchAsync(upserts, removals);
            var conflicts = await Task.Run(() => pending.Where(p => !SourceFiles.Equal(p.Data.Baseline,SourceFiles.Read(_root,p.Session.Path))).Select(p=>p.Session.Path).ToHashSet(StringComparer.OrdinalIgnoreCase));
            foreach (var item in pending)
            {
                var session = item.Session; var data = item.Data;
                session.Data = data;
                session.Conflict = conflicts.Contains(session.Path);
                if (session.Revision == item.Revision) session.Dirty = false;
                UpdateHeader(session);
            }
            _main.SourceDraftsChanged(); UpdateStatus(); UpdateTreeCounts();
            if (_documents.Values.Any(d => d.Dirty)) _timer.Start();
        }
        finally { foreach (var doc in _documents.Values) doc.Saving = false; UpdateStatus(); _saving.Release(); }
    }
    private void UpdateStatus()
    {
        if (Current is not { } doc) return;
        var caret = doc.Editor.TextArea.Caret;
        Status.Text = $"{doc.Path} · L{caret.Line}:C{caret.Column} · {doc.Data.CodePage}" + (doc.Data.Bom ? " BOM" : "") + " · " +
            (doc.Saving ? T("正在保存草稿", "Saving draft") : doc.AppliedPending ? T("已应用，草稿待收尾", "Applied; draft cleanup pending") : doc.Conflict ? T("磁盘冲突，请三方核对", "Disk conflict; compare three versions") : doc.Dirty ? T("草稿未保存", "Draft not saved") : _store.Operations.Any(o=>o.TargetKind==DraftTargetKind.SourceFile && o.RelativeSourceFile.Equals(doc.Path,StringComparison.OrdinalIgnoreCase)) ? T("已存草稿", "Draft saved") : T("无修改", "Unchanged")) + (doc.Data.Baseline is null ? T(" · 尚未写入正式目录", " · Not yet in formal files") : "") + " · " + doc.LineEndings + (doc.Editor.Document.TextLength > 2_000_000 ? T(" · 大文件：简化着色", " · Large file: reduced highlighting") : "");
    }
    private Dictionary<string, string> Buffers() => _documents.ToDictionary(p => p.Key, p => p.Value.Editor.Text, StringComparer.OrdinalIgnoreCase);
    private Dictionary<string,(string Fingerprint,IReadOnlyList<SourceSymbol> Symbols,IReadOnlyList<string> Words)> _indexCache = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _indexCancellation;
    private async Task IndexAsync(CancellationToken token = default)
    {
        _indexCancellation?.Cancel(); var cancel = _indexCancellation = CancellationTokenSource.CreateLinkedTokenSource(token); token = cancel.Token;
        var previous = _indexCache; var next = new Dictionary<string,(string Fingerprint,IReadOnlyList<SourceSymbol> Symbols,IReadOnlyList<string> Words)>(StringComparer.OrdinalIgnoreCase);
        var buffers = Buffers(); var drafts = _store.Operations.Where(o => o.TargetKind == DraftTargetKind.SourceFile).ToDictionary(o => o.RelativeSourceFile, o => o.Source!.Text, StringComparer.OrdinalIgnoreCase);
        var paths = _catalog.Files.Where(f => SourceFiles.IsNdf(f.Path)).Select(f => f.Path).Concat(drafts.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var result = await Task.Run(() =>
        {
            var symbols = new List<SourceSymbol>(); var words = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in paths)
            {
                token.ThrowIfCancellationRequested();
                if (_closed) break;
                try { var text = buffers.GetValueOrDefault(path) ?? drafts.GetValueOrDefault(path) ?? SourceFiles.Open(_root, path).Text; if (text.Length > 10_000_000) continue; var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(text.AsSpan()))); if (!previous.TryGetValue(path,out var entry) || entry.Fingerprint != fingerprint) entry=(fingerprint,SourceLanguage.Symbols(path,text,token),SourceLanguage.Words(text,token)); next[path]=entry; symbols.AddRange(entry.Symbols); foreach(var word in entry.Words)words.Add(word); }
                catch (Exception ex) when (ex is IOException or ArgumentException) { /* File remains editable; analysis is explicitly partial. */ }
            }
            return (symbols, words.ToArray());
        });
        if (_closed || cancel.IsCancellationRequested) return; _indexCache = next; _symbols = result.symbols; _words = result.Item2.Concat(_symbols.Select(s => "~/" + s.Name)).Distinct().ToArray(); UpdateSymbols();
    }
    private async void UpdateSymbols()
    {
        if (Symbols is null || Current is not { } current) return;
        if (current.Editor.Document.TextLength > 10_000_000) { Symbols.ItemsSource = null; return; }
        if (current.AnalysisRevision == current.Revision) { Symbols.ItemsSource = current.Symbols; return; }
        current.AnalysisCancellation?.Cancel(); var cancel = current.AnalysisCancellation = new();
        var revision = current.Revision; var text = current.Editor.Text;
        try
        {
            var result = await Task.Run(() => { cancel.Token.ThrowIfCancellationRequested(); var symbols = SourceLanguage.Symbols(current.Path, text,cancel.Token); cancel.Token.ThrowIfCancellationRequested(); var objects = new NdfTopLevelScanner().Scan(text, current.Path, "source",cancellationToken:cancel.Token).Objects; return (symbols, objects); }, cancel.Token);
            if (_closed || cancel.IsCancellationRequested || current.Revision != revision) return;
            current.AnalysisRevision = revision; current.Symbols = result.symbols;
            current.Folding.UpdateFoldings(result.objects.Where(o => o.CharacterLength > 2).Select(o => new NewFolding(o.CharacterOffset, o.CharacterOffset + o.CharacterLength) { Name = o.Name }), -1);
            if (Current == current) Symbols.ItemsSource = current.Symbols;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed) SetOutput(T("大纲未完成：", "Outline incomplete: ") + ex.Message); }
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await Run(() => RefreshAsync(), freeze: false);
    private async void Save_Click(object sender, RoutedEventArgs e) => await Run(SaveAllAsync);
    private async void New_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var path = SourceDialogs.Prompt(this, T("项目内相对路径", "Relative path inside project"), "Custom/New.ndf"); if (string.IsNullOrWhiteSpace(path)) return;
        if (!SourceFiles.IsNdf(path)) throw new InvalidDataException(T("新文件必须为 .ndf", "New files must use .ndf"));
        path = path.Replace('\\', '/'); if (SourceFiles.Read(_root, path) is not null || _documents.ContainsKey(path)) throw new IOException(T("文件已存在", "File exists"));
        await OpenFileAsync(path); Current!.Dirty = true; await SaveAllAsync(); BuildTree();
    });
    private async void FileTree_Open(object sender, MouseButtonEventArgs e) { if (FileTree.SelectedItem is SourceTreeNode { Path: string path } && SourceFiles.IsText(path)) await Run(() => OpenFileAsync(path)); }
    private async void FileTree_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && FileTree.SelectedItem is SourceTreeNode { Path: string path } && SourceFiles.IsText(path)) { e.Handled = true; await Run(() => OpenFileAsync(path)); } }
    private void PathFilter_Changed(object sender, TextChangedEventArgs e) { _filterTimer.Stop(); _filterTimer.Start(); }
    private async void Document_Changed(object sender, SelectionChangedEventArgs e) { if (!ReferenceEquals(e.Source, Documents)) return; if (Documents.SelectedItem is TabItem { Tag: string path }) { await Run(() => OpenFileAsync(path), freeze: false); return; } UpdateSymbols(); UpdateStatus(); }
    private void Wrap_Changed(object sender, RoutedEventArgs e) { foreach (var d in _documents.Values) d.Editor.WordWrap = Wrap.IsChecked == true; }
    private async void Search_Click(object sender, RoutedEventArgs e) => await SearchAsync();
    private readonly List<SourceSearchFile> _searchFiles = [];
    private string _searchQuery = ""; private bool _searchCase; private int _searchPage; private bool _pageBusy;
    private async void PreviousPage_Click(object sender, RoutedEventArgs e) { if (_searchPage > 0 && !_pageBusy) { _searchPage--; await ShowSearchPage(); } }
    private async void NextPage_Click(object sender, RoutedEventArgs e) { if ((_searchPage+1)*500 < _searchFiles.Sum(f => f.Count) && !_pageBusy) { _searchPage++; await ShowSearchPage(); } }
    private async Task ShowSearchPage()
    {
        if (_pageBusy || _closed) return; _pageBusy = true;
        var search = _search; var files = _searchFiles.ToArray(); var page = _searchPage; var query = _searchQuery; var match = _searchCase;
        try
        {
            var hits = await Task.Run(() => SourceSearch.Page(files, query, match, page*500, 500, search?.Token ?? default));
            if (_closed || search != _search || search?.IsCancellationRequested == true) return;
            Hits.ItemsSource = hits; SearchPageStatus.Text = $"{page+1}/{Math.Max(1, (files.Sum(f => f.Count)+499)/500)} · {files.Sum(f => f.Count)} " + T("处；Ctrl+A 仅选当前页", "matches; Ctrl+A selects this page only");
        }
        catch (OperationCanceledException) { }
        finally { _pageBusy = false; }
    }
    private async Task SearchAsync()
    {
        if (Query.Text.Length == 0) return; _search?.Cancel(); var cancel = _search = new();
        _searchQuery = Query.Text; _searchCase = MatchCase.IsChecked == true; _searchPage = 0; _searchFiles.Clear(); Hits.ItemsSource = null;
        var query = _searchQuery; var matchCase = _searchCase; var buffers = Buffers();
        var drafts = _store.Operations.Where(o => o.TargetKind == DraftTargetKind.SourceFile).ToDictionary(o => o.RelativeSourceFile, o => o.Source!.Text, StringComparer.OrdinalIgnoreCase);
        var paths = CurrentOnly.IsChecked == true ? Current is { } current ? new[] { current.Path } : [] : _catalog.Files.Select(f => f.Path).Concat(drafts.Keys).Concat(buffers.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        try
        {
            Status.Text = T("正在搜索…", "Searching…"); Results.SelectedIndex = 0;
            var errors = await Task.Run(async () =>
            {
                var errors = new List<string>(); var batch = new List<SourceSearchFile>(); var watch = System.Diagnostics.Stopwatch.StartNew();
                foreach (var path in paths)
                {
                    cancel.Token.ThrowIfCancellationRequested();
                    try { var text = buffers.GetValueOrDefault(path) ?? drafts.GetValueOrDefault(path) ?? SourceFiles.Open(_root, path).Text; var count = SourceSearch.Count(text, query, matchCase, cancel.Token); if (count > 0) batch.Add(new(path,text,count)); }
                    catch (Exception ex) when (ex is IOException or ArgumentException) { errors.Add(path + ": " + ex.Message); }
                    if (batch.Count > 0 && (watch.ElapsedMilliseconds > 150 || batch.Count >= 25))
                    {
                        var chunk = batch.ToArray(); batch.Clear(); watch.Restart();
                        await Dispatcher.InvokeAsync(() => { if (_closed || cancel != _search || cancel.IsCancellationRequested) return; _searchFiles.AddRange(chunk); if (!_pageBusy) _ = ShowSearchPage(); });
                    }
                }
                var last = batch.ToArray(); await Dispatcher.InvokeAsync(() => { if (!_closed && cancel == _search && !cancel.IsCancellationRequested) _searchFiles.AddRange(last); });
                return errors;
            }, cancel.Token);
            if (_closed || cancel != _search || cancel.IsCancellationRequested) return;
            while (_pageBusy) { await Task.Delay(15); cancel.Token.ThrowIfCancellationRequested(); }
            await ShowSearchPage(); Status.Text = T("搜索完成；替换前核对结果，表单草稿未物化为源码。", "Search complete; review replacements. Form drafts are not materialized.");
            if (errors.Count > 0) SetOutput(string.Join("\n", errors));
        }
        catch (OperationCanceledException) { if (!_closed && cancel == _search) Status.Text = T("搜索已取消，当前结果不完整", "Search cancelled; current results are incomplete"); }
        catch (Exception ex) { Error(ex); }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { _search?.Cancel(); _scan?.Cancel(); _indexCancellation?.Cancel(); _preparing?.Cancel(); }
    private async void Output_Open(object sender, MouseButtonEventArgs e) => await Run(async () =>
    {
        var index = Output.GetLineIndexFromCharacterIndex(Output.CaretIndex); if (index < 0) return;
        var line = Output.GetLineText(index).Replace('\\', '/');
        var paths = _catalog.Files.Where(f => line.Contains(f.Path, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (paths.Length == 0) paths = _catalog.Files.Where(f => line.Contains(Path.GetFileName(f.Path), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (paths.Length != 1) return;
        await OpenFileAsync(paths[0].Path);
        var match = System.Text.RegularExpressions.Regex.Match(line, @"(?:line\s*|行\s*|[:,(]\s*)(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        if (match.Success && int.TryParse(match.Groups[1].Value, out var number) && Current is { } doc) { number = Math.Clamp(number, 1, doc.Editor.Document.LineCount); doc.Editor.CaretOffset = doc.Editor.Document.GetLineByNumber(number).Offset; doc.Editor.ScrollToLine(number); }
    });
    private async void Hits_Open(object sender, MouseButtonEventArgs e) { if (Hits.SelectedItem is SourceHit hit) await Run(() => OpenFileAsync(hit.File, hit.Offset)); }
    private async void Replace_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var hits = Hits.SelectedItems.Cast<SourceHit>().ToArray(); if (hits.Length == 0) return;
        var replacements = new List<(DocumentSession Doc, string Before, string Text)>();
        foreach (var group in hits.GroupBy(h => h.File)) { await OpenFileAsync(group.Key); replacements.Add((Current!, Current!.Editor.Text, SourceLanguage.Replace(Current!.Editor.Text, group, Replacement.Text))); }
        var operations = replacements.Select(r => SourceEditing.Operation(r.Doc.Path,r.Doc.Data with { Text=r.Text })).ToArray();
        var files = replacements.Select(r => new PlannedFileChange(r.Doc.Path, SourceFiles.Resolve(_root,r.Doc.Path), SourceFiles.IsNdf(r.Doc.Path) ? FormalTextFileKind.Ndf : FormalTextFileKind.Csv,PlannedFileAction.Write,true,Encoding.UTF8.GetBytes(r.Before),Encoding.UTF8.GetBytes(r.Text),null,[])).ToArray();
        var preview = new ApplyPreview(_root,"",DateTimeOffset.UtcNow,operations.Select(o => o with { Source=o.Source! with { CodePage=65001 } }).ToArray(),files,[T($"本次替换 {hits.Length} 处，来自当前结果页的选中项；只更新草稿。",$"Replace {hits.Length} selected matches from the current page; drafts only.")]);
        if (SourceDialogs.Review(this,preview,draftsOnly:true)!=true) return;
        _replacementUndo = replacements.ToDictionary(r=>r.Doc.Path,r=>(r.Before,r.Text),StringComparer.OrdinalIgnoreCase);
        foreach (var change in replacements) change.Doc.Editor.Document.Replace(0, change.Doc.Editor.Document.TextLength, change.Text);
        await SaveAllAsync(); Hits.ItemsSource = null; SearchPageStatus.Text = ""; _search?.Cancel(); _searchFiles.Clear();
    });
    private async void Check_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        await SaveAllAsync(); var revisions = _documents.ToDictionary(p=>p.Key,p=>p.Value.Revision);
        _preparing?.Cancel(); _preparing = new(); var operations = _store.Operations.Where(o=>o.TargetKind==DraftTargetKind.SourceFile).ToArray();
        if (operations.Length == 0) { SetOutput(T("没有待检查的源码草稿；当前操作检查待应用组合。", "No source drafts; this action checks the pending source group.")); return; }
        var preview = await Task.Run(() => SourceEditing.Prepare(_root,operations,_preparing.Token),_preparing.Token);
        var stale = revisions.Any(p=>!_documents.TryGetValue(p.Key,out var doc) || doc.Revision!=p.Value);
        SetOutput((stale ? T("检查期间内容变化；结果已过期，请重新检查。\n", "Text changed during checking; results are stale. Check again.\n") : "") + string.Join("\n",preview.ValidationMessages));
    }, freeze:false);
    private async void Apply_Click(object sender, RoutedEventArgs e) => await Run(ApplyAsync);
    public async Task ApplyAsync()
    {
        Body.IsEnabled = false;
        try { await ApplyCoreAsync(); } finally { Body.IsEnabled = true; }
    }
    private async Task ApplyCoreAsync()
    {
        await _main.FlushBusinessInputsAsync(); await SaveAllAsync();
        foreach (var operation in _store.Operations.Where(o => o.TargetKind == DraftTargetKind.SourceFile && SourceFiles.IsNdf(o.RelativeSourceFile) && o.Source is { } data && (data.CodePage != 65001 || data.Bom) && !data.ConvertEncoding).ToArray())
        {
            if (MessageBox.Show(this, operation.RelativeSourceFile + "\n" + T("应用时转换为 UTF-8 无 BOM？原始字节将备份。", "Convert to UTF-8 without BOM on apply? Original bytes will be backed up."), Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            await _store.UpsertAsync(operation with { Source = operation.Source! with { ConvertEncoding = true }, UpdatedUtc = DateTimeOffset.UtcNow });
            if (_documents.TryGetValue(operation.RelativeSourceFile, out var doc)) doc.Data = doc.Data with { ConvertEncoding = true };
        }
        await SaveAllAsync();
        var operations = _store.Operations.Where(o => o.TargetKind == DraftTargetKind.SourceFile).ToArray(); if (operations.Length == 0) { Status.Text = T("没有源码草稿", "No source drafts"); return; }
        _preparing?.Cancel(); _preparing = new();
        var preview = await Task.Run(() => SourceEditing.Prepare(_root, operations, _preparing.Token), _preparing.Token);
        if (SourceDialogs.Review(this, preview) != true) return;
        var result = await Task.Run(() => SourceTransactions.Commit(preview, _store));
        foreach (var operation in preview.Operations)
            if (_documents.TryGetValue(operation.RelativeSourceFile, out var doc)) { doc.Data = SourceFiles.Open(_root, doc.Path); doc.Dirty = false; doc.Conflict = false; doc.AppliedPending = _store.Operations.Any(o=>o.Id==operation.Id); UpdateHeader(doc); }
        _main.SourceDraftsChanged();
        UpdateStatus(); SetOutput(result.Message + "\n" + result.BackupId + "\n" + string.Join("\n",result.Warnings));
        try { await _main.RefreshSourceProjectAsync(preview); }
        catch (Exception ex) { SetOutput(result.Message + "\n" + T("界面刷新失败：", "Refresh failed: ") + ex.Message); return; }
        try { await RefreshAsync(); UpdateStatus(); } catch (Exception ex) { SetOutput(result.Message + "\n" + result.BackupId + "\n" + T("文件已应用；文件树刷新失败：", "Files applied; file tree refresh failed: ") + ex.Message); }
    }
    private async void Conflict_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (Current is not { } doc) return; var disk = SourceFiles.Open(_root, doc.Path, doc.Data.CodePage);
        var merged = SourceDialogs.Merge(this, doc.Path, doc.Data, disk, doc.Editor.Text); if (merged is null) return;
        if (!SourceFiles.Equal(disk.Baseline, SourceFiles.Read(_root, doc.Path))) throw new IOException(T("核对期间磁盘再次变化", "Disk changed during review"));
        doc.Data = disk with { ConvertEncoding = doc.Data.ConvertEncoding }; doc.Conflict = false; doc.Editor.Text = merged; doc.Dirty = true; await SaveAllAsync();
    });
    private async void Discard_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (Current is not { } doc || MessageBox.Show(this, doc.Path + "\n" + T("丢弃该文件源码草稿？", "Discard the source draft for this file?"), Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        await _store.RemoveAsync(SourceEditing.Operation(doc.Path, doc.Data).Id); doc.Dirty = false;
        _documents.Remove(doc.Path); Documents.Items.Remove(doc.Tab); _main.SourceDraftsChanged(); BuildTree();
    });
    private async void History_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        await SaveAllAsync(); var journals = SourceTransactions.List(_root);
        var chosen = SourceDialogs.ChooseJournal(this, journals); if (chosen is null) return;
        if (chosen.State == "Completed" && _store.Operations.Any(o => o.TargetKind == DraftTargetKind.SourceFile))
        {
            if (_store.Operations.Where(o => o.TargetKind == DraftTargetKind.SourceFile).All(o => chosen.Drafts.TryGetValue(o.Id, out var time) && time == o.UpdatedUtc)) { await SourceTransactions.Cleanup(chosen, _store); _main.SourceDraftsChanged(); SetOutput(T("已收尾已提交草稿。再次打开可恢复备份。", "Committed drafts cleaned up. Open history again to restore.")); return; }
            throw new InvalidOperationException(T("恢复已完成批次前，请先处理源码草稿。", "Resolve source drafts before restoring a completed transaction."));
        }
        if (MessageBox.Show(this, chosen.Id + "\n" + T("恢复到该批次修改前？", "Restore files to their state before this transaction?"), Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        await Task.Run(() => SourceTransactions.Restore(_root, chosen.Id));
        foreach (var doc in _documents.Values.Where(d => !_store.Operations.Any(o => o.TargetKind == DraftTargetKind.SourceFile && o.RelativeSourceFile.Equals(d.Path, StringComparison.OrdinalIgnoreCase))))
        { doc.Data = SourceFiles.Open(_root, doc.Path); doc.Editor.Text = doc.Data.Text; doc.Dirty = false; UpdateHeader(doc); }
        await _main.RefreshSourceProjectAsync(new(_root, chosen.Id, DateTimeOffset.UtcNow, [], [], []));
        await RefreshAsync();
        SetOutput(T("恢复完成。请使用三方核对处理仍保留的源码草稿。", "Restored. Use three-way review for any retained source drafts."));
    });
    private async void Generate_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        await _main.FlushBusinessInputsAsync(); await SaveAllAsync();
        if (!_main.CanRunGenerate) throw new InvalidOperationException(_main.GenerateAvailability);
        if (MessageBox.Show(this, T("生成只读取正式文件，未应用草稿不会进入生成结果。继续？", "Generation reads formal files only; unapplied drafts are excluded. Continue?"), Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var result = await _main.RunGenerateAsync(); SetOutput(result.CombinedOutput + "\nExit: " + result.ExitCode);
    });
    private void Legacy_Click(object sender, RoutedEventArgs e) { if (_main.HasOpenProject) new Advanced.AdvancedWindow(_main) { Owner = this }.ShowDialog(); }
    private string Word()
    {
        if (Current is not { } doc) return ""; if (doc.Editor.SelectionLength > 0) return doc.Editor.SelectedText;
        var text = doc.Editor.Text; var start = doc.Editor.CaretOffset; var end = start;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_')) start--;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_')) end++;
        return text[start..end];
    }
    private async void Definition_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var word = Word(); await IndexAsync(); var choices = _symbols.Where(s => s.Name == word).ToArray();
        var choice = choices.Length == 1 ? choices[0] : choices.Length > 1 ? SourceDialogs.ChooseSymbol(this,choices) : null;
        if (choice is not null) await OpenFileAsync(choice.File,choice.Offset);
        else if (choices.Length == 0) SetOutput(T("当前索引未找到定义；索引可能不完整。", "No definition in the current index; coverage may be incomplete."));
    }, freeze:false);
    private async void References_Click(object sender, RoutedEventArgs e) { Query.Text = Word(); CurrentOnly.IsChecked = false; await SearchAsync(); Status.Text += T(" · 文本使用位置，非完整语义引用", " · Text usages, not complete semantic references"); }
    private async void Symbols_Open(object sender, MouseButtonEventArgs e) { if (Symbols.SelectedItem is SourceSymbol symbol) await Run(() => OpenFileAsync(symbol.File, symbol.Offset)); }
    private async void Back_Click(object sender, RoutedEventArgs e) { if (_navigation.TryPop(out var prior)) await Run(() => OpenFileAsync(prior.Path, prior.Offset, remember: false)); }
    private void Business_Click(object sender, RoutedEventArgs e)
    { if (Current is { } doc) { var obj = _main.ProjectObjects.FirstOrDefault(o => o.RelativeSourceFile.Equals(doc.Path, StringComparison.OrdinalIgnoreCase) && o.CharacterOffset <= doc.Editor.CaretOffset && o.CharacterOffset + o.CharacterLength >= doc.Editor.CaretOffset); if (obj is not null) { _main.NavigateObject(obj); Owner?.Activate(); } } }
    private async void Shortcuts(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F12) { Definition_Click(sender, e); e.Handled = true; }
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (e.Key == Key.S) { e.Handled = true; await Run(SaveAllAsync); }
        if (e.Key == Key.H || e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { e.Handled = true; Query.Focus(); Query.SelectAll(); }
        if (e.Key == Key.G && Current is { } doc) { e.Handled = true; var value = SourceDialogs.Prompt(this, T("跳至行", "Go to line"), "1"); if (int.TryParse(value, out var line)) { line = Math.Clamp(line, 1, doc.Editor.Document.LineCount); doc.Editor.CaretOffset = doc.Editor.Document.GetLineByNumber(line).Offset; doc.Editor.ScrollToLine(line); } }
        if (e.Key == Key.Space && Current is { } current)
        {
            e.Handled = true; var completion = new CompletionWindow(current.Editor.TextArea);
            var preceding = current.Editor.Document.GetText(Math.Max(0,current.Editor.CaretOffset-3),Math.Min(3,current.Editor.CaretOffset));
            var fields = WarnoLiteModdingTool.Core.Units.UnitFieldDefinitions.All.Select(f=>f.Selector.FieldName).Distinct().ToHashSet();
            var types = _symbols.Select(s=>s.Type).Where(t=>t!="value").ToHashSet();
            var references = _symbols.GroupBy(s=>"~/"+s.Name).ToDictionary(g=>g.Key,g=>g.First());
            foreach (var word in _words.OrderBy(w=>preceding.EndsWith("~/") ? w.StartsWith("~/")?0:1 : fields.Contains(w)?0:1))
            {
                var reference = references.GetValueOrDefault(word);
                var kind=reference is not null ? T("对象引用","Object reference") : types.Contains(word) ? T("构造类型","Constructor type") : fields.Contains(word) ? T("已知字段","Known field") : T("项目观察词","Observed word");
                completion.CompletionList.CompletionData.Add(new Completion(word,kind,reference is null ? T("来自当前项目观察；不代表完整语法定义","Observed in this project; not a complete language definition") : reference.File+":"+reference.Line, preceding.EndsWith("~/") && word.StartsWith("~/") ? word[2..] : word));
            }
            if (completion.CompletionList.CompletionData.Count > 0) completion.Show();
        }
    }
    private sealed class Completion(string text, string kind, string description, string insertion) : ICompletionData
    {
        public ImageSource? Image => null; public string Text => text; public object Content => text+" · "+kind; public object Description => description; public double Priority => 0;
        public void Complete(TextArea textArea, ISegment segment, EventArgs e) => textArea.Document.Replace(segment, insertion);
    }
    private sealed class DocumentSession(string path, SourceFileDraft data, TextEditor editor, TabItem tab)
    {
        public string Path { get; } = path; public SourceFileDraft Data { get; set; } = data; public TextEditor Editor { get; } = editor; public TabItem Tab { get; } = tab;
        public bool Dirty { get; set; } public int Revision { get; set; }
        public bool Saving { get; set; } public bool Conflict { get; set; } public bool AppliedPending { get; set; }
        public string LineEndings { get; } = data.Text.Contains("\r\n") ? data.Text.Replace("\r\n", "").Contains('\n') ? "CRLF/LF" : "CRLF" : "LF";
        public int AnalysisRevision { get; set; } = -1; public IReadOnlyList<SourceSymbol> Symbols { get; set; } = []; public CancellationTokenSource? AnalysisCancellation { get; set; }
        public FoldingManager Folding { get; } = FoldingManager.Install(editor.TextArea);
    }
}
