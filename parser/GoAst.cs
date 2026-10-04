namespace GilbMetricParser;

// ─────────────────────────────────────────────────────────────────────────
//  Узлы абстрактного синтаксического дерева (AST) языка Go.
//  Для расчёта метрики Джилба достаточно структурных узлов; выражения
//  хранятся неявно (как диапазоны токенов) и в дерево не разворачиваются.
// ─────────────────────────────────────────────────────────────────────────

public abstract class GoNode
{
    public int Line { get; set; }
    public int Column { get; set; }
}

public abstract class GoStmt : GoNode { }

public abstract class GoDecl : GoNode { }

public sealed class GoBlock : GoNode
{
    public List<GoStmt> Statements { get; } = new();
}

/// <summary>Простой оператор (присваивание, вызов, выражение).</summary>
public sealed class GoExprStmt : GoStmt { }

/// <summary>Оператор return.</summary>
public sealed class GoReturnStmt : GoStmt { }

/// <summary>break / continue / goto / fallthrough.</summary>
public sealed class GoBranchStmt : GoStmt { }

/// <summary>Объявление var/const/type внутри функции.</summary>
public sealed class GoDeclStmt : GoStmt { }

/// <summary>Оператор-блок { … } (не увеличивает вложенность).</summary>
public sealed class GoBlockStmt : GoStmt
{
    public GoBlock Block { get; set; } = new();
}

/// <summary>Оператор с меткой: label: stmt.</summary>
public sealed class GoLabeledStmt : GoStmt
{
    public GoStmt? Inner { get; set; }
}

/// <summary>if [init;] cond { … } [else …].</summary>
public sealed class GoIfStmt : GoStmt
{
    public GoBlock Body { get; set; } = new();
    public GoStmt? Else { get; set; }
}

/// <summary>Все формы цикла for (в т.ч. range).</summary>
public sealed class GoForStmt : GoStmt
{
    public bool IsRange { get; set; }
    public GoBlock Body { get; set; } = new();
}

/// <summary>Одна ветвь оператора switch/select.</summary>
public sealed class GoCaseClause : GoNode
{
    public bool IsDefault { get; set; }
    public GoBlock Body { get; set; } = new();
}

/// <summary>switch, type switch или select.</summary>
public sealed class GoSwitchStmt : GoStmt
{
    public bool IsTypeSwitch { get; set; }
    public bool IsSelect { get; set; }
    public List<GoCaseClause> Cases { get; } = new();
}

/// <summary>Объявление функции или метода.</summary>
public sealed class GoFuncDecl : GoDecl
{
    public string Name { get; set; } = string.Empty;
    public GoBlock Body { get; set; } = new();
}

/// <summary>Объявление верхнего уровня (package/import/var/const/type).</summary>
public sealed class GoGenDecl : GoDecl
{
    public string Keyword { get; set; } = string.Empty;
}

public sealed class GoFile : GoNode
{
    public List<GoDecl> Decls { get; } = new();
}

// ─────────────────────────────────────────────────────────────────────────
//  Рекурсивный спуск: построение AST.
// ─────────────────────────────────────────────────────────────────────────

public sealed class GoParser
{
    private static readonly Token EofToken =
        new() { Kind = TokenKind.Punctuation, Text = "<eof>", Line = 0, Column = 0 };

    private readonly List<Token> _tokens;
    private readonly List<GoFuncDecl> _functionLiterals = new();
    private int _pos;

    public List<string> Warnings { get; } = new();

    /// <summary>Тела анонимных функций, найденные внутри выражений.</summary>
    public IReadOnlyList<GoFuncDecl> FunctionLiterals => _functionLiterals;

    public GoParser(List<Token> tokens) => _tokens = tokens;

    private bool Eof => _pos >= _tokens.Count;
    private Token Cur => _pos < _tokens.Count ? _tokens[_pos] : EofToken;
    private Token Peek(int k = 1) => (_pos + k < _tokens.Count) ? _tokens[_pos + k] : EofToken;
    private void Advance() => _pos++;
    private bool Is(string text) => Cur.Text == text;
    private bool IsKeyword(string text) => Cur.Kind == TokenKind.Keyword && Cur.Text == text;

