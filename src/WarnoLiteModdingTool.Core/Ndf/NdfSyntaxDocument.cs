namespace WarnoLiteModdingTool.Core.Ndf;

public sealed class NdfSyntaxDocument
{
    private readonly string _source;
    private readonly List<NdfToken> _tokens;
    private readonly object _cacheGate = new();
    private readonly Dictionary<(string Type, int Start, int End), IReadOnlyList<NdfConstructorSpan>> _constructors = new();
    private readonly Dictionary<NdfConstructorSpan, IReadOnlyList<NdfAssignmentSpan>> _assignments = new();
    private readonly Dictionary<(int Index, string Open, string Close), int> _matching = new();
    private Dictionary<string, List<int>>? _constructorStarts;
    public IReadOnlyList<NdfToken> Tokens => _tokens;

    private NdfSyntaxDocument(string source, List<NdfToken> tokens) { _source = source; _tokens = tokens; }

    /// <summary>Object view over immutable, already tokenized source. Offsets stay file-relative.</summary>
    public NdfSyntaxDocument Slice(int startOffset, int length)
    {
        if (startOffset < 0 || length < 0 || startOffset > _source.Length - length) throw new ArgumentOutOfRangeException(nameof(startOffset));
        var first = TokenIndexAtOrAfter(startOffset);
        var last = TokenIndexAtOrAfter(startOffset + length);
        if (last > first && _tokens[last - 1].End > startOffset + length) throw new ArgumentException("Slice splits a token.");
        return new(_source, _tokens.GetRange(first, last - first));
    }

    public int TokenIndexAtOrAfter(int offset)
    {
        var low = 0; var high = _tokens.Count;
        while (low < high) { var mid = low + (high - low) / 2; if (_tokens[mid].Start < offset) low = mid + 1; else high = mid; }
        return low;
    }

    public NdfSyntaxDocument(string source, int startOffset = 0, int? length = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (startOffset < 0 || startOffset > source.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(startOffset));
        }

        var actualLength = length ?? source.Length - startOffset;
        if (actualLength < 0 || startOffset + actualLength > source.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        _source = source;
        Projects.AnalysisMetrics.Current?.Syntax(actualLength);
        _tokens = Tokenize(source, startOffset, actualLength, cancellationToken);
    }

    public IReadOnlyList<NdfConstructorSpan> FindConstructors(string typeName) =>
        FindConstructors(typeName, 0, _tokens.Count - 1);

    public IReadOnlyList<NdfConstructorSpan> FindConstructors(
        string typeName,
        NdfValueSpan within) =>
        FindConstructors(typeName, within.StartTokenIndex, within.EndTokenIndex);

    public IReadOnlyList<NdfConstructorSpan> FindConstructors(
        string typeName,
        int startTokenIndex,
        int endTokenIndex)
    {
        lock (_cacheGate)
        {
            var key = (typeName, startTokenIndex, endTokenIndex);
            if (!_constructors.TryGetValue(key, out var result))
                _constructors[key] = result = FindConstructorsCore(typeName, startTokenIndex, endTokenIndex);
            return result;
        }
    }

    private IReadOnlyList<NdfConstructorSpan> FindConstructorsCore(string typeName, int startTokenIndex, int endTokenIndex)
    {
        var result = new List<NdfConstructorSpan>();
        if (_tokens.Count == 0 || startTokenIndex > endTokenIndex)
        {
            return result;
        }

        var start = Math.Max(0, startTokenIndex);
        var end = Math.Min(_tokens.Count - 1, endTokenIndex);
        if (_constructorStarts is null)
        {
            var starts = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (var i = 0; i + 1 < _tokens.Count; i++)
            {
                if ((i & 1023) == 0) Projects.ProjectReadScope.Current?.Cancellation.ThrowIfCancellationRequested();
                if (_tokens[i + 1].Text != "(") continue;
                if (!starts.TryGetValue(_tokens[i].Text, out var positions)) starts[_tokens[i].Text] = positions = [];
                positions.Add(i);
            }
            _constructorStarts = starts;
        }
        // Preserve the original outermost-of-the-requested-type behavior, including
        // bounded queries and malformed/unclosed candidates.
        var next = start;
        foreach (var index in _constructorStarts.GetValueOrDefault(typeName) ?? [])
        {
            if (index < next) continue;
            if (index >= end) break;
            var closing = FindMatching(index + 1, "(", ")", end);
            if (closing >= 0)
            {
                result.Add(new NdfConstructorSpan(typeName, index, index + 1, closing));
                next = closing + 1;
            }
        }

        return result;
    }

