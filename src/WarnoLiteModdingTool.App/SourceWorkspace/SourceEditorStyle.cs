using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Rendering;
using WarnoLiteModdingTool.Core.Ndf;

namespace WarnoLiteModdingTool.App.SourceWorkspace;

internal static class SourceEditorStyle
{
    public static TextEditor Create(bool readOnly = false)
    {
        var editor = new TextEditor { FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"), FontSize = 14, ShowLineNumbers = true, IsReadOnly = readOnly, Padding = new Thickness(8), HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto };
        editor.Options.EnableHyperlinks = false; editor.Options.EnableEmailHyperlinks = false;
        editor.Options.ConvertTabsToSpaces = false; editor.Options.HighlightCurrentLine = true;
        var brackets = new Brackets(); editor.TextArea.TextView.LineTransformers.Add(brackets);
        var pairs = new Dictionary<int, int>(); var revision = 0; CancellationTokenSource? analysis = null;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        void Paint()
        {
            brackets.Positions.Clear();
            foreach (var offset in new[] { editor.CaretOffset, editor.CaretOffset - 1 })
                if (pairs.TryGetValue(offset, out var partner)) { brackets.Positions.Add(offset); brackets.Positions.Add(partner); break; }
            editor.TextArea.TextView.Redraw();
        }
        timer.Tick += async (_, _) =>
        {
            timer.Stop(); analysis?.Cancel(); var cancel = analysis = new(); var captured = revision;
            if (editor.Document.TextLength > 2_000_000) { pairs.Clear(); Paint(); return; }
            var text = editor.Text;
            try
            {
                var result = await Task.Run(() =>
                {
                    var map = new Dictionary<int, int>(); var stack = new Stack<NdfSyntaxDocument.NdfToken>();
                    foreach (var token in new NdfSyntaxDocument(text,cancellationToken:cancel.Token).Tokens)
                    {
                        cancel.Token.ThrowIfCancellationRequested();
                        if (token.Text is "(" or "[") stack.Push(token);
                        else if (token.Text is ")" or "]" && stack.TryPop(out var open) && open.Text == (token.Text == ")" ? "(" : "[")) { map[open.Start] = token.Start; map[token.Start] = open.Start; }
                    }
                    return map;
                }, cancel.Token);
                if (cancel.IsCancellationRequested || captured != revision) return; pairs = result; Paint();
            }
            catch (OperationCanceledException) { }
            catch (InvalidDataException) { if (!cancel.IsCancellationRequested && captured == revision) { pairs.Clear(); Paint(); } }
        };
        editor.TextChanged += (_, _) => { revision++; pairs.Clear(); analysis?.Cancel(); timer.Stop(); timer.Start(); };
        editor.Loaded += (_, _) => timer.Start();
        editor.Unloaded += (_, _) => { timer.Stop(); analysis?.Cancel(); };
        editor.TextArea.Caret.PositionChanged += (_, _) => Paint();
        Apply(editor); return editor;
    }
    public static void Apply(TextEditor editor)
    {
        editor.SetResourceReference(TextEditor.BackgroundProperty, "SurfaceBrush"); editor.SetResourceReference(TextEditor.ForegroundProperty, "TextBrush");
        editor.SetResourceReference(TextEditor.LineNumbersForegroundProperty, "MutedTextBrush");
        if (editor.Document.TextLength > 2_000_000) { editor.SyntaxHighlighting = null; return; }
        var dark = Theming.ThemeManager.IsDark;
        var keyword = dark ? "#79B8FF" : "#1556A6"; var str = dark ? "#E5BC86" : "#875016"; var comment = dark ? "#9BB8A0" : "#4E7052";
        var xml = """
        <SyntaxDefinition name="NDF" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="Keyword" foreground="KEY" fontWeight="bold"/><Color name="String" foreground="STR"/><Color name="Comment" foreground="COM"/>
          <RuleSet>
            <Span color="Comment" begin="//" end="\n"/><Span color="Comment" multiline="true" begin="/\*" end="\*/"/>
            <Span color="String" begin="&quot;" end="&quot;"><RuleSet><Span begin="\\." end=""/></RuleSet></Span>
            <Span color="String" begin="'" end="'"><RuleSet><Span begin="\\." end=""/></RuleSet></Span>
            <Keywords color="Keyword"><Word>is</Word><Word>export</Word><Word>private</Word><Word>template</Word><Word>MAP</Word><Word>True</Word><Word>False</Word><Word>nil</Word></Keywords>
          </RuleSet>
        </SyntaxDefinition>
        """.Replace("KEY", keyword).Replace("STR", str).Replace("COM", comment);
        using var reader = XmlReader.Create(new StringReader(xml)); editor.SyntaxHighlighting = HighlightingLoader.Load(reader, null);
    }
    private sealed class Brackets : DocumentColorizingTransformer
    {
        public List<int> Positions { get; } = [];
        protected override void ColorizeLine(ICSharpCode.AvalonEdit.Document.DocumentLine line)
        { foreach (var p in Positions.Where(p => p >= line.Offset && p < line.EndOffset)) ChangeLinePart(p, p + 1, e => e.TextRunProperties.SetBackgroundBrush(new SolidColorBrush(Color.FromArgb(110, 70, 140, 220)))); }
    }
}