    // ── Файл и объявления верхнего уровня ────────────────────────────────

    public GoFile ParseFile()
    {
        var file = new GoFile { Line = 1, Column = 1 };
        int guard = 0;

        while (!Eof && guard++ < 1_000_000)
        {
            if (IsKeyword("package"))
            {
                Advance();
                SkipToLineEnd();
            }
            else if (IsKeyword("import"))
            {
                file.Decls.Add(ParseGenDecl("import"));
            }
            else if (IsKeyword("func"))
            {
                file.Decls.Add(ParseFuncDecl());
            }
            else if (IsKeyword("var") || IsKeyword("const") || IsKeyword("type"))
            {
                file.Decls.Add(ParseGenDecl(Cur.Text));
            }
            else
            {
                // Защита от неожиданных токенов на верхнем уровне.
                Advance();
            }
        }

        file.Decls.AddRange(_functionLiterals);
        return file;
    }

    private GoGenDecl ParseGenDecl(string keyword)
    {
        var decl = new GoGenDecl { Line = Cur.Line, Column = Cur.Column, Keyword = keyword };
        Advance(); // var/const/type/import

        if (Is("("))
            SkipBalanced("(", ")");
        else
            SkipToStatementEnd();

        return decl;
    }

    private GoFuncDecl ParseFuncDecl()
    {
        var f = new GoFuncDecl { Line = Cur.Line, Column = Cur.Column };
        Advance(); // func

        if (Is("(")) SkipBalanced("(", ")");          // получатель метода
        if (Cur.Kind == TokenKind.Identifier)          // имя функции
        {
            f.Name = Cur.Text;
            Advance();
        }
        if (Is("(")) SkipBalanced("(", ")");          // параметры

        if (Is("("))                                    // список результатов
            SkipBalanced("(", ")");
        else if (CanStartType(Cur))
            SkipType();

        if (Is("{"))
            f.Body = ParseBlock();
        else
            Warnings.Add($"Не найдено тело функции '{f.Name}' (строка {f.Line}).");

        return f;
    }

    // ── Блоки и операторы ────────────────────────────────────────────────

    private GoBlock ParseBlock()
    {
        var block = new GoBlock { Line = Cur.Line, Column = Cur.Column };
        if (!Is("{"))
        {
            Warnings.Add($"Ожидалась '{{' (строка {Cur.Line}).");
            return block;
        }

        Advance(); // {
        while (!Eof && !Is("}"))
        {
            if (Is(";")) { Advance(); continue; }
            GoStmt? stmt = ParseStmt();
            if (stmt != null) block.Statements.Add(stmt);
        }
        if (Is("}")) Advance();
        return block;
    }

    private GoStmt? ParseStmt()
    {
        if (Eof) return null;

        // Метка: идентификатор ':' (но не ':=').
        if (Cur.Kind == TokenKind.Identifier && Peek().Text == ":")
        {
            var label = new GoLabeledStmt { Line = Cur.Line, Column = Cur.Column };
            Advance(); // имя
            Advance(); // ':'
            label.Inner = ParseStmt();
            return label;
        }

        if (IsKeyword("if")) return ParseIf();
        if (IsKeyword("for")) return ParseFor();
        if (IsKeyword("switch")) return ParseSwitch(isSelect: false);
        if (IsKeyword("select")) return ParseSwitch(isSelect: true);

        if (Is("{"))
        {
            GoBlock b = ParseBlock();
            return new GoBlockStmt { Line = b.Line, Column = b.Column, Block = b };
        }

        if (IsKeyword("var") || IsKeyword("const") || IsKeyword("type"))
        {
            var decl = new GoDeclStmt { Line = Cur.Line, Column = Cur.Column };
            Advance();
            if (Is("(")) SkipBalanced("(", ")");
            else SkipToStatementEnd();
            return decl;
        }

        if (IsKeyword("return"))
        {
            var s = new GoReturnStmt { Line = Cur.Line, Column = Cur.Column };
            Advance();
            SkipToStatementEnd();
            return s;
        }

        if (IsKeyword("break") || IsKeyword("continue") || IsKeyword("goto") || IsKeyword("fallthrough"))
        {
            var s = new GoBranchStmt { Line = Cur.Line, Column = Cur.Column };
            string kw = Cur.Text;
            Advance();
            if (kw != "fallthrough" && Cur.Kind == TokenKind.Identifier)
                Advance(); // метка перехода
            SkipToStatementEnd();
            return s;
        }

        if (IsKeyword("go") || IsKeyword("defer"))
        {
            var s = new GoExprStmt { Line = Cur.Line, Column = Cur.Column };
            Advance();
            SkipToStatementEnd();
            return s;
        }

        // Простой оператор (присваивание, вызов, выражение, инкремент).
        var expr = new GoExprStmt { Line = Cur.Line, Column = Cur.Column };
        int before = _pos;
        SkipToStatementEnd();
        if (_pos == before) Advance(); // защита от зацикливания
        return expr;
    }

