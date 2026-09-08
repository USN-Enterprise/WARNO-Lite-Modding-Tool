using System.Text;

namespace WarnoLiteModdingTool.Core.Ndf;

public sealed class NdfTopLevelScanner
{
    public NdfScanResult Scan(
        string source,
        string sourceFile,
        string moduleKey,
        string? projectRoot = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);

        var objects = new List<NdfObjectInfo>();
        var diagnostics = new List<NdfDiagnostic>();
        var lineStarts = BuildLineStarts(source);
        var cursor = 0;
        var lastByteCharacter = 0;
        long byteCursor = 0;

        while (TryFindNextDeclaration(source, cursor, out var declaration, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (declaration.ErrorMessage is not null)
            {
                diagnostics.Add(CreateDiagnostic(
                    sourceFile,
                    declaration.StartOffset,
                    lineStarts,
                    NdfDiagnosticSeverity.Warning,
                    declaration.ErrorMessage));
                cursor = declaration.ResumeOffset;
                continue;
            }

            var closingOffset = FindMatchingParenthesis(source, declaration.OpeningOffset, cancellationToken);
            if (closingOffset < 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    sourceFile,
                    declaration.StartOffset,
                    lineStarts,
                    NdfDiagnosticSeverity.Error,
                    $"对象 {declaration.Name} 的圆括号未闭合。"));
                break;
            }

            byteCursor += Encoding.UTF8.GetByteCount(source.AsSpan(lastByteCharacter, declaration.StartOffset - lastByteCharacter));
            var byteLength = Encoding.UTF8.GetByteCount(source.AsSpan(declaration.StartOffset, closingOffset - declaration.StartOffset + 1));
            var relativeFile = projectRoot is null
                ? sourceFile
                : Path.GetRelativePath(projectRoot, sourceFile);

            objects.Add(new NdfObjectInfo(
                moduleKey,
                declaration.Name,
                NameHumanizer.Humanize(declaration.Name),
                declaration.TypeName,
                sourceFile,
                relativeFile,
                declaration.StartOffset,
                closingOffset - declaration.StartOffset + 1,
                byteCursor,
                byteLength,
                FindLineNumber(lineStarts, declaration.StartOffset)));