    public IReadOnlyList<NdfValueSpan> FindDirectAssignments(
        NdfConstructorSpan constructor,
        string fieldName)
    {
        var assignments = EnumerateDirectAssignments(constructor);
        return assignments
            .Where(item => string.Equals(item.Name, fieldName, StringComparison.Ordinal))
            .Select(item => item.Value)
            .ToArray();
    }

    public IReadOnlyList<NdfValueSpan> FindAssignmentsAnywhere(string fieldName)
    {
        var result = new List<NdfValueSpan>();
        for (var index = 0; index + 2 < _tokens.Count; index++)
        {
            if (!string.Equals(_tokens[index].Text, fieldName, StringComparison.Ordinal) ||
                _tokens[index + 1].Text != "=")
            {
                continue;
            }

            result.Add(new NdfValueSpan(index + 2, index + 2));
        }

        return result;
    }

    public bool NeedsArraySeparator(NdfValueSpan value) => value.EndTokenIndex>value.StartTokenIndex && _tokens[value.EndTokenIndex].Text=="]" && _tokens[value.EndTokenIndex-1].Text is not ("[" or ",");

    public IReadOnlyList<NdfMapEntry> ReadMapEntries(NdfValueSpan value)
    {
        var result = new List<NdfMapEntry>();
        var opening = FindToken("[", value.StartTokenIndex, value.EndTokenIndex);
        if (opening < 0)
        {
            return result;
        }

        var closing = FindMatching(opening, "[", "]", value.EndTokenIndex);
        if (closing < 0)
        {
            return result;
        }

        var index = opening + 1;
        while (index < closing)
        {
            if (_tokens[index].Text != "(")
            {
                index++;
                continue;
            }

            var tupleClose = FindMatching(index, "(", ")", closing);
            if (tupleClose < 0)
            {
                break;
            }

            var comma = FindTopLevelComma(index + 1, tupleClose - 1);
            if (comma > index + 1 && comma < tupleClose - 1)
            {
                result.Add(new NdfMapEntry(
                    new NdfValueSpan(index + 1, comma - 1),
                    new NdfValueSpan(comma + 1, tupleClose - 1)));
            }

            index = tupleClose + 1;
        }

        return result;
    }

    public IReadOnlyList<NdfValueSpan> ReadArrayElements(NdfValueSpan value)
    {
        var opening = FindToken("[", value.StartTokenIndex, value.EndTokenIndex);
        if (opening < 0)
        {
            return [];
        }

        var closing = FindMatching(opening, "[", "]", value.EndTokenIndex);
        if (closing < 0)
        {
            return [];
        }

        var result = new List<NdfValueSpan>();
        var elementStart = opening + 1;
        var parentheses = 0;
        var brackets = 0;
        for (var index = elementStart; index < closing; index++)
        {
            switch (_tokens[index].Text)
            {
                case "(": parentheses++; break;
                case ")": parentheses--; break;
                case "[": brackets++; break;
                case "]": brackets--; break;
                case "," when parentheses == 0 && brackets == 0:
                    if (elementStart <= index - 1)
                    {
                        result.Add(new NdfValueSpan(elementStart, index - 1));
                    }

                    elementStart = index + 1;
                    break;
            }
        }

        if (elementStart <= closing - 1)
        {
            result.Add(new NdfValueSpan(elementStart, closing - 1));
        }

        return result;
    }