    private GoIfStmt ParseIf()
    {
        var node = new GoIfStmt { Line = Cur.Line, Column = Cur.Column };
        Advance(); // if

        SkipHeaderToBodyBrace();
        if (Is("{"))
            node.Body = ParseBlock();

        if (IsKeyword("else"))
        {
            Advance();
            if (IsKeyword("if"))
            {
                node.Else = ParseIf();
            }
            else if (Is("{"))
            {
                GoBlock b = ParseBlock();
                node.Else = new GoBlockStmt { Line = b.Line, Column = b.Column, Block = b };
            }
        }

        return node;
    }

    private GoForStmt ParseFor()
    {
        var node = new GoForStmt { Line = Cur.Line, Column = Cur.Column };
        Advance(); // for

        int headerStart = _pos;
        int bodyIndex = FindHeaderEnd(headerStart);
        node.IsRange = HeaderContainsKeyword(headerStart, bodyIndex, "range");

        _pos = bodyIndex;
        if (Is("{"))
            node.Body = ParseBlock();

        return node;
    }

    private GoSwitchStmt ParseSwitch(bool isSelect)
    {
        var node = new GoSwitchStmt { Line = Cur.Line, Column = Cur.Column, IsSelect = isSelect };
        Advance(); // switch / select

        int headerStart = _pos;
        int bodyIndex = FindHeaderEnd(headerStart);
        if (!isSelect)
            node.IsTypeSwitch = HeaderContainsTypeAssertion(headerStart, bodyIndex);

        _pos = bodyIndex;
        if (!Is("{"))
        {
            Warnings.Add($"Не найдено тело switch/select (строка {node.Line}).");
            return node;
        }

        Advance(); // {
        int guard = 0;
        while (!Eof && !Is("}") && guard++ < 1_000_000)
        {
            if (Is(";")) { Advance(); continue; }

            if (IsKeyword("case"))
            {
                var clause = new GoCaseClause { Line = Cur.Line, Column = Cur.Column };
                Advance(); // case
                SkipUntilColon();
                if (Is(":")) Advance();
                clause.Body = ParseCaseBody();
                node.Cases.Add(clause);
            }
            else if (IsKeyword("default"))
            {
                var clause = new GoCaseClause { IsDefault = true, Line = Cur.Line, Column = Cur.Column };
                Advance(); // default
                if (Is(":")) Advance();
                clause.Body = ParseCaseBody();
                node.Cases.Add(clause);
            }
            else
            {
                Advance(); // защита
            }
        }
        if (Is("}")) Advance();

        return node;
    }

    private GoBlock ParseCaseBody()
    {
        var block = new GoBlock { Line = Cur.Line, Column = Cur.Column };
        int guard = 0;
        while (!Eof && !Is("}") && !IsKeyword("case") && !IsKeyword("default") && guard++ < 1_000_000)
        {
            if (Is(";")) { Advance(); continue; }
            GoStmt? stmt = ParseStmt();
            if (stmt != null) block.Statements.Add(stmt);
        }
        return block;
    }

    // ── Вспомогательные методы разбора ───────────────────────────────────