            byteCursor += byteLength;
            lastByteCharacter = closingOffset + 1;
            cursor = closingOffset + 1;
        }

        return new NdfScanResult(objects, diagnostics);
    }

    private static bool TryFindNextDeclaration(
        string source,
        int start,
        out DeclarationCandidate declaration,
        CancellationToken cancellationToken)
    {
        var lineStart = Math.Max(0, start);
        if (lineStart > 0 && source[lineStart - 1] is not '\r' and not '\n')
        {
            lineStart = NextLineStart(source, lineStart);
        }

        while (lineStart < source.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parseCursor = lineStart;
            while (parseCursor < source.Length && source[parseCursor] is ' ' or '\t' or '\f')
            {
                parseCursor++;
            }

            if (parseCursor >= source.Length)
            {
                break;
            }

            if (source[parseCursor] is '\r' or '\n' ||
                (source[parseCursor] == '/' && parseCursor + 1 < source.Length && source[parseCursor + 1] == '/'))
            {
                lineStart = NextLineStart(source, parseCursor);
                continue;
            }

            var declarationStart = parseCursor;
            var firstToken = ReadToken(source, ref parseCursor);
            var hasExportKeyword = string.Equals(firstToken, "export", StringComparison.Ordinal);
            string name;
            if (hasExportKeyword)
            {
                SkipTrivia(source, ref parseCursor);
                name = ReadToken(source, ref parseCursor);
                if (name.Length == 0)
                {
                    declaration = DeclarationCandidate.Invalid(
                        declarationStart,
                        NextLineStart(source, lineStart),
                        "export 后缺少对象名称。");
                    return true;
                }
            }
            else
            {
                name = firstToken;
            }

            SkipTrivia(source, ref parseCursor);
            if (!IsKeywordAt(source, parseCursor, "is"))
            {
                if (hasExportKeyword)
                {
                    declaration = DeclarationCandidate.Invalid(
                        declarationStart,
                        NextLineStart(source, lineStart),
                        $"对象 {name} 缺少 is 类型声明。");
                    return true;
                }

                lineStart = NextLineStart(source, lineStart);
                continue;
            }

            parseCursor += 2;
            SkipTrivia(source, ref parseCursor);
            var valueStart = parseCursor;
            if (IsConstantValue(source, valueStart))
            {
                var end = FindConstantEnd(source, valueStart, cancellationToken);
                if (end < 0)
                {
                    declaration = DeclarationCandidate.Invalid(declarationStart, source.Length, $"常量 {name} 的引号或括号未闭合。");
                    return true;
                }
                lineStart = NextLineStart(source, end);
                continue;
            }
            var typeName = ReadToken(source, ref parseCursor);
            if (typeName.Length == 0)
            {
                declaration = DeclarationCandidate.Invalid(
                    declarationStart,
                    NextLineStart(source, lineStart),
                    $"对象 {name} 缺少类型名称。");
                return true;
            }

            SkipTrivia(source, ref parseCursor);
            if (string.Equals(typeName, "MAP", StringComparison.Ordinal) &&
                parseCursor < source.Length && source[parseCursor] == '[')
            {
                var closingBracket = FindMatchingDelimiter(source, parseCursor, '[', ']', cancellationToken);
                if (closingBracket < 0)
                {
                    declaration = DeclarationCandidate.Invalid(
                        declarationStart,
                        source.Length,
                        $"MAP {name} 的方括号未闭合。");
                    return true;
                }

                lineStart = NextLineStart(source, closingBracket + 1);
                continue;
            }

            if (parseCursor >= source.Length || source[parseCursor] != '(')
            {
                declaration = DeclarationCandidate.Invalid(
                    declarationStart,
                    NextLineStart(source, lineStart),
                    $"对象 {name} 的类型声明后缺少左圆括号。");
                return true;
            }

            declaration = DeclarationCandidate.Valid(declarationStart, name, typeName, parseCursor);
            return true;
        }

        declaration = default;
        return false;
    }

    // Constants are not descriptor objects. Recognize their RHS rather than
    // suppressing the missing-constructor-parenthesis diagnostic globally.
    private static bool IsConstantValue(string source, int start)
    {
        if (start >= source.Length) return false;
        var first = source[start];
        if (char.IsDigit(first) || first is '+' or '-' or '.' or '\'' or '"' or '[' or '(') return true;
        var cursor = start;
        var token = ReadToken(source, ref cursor);
        if (token.Equals("true", StringComparison.OrdinalIgnoreCase) || token.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith("~/", StringComparison.Ordinal) || token.StartsWith("$/", StringComparison.Ordinal) || token.Contains('/')) return true;
        if (token.Contains('*') || token.Contains('+')) return true;
        // An identifier followed by an arithmetic operator is an expression;
        // a constructor type without '(' still produces a diagnostic.
        while (cursor < source.Length && source[cursor] is ' ' or '\t') cursor++;
        return cursor < source.Length && (source[cursor] is '+' or '-' or '*' || source[cursor] == '/' && (cursor + 1 >= source.Length || source[cursor + 1] != '/'));
    }

    private static int FindConstantEnd(string source, int start, CancellationToken cancellationToken)
    {
        var delimiters = new Stack<char>();
        var quote = '\0'; var escaped = false; var comment = false;
        for (var i = start; i < source.Length; i++)
        {
            if ((i & 0x3fff) == 0) cancellationToken.ThrowIfCancellationRequested();
            var c = source[i];
            if (quote != '\0')
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == quote) quote = '\0';
                continue;
            }
            if (c is '\r' or '\n')
            {
                comment = false;
                if (delimiters.Count == 0) return i;
                continue;
            }
            if (comment) continue;
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/') { comment = true; i++; continue; }
            if (c is '\'' or '"') { quote = c; continue; }
            if (c is '[' or '(') delimiters.Push(c);
            else if (c is ']' or ')')
            {
                if (delimiters.Count == 0 || delimiters.Pop() != (c == ']' ? '[' : '(')) return -1;
            }
        }
        return quote == '\0' && delimiters.Count == 0 ? source.Length : -1;
    }

    private static int NextLineStart(string source, int offset)
    {
        var cursor = Math.Max(0, offset);
        while (cursor < source.Length && source[cursor] is not '\r' and not '\n')
        {
            cursor++;
        }

        if (cursor < source.Length && source[cursor] == '\r')
        {
            cursor++;
        }

        if (cursor < source.Length && source[cursor] == '\n')
        {
            cursor++;
        }

        return cursor;
    }

    private static int FindMatchingParenthesis(
        string source,
        int openingOffset,
        CancellationToken cancellationToken) =>
        FindMatchingDelimiter(source, openingOffset, '(', ')', cancellationToken);

    private static int FindMatchingDelimiter(
        string source,
        int openingOffset,
        char openingDelimiter,
        char closingDelimiter,
        CancellationToken cancellationToken)
    {
        var depth = 0;
        var quote = '\0';
        var escaped = false;
        var lineComment = false;

        for (var index = openingOffset; index < source.Length; index++)
        {
            if ((index & 0x3FFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var character = source[index];
            if (lineComment)
            {
                if (character is '\r' or '\n')
                {
                    lineComment = false;
                }

                continue;
            }

            if (quote != '\0')
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
                continue;
            }

            if (character == '/' && index + 1 < source.Length && source[index + 1] == '/')
            {
                lineComment = true;
                index++;
                continue;
            }

            if (character == openingDelimiter)
            {
                depth++;
            }
            else if (character == closingDelimiter && --depth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static void SkipTrivia(string source, ref int cursor)
    {
        while (cursor < source.Length)
        {
            if (char.IsWhiteSpace(source[cursor]))
            {
                cursor++;
                continue;
            }

            if (source[cursor] == '/' && cursor + 1 < source.Length && source[cursor + 1] == '/')
            {
                cursor += 2;
                while (cursor < source.Length && source[cursor] is not '\r' and not '\n')
                {
                    cursor++;
                }

                continue;
            }

            break;
        }
    }

    private static string ReadToken(string source, ref int cursor)
    {
        var start = cursor;
        while (cursor < source.Length)
        {
            var character = source[cursor];
            if (char.IsWhiteSpace(character) || character is '(' or ')' or '[' or ']' or ',' || character == '/' && cursor + 1 < source.Length && source[cursor + 1] == '/')
            {
                break;
            }

            cursor++;
        }

        return source[start..cursor];
    }

    private static bool IsKeywordAt(string source, int offset, string keyword)
    {
        if (offset < 0 || offset + keyword.Length > source.Length ||
            !source.AsSpan(offset, keyword.Length).Equals(keyword, StringComparison.Ordinal))
        {
            return false;
        }

        var beforeIsBoundary = offset == 0 || !IsIdentifierCharacter(source[offset - 1]);
        var after = offset + keyword.Length;
        var afterIsBoundary = after >= source.Length || !IsIdentifierCharacter(source[after]);
        return beforeIsBoundary && afterIsBoundary;
    }

    private static bool IsIdentifierCharacter(char character) =>
        char.IsLetterOrDigit(character) || character == '_';

    private static int[] BuildLineStarts(string source)
    {
        var starts = new List<int> { 0 };
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] == '\n')
            {
                starts.Add(index + 1);
            }
        }

        return starts.ToArray();
    }

    private static int FindLineNumber(int[] lineStarts, int offset)
    {
        var result = Array.BinarySearch(lineStarts, offset);
        return result >= 0 ? result + 1 : ~result;
    }

    private static NdfDiagnostic CreateDiagnostic(
        string sourceFile,
        int offset,
        int[] lineStarts,
        NdfDiagnosticSeverity severity,
        string message) =>
        new(sourceFile, offset, FindLineNumber(lineStarts, offset), severity, message);

    private readonly record struct DeclarationCandidate(
        int StartOffset,
        string Name,
        string TypeName,
        int OpeningOffset,
        int ResumeOffset,
        string? ErrorMessage)
    {
        public static DeclarationCandidate Valid(
            int startOffset,
            string name,
            string typeName,
            int openingOffset) =>
            new(startOffset, name, typeName, openingOffset, openingOffset + 1, null);

        public static DeclarationCandidate Invalid(
            int startOffset,
            int resumeOffset,
            string errorMessage) =>
            new(startOffset, string.Empty, string.Empty, -1, resumeOffset, errorMessage);
    }
}
