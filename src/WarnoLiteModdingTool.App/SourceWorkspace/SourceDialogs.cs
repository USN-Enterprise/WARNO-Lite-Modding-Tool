using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit;
using WarnoLiteModdingTool.App.Controls;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.Core.SourceWorkspace;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.App.SourceWorkspace;

internal static class SourceDialogs
{
    private static string T(string zh, string en) => UiText.Current.English ? en : zh;
    private static Window Window(Window owner, string title, double width = 1100, double height = 760)
    {
        var window = new Window { Owner = owner, Title = title, Width = Math.Min(width, SystemParameters.WorkArea.Width - 40), Height = Math.Min(height, SystemParameters.WorkArea.Height - 40), MinWidth = 500, MinHeight = 250, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.SetResourceReference(Control.BackgroundProperty, "BackgroundBrush"); window.SetResourceReference(Control.ForegroundProperty, "TextBrush"); return window;
    }
    public static string? Prompt(Window owner, string title, string value)
    {
        var window = Window(owner, title, 620, 260); var panel = new StackPanel { Margin = new Thickness(20) }; window.Content = panel;
        panel.Children.Add(DamageUi.Heading(title)); var input = new TextBox { Text = value, Margin = new Thickness(0, 12, 0, 12) }; panel.Children.Add(input);
        panel.Children.Add(DamageUi.Button(T("确定", "OK"), () => window.DialogResult = true)); input.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return window.ShowDialog() == true ? input.Text : null;
    }
    private static string Decode(byte[]? bytes, int codePage = 65001)
    { if (bytes is null) return ""; using var reader = new StreamReader(new MemoryStream(bytes), Encoding.GetEncoding(codePage), true); return reader.ReadToEnd(); }
    public static bool? Review(Window owner, ApplyPreview preview, bool draftsOnly = false)
    {
        var window = Window(owner, draftsOnly ? T("替换预览", "Replacement preview") : T("源码差异与应用", "Source comparison and apply"), 1250, 850);
        var dock = new DockPanel { Margin = new Thickness(14) }; dock.SetResourceReference(Panel.BackgroundProperty, "BackgroundBrush"); window.Content = dock;
        var bottom = new StackPanel { Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(bottom, Dock.Bottom); dock.Children.Add(bottom);
        var acknowledge = new CheckBox { Content = T("已核对全部文件差异，接受所列未验证内容，按原文应用", "I reviewed all file changes and accept the listed unverified content; apply the source text"), Margin = new Thickness(0, 6, 0, 6) };
        bottom.Children.Add(acknowledge); if (draftsOnly) acknowledge.Content = T("已核对选中替换，只写入草稿", "I reviewed the selected replacements; save drafts only"); var apply = DamageUi.Button(draftsOnly ? T("加入替换草稿", "Save replacement drafts") : T("备份并应用整个源码组", "Back up and apply the entire source group"), () => { preview.SourceAcknowledged = true; window.DialogResult = true; }); apply.IsEnabled = false;
        acknowledge.Checked += (_, _) => apply.IsEnabled = true; acknowledge.Unchecked += (_, _) => apply.IsEnabled = false; bottom.Children.Add(apply);
        var diagnostics = new TextBox { IsReadOnly = true, Text = string.Join("\n", preview.ValidationMessages) + "\n" + preview.BackupId, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 105, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(diagnostics, Dock.Top); dock.Children.Add(diagnostics);
        var selector = new ComboBox { ItemsSource = preview.Files, DisplayMemberPath = "RelativePath", Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(selector, Dock.Top); dock.Children.Add(selector);
        var before = SourceEditorStyle.Create(true); var after = SourceEditorStyle.Create(true);
        var comparison = Columns((draftsOnly ? T("替换前", "Before replacement") : T("正式原文", "Formal original"), before), (draftsOnly ? T("替换后", "After replacement") : T("应用后全文", "Complete result"), after));
        var controls = new WrapPanel(); DockPanel.SetDock(controls, Dock.Top); dock.Children.Add(controls);
        var info = new TextBlock { Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap };
        SourceDiffResult diff = new([], false); var selectedBlock = -1; CancellationTokenSource? cancellation = null; var syncing = false;
        void Navigate(int direction)
        {
            if (diff.Blocks.Count == 0) return; selectedBlock = (selectedBlock + direction + diff.Blocks.Count) % diff.Blocks.Count; var block = diff.Blocks[selectedBlock];
            syncing = true;
            window.Dispatcher.InvokeAsync(() =>
            {
                before.UpdateLayout(); after.UpdateLayout();
                var oldLine=Math.Min(block.BeforeLine,before.Document.LineCount);var newLine=Math.Min(block.AfterLine,after.Document.LineCount);
                before.CaretOffset=before.Document.GetLineByNumber(oldLine).Offset;after.CaretOffset=after.Document.GetLineByNumber(newLine).Offset;
                before.ScrollToLine(oldLine);after.ScrollToLine(newLine);
                before.TextArea.Caret.BringCaretToView();after.TextArea.Caret.BringCaretToView();
                window.Dispatcher.InvokeAsync(()=>syncing=false,System.Windows.Threading.DispatcherPriority.ContextIdle);
            },System.Windows.Threading.DispatcherPriority.Loaded);
            info.Text = $"{selectedBlock + 1}/{diff.Blocks.Count}" + (diff.Coarse ? T(" · 部分为粗粒度差异", " · Some regions use coarse comparison") : "");
        }
        controls.Children.Add(DamageUi.Button(T("上一处", "Previous change"), () => Navigate(-1)));
        controls.Children.Add(DamageUi.Button(T("下一处", "Next change"), () => Navigate(1)));
        controls.Children.Add(DamageUi.Button(T("取消比较，查看全文", "Cancel comparison; show full text"), () => { cancellation?.Cancel(); info.Text = T("比较已取消；全文仍可核对", "Comparison cancelled; full text remains available"); }));
        var single = new CheckBox { Content = T("单栏显示", "Single column"), Margin = new Thickness(8) }; var showOriginal = false;
        void Layout()
        {
            var narrow = single.IsChecked == true || window.ActualWidth < 900;
            comparison.ColumnDefinitions[0].Width = new GridLength(narrow && !showOriginal ? 0 : 1, narrow && !showOriginal ? GridUnitType.Pixel : GridUnitType.Star);
            comparison.ColumnDefinitions[1].Width = new GridLength(narrow && showOriginal ? 0 : 1, narrow && showOriginal ? GridUnitType.Pixel : GridUnitType.Star);
            comparison.Children[0].Visibility = narrow && !showOriginal ? Visibility.Collapsed : Visibility.Visible;
            comparison.Children[1].Visibility = narrow && showOriginal ? Visibility.Collapsed : Visibility.Visible;
        }
        controls.Children.Add(single); single.Checked += (_, _) => Layout(); single.Unchecked += (_, _) => Layout(); window.SizeChanged += (_, _) => Layout();
        controls.Children.Add(DamageUi.Button(T("单栏：切换前后", "Single column: switch side"), () => { single.IsChecked = true; showOriginal = !showOriginal; Layout(); }));
        controls.Children.Add(info); dock.Children.Add(comparison);
        var summary = new TextBlock { Text = $"{preview.Files.Count} " + T("文件 · 新建 ", "files · new ") + preview.Files.Count(f => !f.Existed) + T(" · 转码 ", " · encoding conversions ") + preview.Operations.Count(o => o.Source?.ConvertEncoding == true), Margin = new Thickness(0,4,0,4) }; bottom.Children.Insert(0,summary);
        before.TextArea.TextView.ScrollOffsetChanged += (_, _) => { if (syncing) return; syncing = true; after.ScrollToVerticalOffset(before.VerticalOffset); syncing = false; };
        after.TextArea.TextView.ScrollOffsetChanged += (_, _) => { if (syncing) return; syncing = true; before.ScrollToVerticalOffset(after.VerticalOffset); syncing = false; };
        window.Closed += (_, _) => cancellation?.Cancel();
        selector.SelectionChanged += async (_, _) =>
        {
            if (selector.SelectedItem is not PlannedFileChange file) return;
            cancellation?.Cancel(); var cancel = cancellation = new(); diff = new([],false); selectedBlock = -1;
            var data = preview.Operations.First(o => o.RelativeSourceFile == file.RelativePath).Source!;
            before.Text = Decode(file.Existed ? file.OriginalBytes : null, data.CodePage); after.Text = data.Text;
            SourceEditorStyle.Apply(before); SourceEditorStyle.Apply(after);
            before.TextArea.TextView.LineTransformers.Clear(); after.TextArea.TextView.LineTransformers.Clear();
            var original = before.Text; var result = after.Text; info.Text = T("正在比较…", "Comparing…");
            try
            {
                var comparisonResult = await Task.Run(() => SourceDiff.Compare(original, result, cancel.Token), cancel.Token);
                if (cancel.IsCancellationRequested) return; diff = comparisonResult;
                before.TextArea.TextView.LineTransformers.Add(new ChangedLines(diff.Blocks, false)); after.TextArea.TextView.LineTransformers.Add(new ChangedLines(diff.Blocks, true));
                before.TextArea.TextView.Redraw(); after.TextArea.TextView.Redraw();
                if (diff.Blocks.Count > 0) Navigate(1); else info.Text = T("正文相同；请核对编码/换行", "Text matches; check encoding/line endings");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!cancel.IsCancellationRequested) info.Text = T("比较未完成：", "Comparison incomplete: ") + ex.Message; }
        };
        selector.SelectedIndex = 0; return window.ShowDialog();
    }
    public static string? Merge(Window owner, string path, SourceFileDraft original, SourceFileDraft disk, string local)
    {
        var window = Window(owner, T("三方核对 · ", "Three-way review · ") + path, 1350, 800); var dock = new DockPanel { Margin = new Thickness(14) }; window.Content = dock;
        var baseEditor = SourceEditorStyle.Create(true); baseEditor.Text = Decode(original.Baseline, original.CodePage);
        var diskEditor = SourceEditorStyle.Create(true); diskEditor.Text = disk.Text;
        var merged = SourceEditorStyle.Create(); merged.Text = local;
        var actions = new WrapPanel(); DockPanel.SetDock(actions, Dock.Bottom); dock.Children.Add(actions);
        actions.Children.Add(DamageUi.Button(T("将磁盘内容填入合并区", "Use disk content in merge editor"), () => merged.Text = disk.Text));
        actions.Children.Add(DamageUi.Button(T("将本地内容填入合并区", "Use local content in merge editor"), () => merged.Text = local));
        SourceDiffResult differences = new([],false); var position = -1; using var cancellation = new CancellationTokenSource(); var comparisonToken=cancellation.Token;
        var state = new TextBlock { Margin = new Thickness(8), Text = T("比较磁盘与本地…", "Comparing disk and local…") }; actions.Children.Add(state);
        void Navigate(int direction) { if(differences.Blocks.Count==0)return; position=(position+direction+differences.Blocks.Count)%differences.Blocks.Count; var block=differences.Blocks[position]; diskEditor.ScrollToLine(Math.Min(block.BeforeLine,diskEditor.Document.LineCount)); merged.ScrollToLine(Math.Min(block.AfterLine,merged.Document.LineCount)); state.Text=$"{position+1}/{differences.Blocks.Count}"; }
        actions.Children.Add(DamageUi.Button(T("上一差异", "Previous difference"),()=>Navigate(-1)));
        actions.Children.Add(DamageUi.Button(T("下一差异", "Next difference"),()=>Navigate(1)));
        var analysisTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) }; var revision=0;
        analysisTimer.Tick += async (_,_) => { analysisTimer.Stop(); var captured=++revision; var text=merged.Text; try { var value=await Task.Run(()=>SourceDiff.Compare(disk.Text,text,comparisonToken),comparisonToken); if(captured!=revision || comparisonToken.IsCancellationRequested)return; differences=value; position=-1; state.Text=$"{value.Blocks.Count} " + T("处磁盘/合并差异", "disk/merge differences"); } catch(OperationCanceledException) { } };
        merged.TextChanged += (_,_) => { revision++; analysisTimer.Stop(); analysisTimer.Start(); }; analysisTimer.Start();
        window.Closed += (_,_)=> { cancellation.Cancel(); analysisTimer.Stop(); };
        actions.Children.Add(DamageUi.Button(T("保存合并草稿并更新基线", "Save merged draft and update baseline"), () => window.DialogResult = true));
        dock.Children.Add(Columns((T("编辑开始时", "Original baseline"), baseEditor), (T("当前磁盘", "Current disk"), diskEditor), (T("合并结果（可编辑）", "Merged result (editable)"), merged)));
        return window.ShowDialog() == true ? merged.Text : null;
    }
    public static SourceJournal? ChooseJournal(Window owner, IReadOnlyList<SourceJournal> journals)
    {
        var window = Window(owner, T("源码备份与恢复", "Source backups and recovery")); var dock = new DockPanel { Margin = new Thickness(14) }; window.Content = dock;
        var grid = DamageUi.Table((T("批次", "Transaction"), "Id"), (T("状态", "State"), "State"), (T("时间", "Time"), "Created")); grid.ItemsSource = journals;
        var button = DamageUi.Button(T("处理选中批次", "Process selected transaction"), () => { if (grid.SelectedItem is SourceJournal) window.DialogResult = true; }); DockPanel.SetDock(button, Dock.Bottom); dock.Children.Add(button); dock.Children.Add(grid);
        return window.ShowDialog() == true ? grid.SelectedItem as SourceJournal : null;
    }
    public static SourceSymbol? ChooseSymbol(Window owner, IReadOnlyList<SourceSymbol> symbols)
    {
        var window = Window(owner,T("选择定义", "Choose definition"),850,500); var panel = new DockPanel { Margin = new Thickness(14) }; window.Content = panel;
        var grid = DamageUi.Table((T("对象", "Object"),"Name"),(T("类型", "Type"),"Type"),(T("源文件", "Source file"),"File"),(T("行号", "Line"),"Line")); grid.ItemsSource=symbols;
        var choose = DamageUi.Button(T("跳转", "Go to"), () => { if(grid.SelectedItem is SourceSymbol)window.DialogResult=true; }); DockPanel.SetDock(choose,Dock.Bottom);panel.Children.Add(choose);panel.Children.Add(grid);
        grid.MouseDoubleClick += (_,_) => { if(grid.SelectedItem is SourceSymbol)window.DialogResult=true; };
        return window.ShowDialog()==true ? grid.SelectedItem as SourceSymbol : null;
    }
    private static Grid Columns(params (string Label, TextEditor Editor)[] columns)
    {
        var grid = new Grid();
        for (var i = 0; i < columns.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition()); var panel = new DockPanel { Margin = new Thickness(4) }; Grid.SetColumn(panel, i); grid.Children.Add(panel);
            var title = DamageUi.Heading(columns[i].Label); DockPanel.SetDock(title, Dock.Top); panel.Children.Add(title); panel.Children.Add(columns[i].Editor);
        }
        return grid;
    }
    private sealed class ChangedLines(IReadOnlyList<SourceDiffBlock> blocks, bool added) : ICSharpCode.AvalonEdit.Rendering.DocumentColorizingTransformer
    {
        protected override void ColorizeLine(ICSharpCode.AvalonEdit.Document.DocumentLine line)
        {
            if (line.Length > 0 && blocks.Any(b => added ? line.LineNumber >= b.AfterLine && line.LineNumber < b.AfterLine + b.AfterCount : line.LineNumber >= b.BeforeLine && line.LineNumber < b.BeforeLine + b.BeforeCount))
                ChangeLinePart(line.Offset, line.EndOffset, e => e.TextRunProperties.SetBackgroundBrush(new System.Windows.Media.SolidColorBrush(added ? System.Windows.Media.Color.FromArgb(55, 50, 180, 100) : System.Windows.Media.Color.FromArgb(55, 220, 80, 80))));
        }
    }
}
