using System.Text;

namespace GilbMetricParser;

/// <summary>Вид лексической единицы языка Go.</summary>
public enum TokenKind
{
    Identifier,
    Keyword,
    Number,
    String,
    Rune,
    Operator,
    Punctuation,
}

/// <summary>Одна лексическая единица (токен) с позицией в исходном тексте.</summary>
public sealed class Token
{
    public TokenKind Kind { get; init; }
    public string Text { get; init; } = string.Empty;
    public int Line { get; init; }
    public int Column { get; init; }

    public override string ToString() => $"{Kind,-12} {Text,-10} @ {Line}:{Column}";
}

/// <summary>
/// Лексический анализатор (сканер) исходного кода на языке Go.
/// Умеет распознавать идентификаторы, ключевые слова, числа, строковые и
/// символьные литералы, операторы и знаки пунктуации, а также пропускать
/// строчные (//) и блочные (/* */) комментарии.
/// </summary>
public static class GoLexer
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "break", "case", "chan", "const", "continue", "default", "defer", "else",
        "fallthrough", "for", "func", "go", "goto", "if", "import", "interface",
        "map", "package", "range", "return", "select", "struct", "switch", "type", "var",
    };

    // Многосимвольные операторы. Проверяются в порядке убывания длины.
    private static readonly string[] MultiCharOperators =
    {
        "<<=", ">>=", "&^=", "...",
        ":=", "==", "!=", "<=", ">=", "&&", "||", "++", "--",
        "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "<<", ">>", "&^", "<-",
    };

    private static readonly HashSet<char> SingleCharOperators = new("+-*/%&|^<>=!~");

    public static List<Token> Tokenize(string source)
    {
        var tokens = new List<Token>();
        int i = 0;
        int line = 1;
        int col = 1;
        int n = source.Length;

        void Advance()
        {
            if (source[i] == '\n') { line++; col = 1; }
            else { col++; }
            i++;
        }

        char Peek(int offset = 0) => (i + offset < n) ? source[i + offset] : '\0';

        while (i < n)
        {
            char c = source[i];

            // Пробельные символы.
            if (char.IsWhiteSpace(c))
            {
                Advance();
                continue;
            }

            // Строчный комментарий.
            if (c == '/' && Peek(1) == '/')
            {
                while (i < n && source[i] != '\n') Advance();
                continue;
            }

            // Блочный комментарий.
            if (c == '/' && Peek(1) == '*')
            {
                int commentStartLine = line;
                Advance(); Advance();
                bool closed = false;
                while (i < n)
                {
                    if (source[i] == '*' && Peek(1) == '/')
                    {
                        Advance(); Advance();
                        closed = true;
                        break;
                    }
                    Advance();
                }
                if (!closed)
                    tokens.Add(new Token { Kind = TokenKind.Punctuation, Text = "</*unterminated*/>", Line = commentStartLine, Column = 1 });
                continue;
            }

            int startLine = line;
            int startCol = col;

            // Идентификатор или ключевое слово.
            if (IsIdentStart(c))
            {
                var sb = new StringBuilder();
                while (i < n && IsIdentPart(source[i]))
                {
                    sb.Append(source[i]);
                    Advance();
                }
                string text = sb.ToString();
                tokens.Add(new Token
                {
                    Kind = Keywords.Contains(text) ? TokenKind.Keyword : TokenKind.Identifier,
                    Text = text,
                    Line = startLine,
                    Column = startCol,
                });
                continue;
            }

            // Строковые литералы: "..." и `...`.
            if (c == '"' || c == '`')
            {
                char quote = c;
                var sb = new StringBuilder();
                sb.Append(quote);
                Advance();
                bool closed = false;
                while (i < n)
                {
                    char d = source[i];
                    if (quote == '"' && d == '\\')
                    {
                        sb.Append(d);
                        Advance();
                        if (i < n) { sb.Append(source[i]); Advance(); }
                        continue;
                    }
                    sb.Append(d);
                    Advance();
                    if (d == quote) { closed = true; break; }
                }
                tokens.Add(new Token { Kind = TokenKind.String, Text = sb.ToString(), Line = startLine, Column = startCol });
                if (!closed) { /* незакрытая строка — не критично для метрики */ }
                continue;
            }

            // Символьный литерал '...'.
            if (c == '\'')
            {
                var sb = new StringBuilder();
                sb.Append(c);
                Advance();
                bool closed = false;
                while (i < n)
                {
                    char d = source[i];
                    if (d == '\\')
                    {
                        sb.Append(d); Advance();
                        if (i < n) { sb.Append(source[i]); Advance(); }
                        continue;
                    }
                    sb.Append(d); Advance();
                    if (d == '\'') { closed = true; break; }
                }
                tokens.Add(new Token { Kind = TokenKind.Rune, Text = sb.ToString(), Line = startLine, Column = startCol });
                _ = closed;
                continue;
            }

            // Числовой литерал.
            if (char.IsDigit(c) || (c == '.' && char.IsDigit(Peek(1))))
            {
                var sb = new StringBuilder();
                while (i < n)
                {
                    char d = source[i];
                    if (char.IsLetterOrDigit(d) || d == '_' || d == '.')
                    {
                        sb.Append(d); Advance();
                        // Знак экспоненты только сразу после e/E/p/P.
                        if ((d == 'e' || d == 'E' || d == 'p' || d == 'P') &&
                            (Peek() == '+' || Peek() == '-'))
                        {
                            sb.Append(source[i]); Advance();
                        }
                        continue;
                    }
                    break;
                }
                tokens.Add(new Token { Kind = TokenKind.Number, Text = sb.ToString(), Line = startLine, Column = startCol });
                continue;
            }

            // Многосимвольные операторы.
            bool matched = false;
            foreach (string op in MultiCharOperators)
            {
                if (i + op.Length <= n && string.CompareOrdinal(source, i, op, 0, op.Length) == 0)
                {
                    tokens.Add(new Token { Kind = TokenKind.Operator, Text = op, Line = startLine, Column = startCol });
                    for (int k = 0; k < op.Length; k++) Advance();
                    matched = true;
                    break;
                }
            }
            if (matched) continue;

            // Односимвольные операторы и пунктуация.
            TokenKind kind = SingleCharOperators.Contains(c) ? TokenKind.Operator : TokenKind.Punctuation;
            tokens.Add(new Token { Kind = kind, Text = c.ToString(), Line = startLine, Column = startCol });
            Advance();
        }

        return tokens;
    }

    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';
    private static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c == '_';
}