    private void SkipToLineEnd()
    {
        if (Eof) return;
        int line = Cur.Line;
        while (!Eof && Cur.Line == line) Advance();
    }

    /// <summary>Пропускает оператор до его конца (ASI, ';' или '}').</summary>
    private void SkipToStatementEnd()
    {
        int paren = 0, bracket = 0, brace = 0;
        Token? prev = null;

        while (!Eof)
        {
            Token t = Cur;

            if (paren == 0 && bracket == 0 && brace == 0)
            {
                if (t.Text == ";") { Advance(); return; }
                if (t.Text == "}") return;
                if (t.Text == "case" || t.Text == "default") return;
                if (prev != null && prev.Line != t.Line && CanEndStatement(prev)) return;
            }

            // Анонимная функция внутри выражения — разбираем её тело.
            if (t.Kind == TokenKind.Keyword && t.Text == "func")
            {
                ParseFuncLiteral();
                if (_pos > 0) prev = _tokens[_pos - 1];
                continue;
            }

            switch (t.Text)
            {
                case "(": paren++; break;
                case ")": paren = Math.Max(0, paren - 1); break;
                case "[": bracket++; break;
                case "]": bracket = Math.Max(0, bracket - 1); break;
                case "{": brace++; break;
                case "}": brace = Math.Max(0, brace - 1); break;
            }

            prev = t;
            Advance();
        }
    }

    /// <summary>Разбирает литерал функции: func(параметры) (результаты) { тело }.</summary>
    private void ParseFuncLiteral()
    {
        var f = new GoFuncDecl { Line = Cur.Line, Column = Cur.Column };
        Advance(); // func

        if (Is("(")) SkipBalanced("(", ")");      // параметры
        if (Is("(")) SkipBalanced("(", ")");      // список результатов
        else if (CanStartType(Cur)) SkipType();   // один результат

        if (Is("{"))
        {
            f.Body = ParseBlock();
            _functionLiterals.Add(f);
        }
    }

    private static bool CanEndStatement(Token t) =>
        t.Kind is TokenKind.Identifier or TokenKind.Number or TokenKind.String or TokenKind.Rune ||
        t.Text is "break" or "continue" or "fallthrough" or "return" or ")" or "]" or "}" or "++" or "--";

    /// <summary>Находит индекс открывающей '{' тела управляющей конструкции.</summary>
    private int FindHeaderEnd(int start)
    {
        int paren = 0, bracket = 0;
        int k = start;
        while (k < _tokens.Count)
        {
            string s = _tokens[k].Text;
            if (s == "(") paren++;
            else if (s == ")") paren = Math.Max(0, paren - 1);
            else if (s == "[") bracket++;
            else if (s == "]") bracket = Math.Max(0, bracket - 1);
            else if (s == "{" && paren == 0 && bracket == 0)
            {
                if (IsCompositeLiteralBrace(k))
                {
                    k = SkipBalancedFrom(k, "{", "}");
                    continue;
                }
                return k;
            }
            k++;
        }
        return _tokens.Count;
    }

    private void SkipHeaderToBodyBrace() => _pos = FindHeaderEnd(_pos);

    /// <summary>
    /// Эвристика: является ли '{' составным литералом (а не телом блока).
    /// В заголовках if/for/switch Go запрещает составные литералы вида T{…}
    /// без скобок, поэтому распознаются только литеральные типы: []T, [N]T,
    /// map[K]V, struct{…}, interface{…}.
    /// </summary>
    private bool IsCompositeLiteralBrace(int pos)
    {
        if (pos <= 0) return false;

        Token prev = _tokens[pos - 1];
        if (prev.Kind == TokenKind.Keyword && (prev.Text == "struct" || prev.Text == "interface"))
            return true;

        int depth = 0;
        int limit = Math.Max(0, pos - 40);
        for (int j = pos - 1; j >= limit; j--)
        {
            string s = _tokens[j].Text;

            if (s == "]") { depth++; continue; }
            if (s == "[")
            {
                if (depth == 0) return false; // индексное выражение
                Token? before = j > 0 ? _tokens[j - 1] : null;
                if (before == null) return true;
                if (before.Text == "map") return true;                 // map[K]V{…}
                if (before.Kind == TokenKind.Identifier) return false; // a[i] { … }
                if (before.Text is "]" or ")" or "*") return false;
                return true;                                           // []T{…}, [N]T{…}
            }

            if (s is "(" or ")" or "{" or "}" or ";" or "," or "=" or ":=" or
                "&&" or "||" or "==" or "!=" or "<" or ">" or "<=" or ">=" or ".")
                return false;

            Token cur = _tokens[j];
            if (cur.Kind == TokenKind.Keyword &&
                s is not ("map" or "chan" or "struct" or "interface" or "func"))
                return false;
            if (cur.Kind == TokenKind.Operator && s is not ("*" or "."))
                return false;
        }
        return false;
    }