    public IReadOnlyList<string> FindReferenceLeaves(string prefix)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in _tokens)
        {
            var leaf = Leaf(Unquote(token.Text));
            if (leaf.StartsWith(prefix, StringComparison.Ordinal))
            {
                values.Add(leaf);
            }
        }

        return values.Order(StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<NdfReferenceSpan> FindReferences(string prefix)
    {
        var result = new List<NdfReferenceSpan>();
        for (var index = 0; index < _tokens.Count; index++)
        {
            var raw = _tokens[index].Text;
            var leaf = Leaf(Unquote(raw));
            if (leaf.StartsWith(prefix, StringComparison.Ordinal))
            {
                result.Add(new NdfReferenceSpan(leaf, raw, new NdfValueSpan(index, index)));
            }
        }

        return result;
    }

    public IReadOnlyList<NdfValueSpan> FindNamedValues(string name)
    {
        var result = new List<NdfValueSpan>();
        var depth = 0;
        for (var i = 0; i + 2 < _tokens.Count; i++)
        {
            var text = _tokens[i].Text;
            if (text is "(" or "[") { depth++; continue; }
            if (text is ")" or "]") { depth--; continue; }
            if (depth != 0 || text != name || _tokens[i + 1].Text != "is") continue;
            var start = i + 2; var end = start;
            var open = _tokens[start].Text == "[" ? start : start + 1;
            if (open < _tokens.Count && _tokens[open].Text is "(" or "[")
            {
                var symbol = _tokens[open].Text;
                end = FindMatching(open, symbol, symbol == "(" ? ")" : "]", _tokens.Count - 1);
                if (end < 0) continue;
            }
            result.Add(new(start, end));
        }
        return result;
    }

    public IReadOnlyList<NdfNamedMapSpan> FindNamedMaps(string typeName)
    {
        var result = new List<NdfNamedMapSpan>();
        for (var index = 0; index + 3 < _tokens.Count; index++)
        {
            if (!string.Equals(_tokens[index + 1].Text, "is", StringComparison.Ordinal) ||
                !string.Equals(_tokens[index + 2].Text, typeName, StringComparison.Ordinal) ||
                _tokens[index + 3].Text != "[")
            {
                continue;
            }

            var closing = FindMatching(index + 3, "[", "]", _tokens.Count - 1);
            if (closing < 0)
            {
                continue;
            }

            result.Add(new NdfNamedMapSpan(
                _tokens[index].Text,
                typeName,
                new NdfValueSpan(index + 3, closing)));
            index = closing;
        }

        return result;
    }

    public NdfValueSpan? FindNamedArgument(NdfValueSpan value, string argumentName)
    {
        for (var index = value.StartTokenIndex; index < value.EndTokenIndex; index++)
        {
            if (_tokens[index + 1].Text != "(")
            {
                continue;
            }

            var closing = FindMatching(index + 1, "(", ")", value.EndTokenIndex);
            if (closing < 0)
            {
                continue;
            }

            var constructor = new NdfConstructorSpan(_tokens[index].Text, index, index + 1, closing);
            var match = FindDirectAssignments(constructor, argumentName);
            if (match.Count == 1)
            {
                return match[0];
            }
        }

        return null;
    }

    public string Raw(NdfValueSpan value)
    {
        if (_tokens.Count == 0 ||
            value.StartTokenIndex < 0 ||
            value.EndTokenIndex >= _tokens.Count ||
            value.StartTokenIndex > value.EndTokenIndex)
        {
            return string.Empty;
        }

        var start = _tokens[value.StartTokenIndex].Start;
        var end = _tokens[value.EndTokenIndex].End;
        return _source[start..end].Trim();
    }

    public int StartOffset(NdfValueSpan value) => _tokens[value.StartTokenIndex].Start;

    public int Length(NdfValueSpan value) =>
        _tokens[value.EndTokenIndex].End - _tokens[value.StartTokenIndex].Start;

    public static string Unquote(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length < 2 ||
            (trimmed[0] != '\'' && trimmed[0] != '"') ||
            trimmed[^1] != trimmed[0])
        {
            return trimmed;
        }

        var quote = trimmed[0];
        var inner = trimmed[1..^1];
        return inner.Replace($"\\{quote}", quote.ToString(), StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    public static string Leaf(string value)
    {
        var trimmed = value.Trim().TrimEnd(',');
        var slash = trimmed.LastIndexOf('/');
        return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
    }

    public IReadOnlyList<NdfAssignmentSpan> EnumerateDirectAssignments(NdfConstructorSpan constructor)
    {
        lock (_cacheGate)
        {
            if (!_assignments.TryGetValue(constructor, out var result))
                _assignments[constructor] = result = EnumerateDirectAssignmentsCore(constructor);
            return result;
        }
    }

    private IReadOnlyList<NdfAssignmentSpan> EnumerateDirectAssignmentsCore(NdfConstructorSpan constructor)
    {
        var starts = new List<(string Name, int NameToken, int ValueStart)>();
        var parentheses = 0;
        var brackets = 0;
        for (var index = constructor.OpenTokenIndex + 1; index < constructor.CloseTokenIndex; index++)
        {
            var text = _tokens[index].Text;
            if (text == "(")
            {
                parentheses++;
                continue;
            }

            if (text == ")")
            {
                parentheses--;
                continue;
            }

            if (text == "[")
            {
                brackets++;
                continue;
            }

            if (text == "]")
            {
                brackets--;
                continue;
            }

            if (parentheses == 0 && brackets == 0 && index + 2 < constructor.CloseTokenIndex &&
                _tokens[index + 1].Text == "=")
            {
                starts.Add((text, index, index + 2));
                index++;
            }
        }

        var result = new List<NdfAssignmentSpan>(starts.Count);
        for (var index = 0; index < starts.Count; index++)
        {
            var current = starts[index];
            var valueEnd = index + 1 < starts.Count
                ? starts[index + 1].NameToken - 1
                : constructor.CloseTokenIndex - 1;
            while (valueEnd >= current.ValueStart && _tokens[valueEnd].Text == ",")
            {
                valueEnd--;
            }

            if (current.ValueStart <= valueEnd)
            {
                result.Add(new NdfAssignmentSpan(
                    current.Name,
                    new NdfValueSpan(current.ValueStart, valueEnd)));
            }
        }

        return result;
    }

    private int FindTopLevelComma(int start, int end)
    {
        var parentheses = 0;
        var brackets = 0;
        for (var index = start; index <= end; index++)
        {
            switch (_tokens[index].Text)
            {
                case "(": parentheses++; break;
                case ")": parentheses--; break;
                case "[": brackets++; break;
                case "]": brackets--; break;
                case "," when parentheses == 0 && brackets == 0: return index;
            }
        }

        return -1;
    }

    private int FindToken(string text, int start, int end)
    {
        for (var index = Math.Max(0, start); index <= Math.Min(end, _tokens.Count - 1); index++)
        {
            if (_tokens[index].Text == text)
            {
                return index;
            }
        }

        return -1;
    }

    private int FindMatching(int openingIndex, string opening, string closing, int maximumIndex)
    {
        lock (_cacheGate)
        {
            var key = (openingIndex, opening, closing);
            if (!_matching.TryGetValue(key, out var result))
                _matching[key] = result = FindMatchingCore(openingIndex, opening, closing);
            return result <= maximumIndex ? result : -1;
        }
    }

    private int FindMatchingCore(int openingIndex, string opening, string closing)
    {
        var depth = 0;
        for (var index = openingIndex; index < _tokens.Count; index++)
        {
            if (_tokens[index].Text == opening)
            {
                depth++;
            }
            else if (_tokens[index].Text == closing && --depth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static List<NdfToken> Tokenize(string source, int startOffset, int length, CancellationToken cancellationToken)
    {
        var tokens = new List<NdfToken>();
        var end = startOffset + length;
        var index = startOffset;
        while (index < end)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var character = source[index];
            if (char.IsWhiteSpace(character))
            {
                index++;
                continue;
            }

            if (character == '/' && index + 1 < end && source[index + 1] == '/')
            {
                index += 2;
                while (index < end && source[index] is not '\r' and not '\n')
                {
                    if ((index & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                    index++;
                }

                continue;
            }

            if (character == '/' && index + 1 < end && source[index + 1] == '*')
            {
                var close = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                if (close < 0 || close + 2 > end) throw new InvalidDataException("NDF块注释未闭合");
                index = close + 2;
                continue;
            }

            if (character is '\'' or '"')
            {
                var quote = character;
                var tokenStart = index++;
                var escaped = false;
                while (index < end)
                {
                    if ((index & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                    var current = source[index++];
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (current == '\\')
                    {
                        escaped = true;
                    }
                    else if (current == quote)
                    {
                        break;
                    }
                }

                tokens.Add(new NdfToken(source[tokenStart..index], tokenStart, index - tokenStart));
                continue;
            }

            if (character is '(' or ')' or '[' or ']' or ',' or '=')
            {
                tokens.Add(new NdfToken(character.ToString(), index, 1));
                index++;
                continue;
            }

            var atomStart = index;
            while (index < end)
            {
                if ((index & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                character = source[index];
                if (char.IsWhiteSpace(character) ||
                    character is '(' or ')' or '[' or ']' or ',' or '=' or '\'' or '"' ||
                    (character == '/' && index + 1 < end && source[index + 1] is '/' or '*'))
                {
                    break;
                }

                index++;
            }

            if (index == atomStart)
            {
                index++;
                continue;
            }

            tokens.Add(new NdfToken(source[atomStart..index], atomStart, index - atomStart));
        }

        return tokens;
    }

    public sealed record NdfToken(string Text, int Start, int Length)
    {
        public int End => Start + Length;
    }
}

public sealed record NdfConstructorSpan(
    string TypeName,
    int TypeTokenIndex,
    int OpenTokenIndex,
    int CloseTokenIndex);

public sealed record NdfValueSpan(int StartTokenIndex, int EndTokenIndex);

public sealed record NdfMapEntry(NdfValueSpan Key, NdfValueSpan Value);

public sealed record NdfAssignmentSpan(string Name, NdfValueSpan Value);

public sealed record NdfReferenceSpan(string Leaf, string Raw, NdfValueSpan Span);

public sealed record NdfNamedMapSpan(string Name, string TypeName, NdfValueSpan Value);