    private int SkipBalancedFrom(int pos, string open, string close)
    {
        int depth = 0;
        int k = pos;
        while (k < _tokens.Count)
        {
            string s = _tokens[k].Text;
            if (s == open) depth++;
            else if (s == close)
            {
                depth--;
                if (depth == 0) return k + 1;
            }
            k++;
        }
        return _tokens.Count;
    }

    private void SkipBalanced(string open, string close)
    {
        if (!Is(open)) return;
        int depth = 0;
        while (!Eof)
        {
            if (Is(open)) depth++;
            else if (Is(close))
            {
                depth--;
                Advance();
                if (depth == 0) return;
                continue;
            }
            Advance();
        }
    }

    private void SkipUntilColon()
    {
        int paren = 0, bracket = 0, brace = 0;
        while (!Eof)
        {
            string s = Cur.Text;
            if (paren == 0 && bracket == 0 && brace == 0 && s == ":") return;
            if (s == "(") paren++;
            else if (s == ")") paren = Math.Max(0, paren - 1);
            else if (s == "[") bracket++;
            else if (s == "]") bracket = Math.Max(0, bracket - 1);
            else if (s == "{") brace++;
            else if (s == "}") { if (brace == 0) return; brace--; }
            Advance();
        }
    }

    private static bool CanStartType(Token t)
    {
        if (t.Kind == TokenKind.Identifier) return true;
        return t.Text is "[" or "map" or "struct" or "interface" or "chan" or "func" or "*" or "<-";
    }

    private void SkipType()
    {
        if (Eof) return;

        if (Is("*")) { Advance(); SkipType(); return; }
        if (Is("[")) { SkipBalanced("[", "]"); SkipType(); return; }
        if (IsKeyword("map")) { Advance(); if (Is("[")) SkipBalanced("[", "]"); SkipType(); return; }
        if (IsKeyword("chan")) { Advance(); if (Is("<-")) Advance(); SkipType(); return; }
        if (Is("<-")) { Advance(); if (IsKeyword("chan")) Advance(); SkipType(); return; }
        if (IsKeyword("func"))
        {
            Advance();
            if (Is("(")) SkipBalanced("(", ")");
            if (Is("(")) SkipBalanced("(", ")");
            else if (CanStartType(Cur)) SkipType();
            return;
        }
        if (IsKeyword("struct") || IsKeyword("interface"))
        {
            Advance();
            if (Is("{")) SkipBalanced("{", "}");
            return;
        }
        if (Cur.Kind == TokenKind.Identifier)
        {
            Advance();
            while (Is("."))
            {
                Advance();
                if (Cur.Kind == TokenKind.Identifier) Advance();
            }
            return;
        }
        if (!Eof) Advance();
    }

    private bool HeaderContainsKeyword(int start, int end, string keyword)
    {
        for (int k = start; k < end && k < _tokens.Count; k++)
            if (_tokens[k].Kind == TokenKind.Keyword && _tokens[k].Text == keyword)
                return true;
        return false;
    }

    private bool HeaderContainsTypeAssertion(int start, int end)
    {
        for (int k = start; k + 3 < end && k + 3 < _tokens.Count; k++)
        {
            if (_tokens[k].Text == "(" && _tokens[k + 1].Text == "." &&
                _tokens[k + 2].Text == "type" && _tokens[k + 3].Text == ")")
                return true;
        }
        return false;
    }
}
