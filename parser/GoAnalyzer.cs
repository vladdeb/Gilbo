namespace GilbMetricParser;

/// <summary>Тип найденной управляющей конструкции.</summary>
public enum ConstructKind
{
    If,
    ElseIf,
    For,
    ForRange,
    Switch,
    TypeSwitch,
    Select,
}

/// <summary>Описание одной управляющей конструкции, найденной в программе.</summary>
public sealed class Construct
{
    public ConstructKind Kind { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }

    /// <summary>Уровень вложенности (для switch/select — эквивалентный).</summary>
    public int Level { get; set; }

    /// <summary>Число ветвей (case + default) для switch/select.</summary>
    public int BranchCount { get; set; }

    /// <summary>Вклад конструкции в абсолютную сложность CL.</summary>
    public int ClContribution { get; set; }

    public string KindName => Kind switch
    {
        ConstructKind.If => "if",
        ConstructKind.ElseIf => "else if",
        ConstructKind.For => "for",
        ConstructKind.ForRange => "for range",
        ConstructKind.Switch => "switch",
        ConstructKind.TypeSwitch => "type switch",
        ConstructKind.Select => "select",
        _ => Kind.ToString(),
    };

    public string Detail => Kind is ConstructKind.Switch or ConstructKind.TypeSwitch or ConstructKind.Select
        ? $"ветвей: {BranchCount} (CL += {ClContribution})"
        : $"CL += {ClContribution}";
}

/// <summary>Результат анализа программы по метрике Джилба.</summary>
public sealed class AnalysisResult
{
    public int AbsoluteComplexity { get; set; }
    public double RelativeComplexity { get; set; }
    public int MaxNestingLevel { get; set; }
    public int TotalOperators { get; set; }
    public List<Construct> Constructs { get; } = new();
    public List<string> Warnings { get; } = new();
}

/// <summary>
/// Структурный анализатор исходного кода на языке Go.
/// </summary>
public static class GoAnalyzer
{
    // Какие токены начинают оператор (используется для подсчёта общего числа операторов).
    private static readonly HashSet<string> StatementKeywords = new(StringComparer.Ordinal)
    {
        "return", "if", "for", "switch", "select", "go", "defer",
        "break", "continue", "goto", "fallthrough", "var", "const",
    };

    // Если предыдущий токен — один из этих, текущий токен не начинает новый оператор.
    private static readonly HashSet<string> ContinuationTokens = new(StringComparer.Ordinal)
    {
        "&&", "||", "+", "-", "*", "/", "%", "&", "|", "^", "<<", ">>", "&^",
        "==", "!=", "<", "<=", ">", ">=", "=", ":=", "+=", "-=", "*=", "/=",
        "%=", "&=", "|=", "^=", ".", ",", "(", "[",
    };

    public static AnalysisResult Analyze(string source)
    {
        var result = new AnalysisResult();
        var tokens = GoLexer.Tokenize(source);

        if (tokens.Count == 0)
        {
            result.Warnings.Add("Исходный текст пуст или не содержит токенов.");
            return result;
        }

        result.TotalOperators = CountOperators(tokens);
        AnalyzeStructure(tokens, result);

        result.RelativeComplexity = result.TotalOperators > 0
            ? (double)result.AbsoluteComplexity / result.TotalOperators
            : 0.0;

        if (result.TotalOperators == 0)
            result.Warnings.Add("Не удалось подсчитать операторы программы (cl не определена).");

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Структурный анализ: поиск ветвлений и вычисление CL / CLI
    // ─────────────────────────────────────────────────────────────────────

    private sealed class Frame
    {
        public bool IsControlBody;
        public bool IsCaseContainer;

        /// <summary>Тело if / else if / else — участвует в завершении цепочки ветвлений.</summary>
        public bool IsIfBranch;

        public int SavedControlDepth;
    }

    private sealed class ChainContext
    {
        public int RootDepth;    // глубина, на которую нужно вернуться при завершении цепочки
        public int BranchDepth;  // глубина текущей ветви цепочки
    }

    private sealed class CaseContext
    {
        public int BraceDepth;
        public int Count;
        public Construct? Owner;
        public int BaseDepth;
    }

    private static void AnalyzeStructure(List<Token> tokens, AnalysisResult result)
    {
        int controlDepth = 0;   // число открытых тел управляющих конструкций
        int braceDepth = 0;     // глобальная глубина фигурных скобок
        int cl = 0;
        int cli = 0;

        var frames = new Stack<Frame>();
        var caseStack = new Stack<CaseContext>();
        var chainStack = new Stack<ChainContext>();

        string? pending = null;          // какая конструкция ожидает своё тело
        int pendingBase = 0;             // controlDepth в момент ключевого слова
        Construct? pendingConstruct = null;
        bool pendingElse = false;        // предыдущий значимый токен — else
        Token? prev = null;

        for (int i = 0; i < tokens.Count; i++)
        {
            Token t = tokens[i];
            Token? p = prev;
            bool afterElse = pendingElse;
            pendingElse = false;

            if (t.Text == "{")
            {
                braceDepth++;

                if (pending != null && IsBodyBrace(p))
                {
                    // Это тело управляющей конструкции.
                    frames.Push(new Frame
                    {
                        IsControlBody = true,
                        IsIfBranch = pending == "if",
                        SavedControlDepth = controlDepth,
                    });
                    controlDepth++;

                    if (pending is "switch" or "typeswitch" or "select")
                    {
                        var ctx = new CaseContext
                        {
                            BraceDepth = braceDepth,
                            Count = 0,
                            Owner = pendingConstruct,
                            BaseDepth = pendingBase,
                        };
                        caseStack.Push(ctx);
                        frames.Peek().IsCaseContainer = true;
                    }

                    pending = null;
                    pendingConstruct = null;
                }
                else if (afterElse && chainStack.Count > 0)
                {
                    // Тело ветви else: наращивает уровень вложенности и
                    // завершает цепочку после закрытия.
                    ChainContext ctx = chainStack.Peek();
                    ctx.BranchDepth += 1;
                    controlDepth = ctx.BranchDepth;

                    frames.Push(new Frame
                    {
                        IsControlBody = true,
                        IsIfBranch = true,
                        SavedControlDepth = ctx.RootDepth,
                    });
                }
                else
                {
                    frames.Push(new Frame { IsControlBody = false, SavedControlDepth = controlDepth });
                }
            }
            else if (t.Text == "}")
            {
                braceDepth--;

                if (frames.Count > 0)
                {
                    Frame f = frames.Pop();
                    controlDepth = f.SavedControlDepth;

                    if (f.IsCaseContainer && caseStack.Count > 0)
                    {
                        CaseContext ctx = caseStack.Pop();
                        FinalizeChoice(ctx, result, ref cl, ref cli);
                    }

                    if (f.IsIfBranch)
                    {
                        // Цепочка if / else if / else завершается, если далее не следует else.
                        Token? next = (i + 1 < tokens.Count) ? tokens[i + 1] : null;
                        if (next == null || next.Text != "else")
                        {
                            if (chainStack.Count > 0)
                            {
                                ChainContext ctx = chainStack.Pop();
                                controlDepth = ctx.RootDepth;
                            }
                        }
                    }
                }
            }
            else if (t.Text == "case" || t.Text == "default")
            {
                if (caseStack.Count > 0)
                {
                    CaseContext ctx = caseStack.Peek();
                    if (braceDepth == ctx.BraceDepth)
                        ctx.Count++;
                }
            }
            else if (t.Kind == TokenKind.Keyword && t.Text == "if")
            {
                bool isElseIf = p != null && p.Text == "else";

                if (isElseIf && chainStack.Count > 0)
                {
                    // Ветвь else if вложена в предыдущую цепочку.
                    ChainContext ctx = chainStack.Peek();
                    ctx.BranchDepth += 1;
                    controlDepth = ctx.BranchDepth;
                }
                else
                {
                    chainStack.Push(new ChainContext { RootDepth = controlDepth, BranchDepth = controlDepth });
                }

                var c = new Construct
                {
                    Kind = isElseIf ? ConstructKind.ElseIf : ConstructKind.If,
                    Line = t.Line,
                    Column = t.Column,
                    Level = controlDepth + 1,
                    ClContribution = 1,
                };
                cl++;
                if (c.Level > cli) cli = c.Level;
                result.Constructs.Add(c);

                pending = "if";
                pendingBase = controlDepth;
            }
            else if (t.Kind == TokenKind.Keyword && t.Text == "for")
            {
                (bool hasRange, _) = InspectHeader(tokens, i);
                var c = new Construct
                {
                    Kind = hasRange ? ConstructKind.ForRange : ConstructKind.For,
                    Line = t.Line,
                    Column = t.Column,
                    Level = controlDepth + 1,
                    ClContribution = 0, // цикл не является условным оператором
                };
                result.Constructs.Add(c);

                pending = "for";
                pendingBase = controlDepth;
            }
            else if (t.Kind == TokenKind.Keyword && t.Text == "switch")
            {
                (_, bool isTypeSwitch) = InspectHeader(tokens, i);
                var c = new Construct
                {
                    Kind = isTypeSwitch ? ConstructKind.TypeSwitch : ConstructKind.Switch,
                    Line = t.Line,
                    Column = t.Column,
                    Level = controlDepth + 1,
                    ClContribution = 0, // уточняется при закрытии тела
                };
                result.Constructs.Add(c);

                pending = isTypeSwitch ? "typeswitch" : "switch";
                pendingBase = controlDepth;
                pendingConstruct = c;
            }
            else if (t.Kind == TokenKind.Keyword && t.Text == "select")
            {
                var c = new Construct
                {
                    Kind = ConstructKind.Select,
                    Line = t.Line,
                    Column = t.Column,
                    Level = controlDepth + 1,
                    ClContribution = 0,
                };
                result.Constructs.Add(c);

                pending = "select";
                pendingBase = controlDepth;
                pendingConstruct = c;
            }

            if (t.Text == "else")
                pendingElse = true;

            prev = t;
        }

        if (caseStack.Count > 0)
            result.Warnings.Add("Обнаружено незакрытое тело switch/select — анализ может быть неполным.");
        if (frames.Count > 0)
            result.Warnings.Add("Обнаружены непарные фигурные скобки — структура программы не сбалансирована.");
        if (pending != null)
            result.Warnings.Add("У управляющей конструкции не найдено тело — анализ может быть неточным.");

        result.AbsoluteComplexity = cl;
        result.MaxNestingLevel = cli;
    }

    /// <summary>
    /// Завершает обработку оператора множественного выбора: вклад в CL и CLI.
    /// n ветвей эквивалентны (n - 1) условным операторам с вложенностью (n - 2).
    /// </summary>
    private static void FinalizeChoice(CaseContext ctx, AnalysisResult result, ref int cl, ref int cli)
    {
        int n = ctx.Count;
        int contribution = Math.Max(0, n - 1);
        cl += contribution;

        // Эквивалентная вложенность: (n - 2), но не менее одного уровня.
        int equivalentLevel = ctx.BaseDepth + Math.Max(1, n - 2);

        if (ctx.Owner != null)
        {
            ctx.Owner.BranchCount = n;
            ctx.Owner.ClContribution = contribution;
            ctx.Owner.Level = equivalentLevel;
        }

        if (equivalentLevel > cli) cli = equivalentLevel;
    }

    /// <summary>Определяет, является ли скобка «{» телом управляющей конструкции.</summary>
    private static bool IsBodyBrace(Token? prev)
    {
        if (prev == null) return true;
        if (prev.Text == "]" || prev.Text == "}") return false;
        if (prev.Kind == TokenKind.Keyword && (prev.Text == "struct" || prev.Text == "interface")) return false;
        return true;
    }

    /// <summary>
    /// Просматривает заголовок конструкции (от ключевого слова до открывающей «{»)
    /// и определяет наличие ключевого слова range или шаблона .(type).
    /// </summary>
    private static (bool hasRange, bool isTypeSwitch) InspectHeader(List<Token> tokens, int start)
    {
        bool hasRange = false;
        bool isTypeSwitch = false;
        int paren = 0;
        int bracket = 0;

        for (int k = start + 1; k < tokens.Count; k++)
        {
            Token t = tokens[k];

            if (t.Text == "(") { paren++; continue; }
            if (t.Text == "[") { bracket++; continue; }
            if (t.Text == ")") { paren = Math.Max(0, paren - 1); continue; }
            if (t.Text == "]") { bracket = Math.Max(0, bracket - 1); continue; }

            if (paren == 0 && bracket == 0 && t.Text == "{")
                break;

            if (t.Kind == TokenKind.Keyword && t.Text == "range")
                hasRange = true;

            // Шаблон .(type) — признак type switch.
            if (t.Text == "(" && k + 3 < tokens.Count &&
                tokens[k + 1].Text == "." && tokens[k + 2].Text == "type" &&
                tokens[k + 3].Text == ")")
            {
                isTypeSwitch = true;
            }

            // Ограничитель, чтобы не уйти слишком далеко.
            if (paren == 0 && bracket == 0 && (t.Text == ";" || t.Line > tokens[start].Line + 40))
                break;
        }

        return (hasRange, isTypeSwitch);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Подсчёт общего числа операторов программы
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Приближённый подсчёт операторов программы по началам операторов.
    /// Строки, начинающиеся с ключевого слова-оператора или идентификатора,
    /// считаются одним оператором (с учётом переносов внутри скобок и
    /// продолжений выражений).
    /// </summary>
    private static int CountOperators(List<Token> tokens)
    {
        int operators = 0;
        int paren = 0;
        Token? prev = null;

        foreach (Token t in tokens)
        {
            bool firstOnLine = prev == null || prev.Line != t.Line;

            if (firstOnLine && paren == 0 && IsCountableToken(t))
            {
                bool isContinuation = prev != null && ContinuationTokens.Contains(prev.Text);
                if (!isContinuation)
                    operators++;
            }

            if (t.Text == "(" || t.Text == "[") paren++;
            else if (t.Text == ")" || t.Text == "]") paren = Math.Max(0, paren - 1);

            prev = t;
        }

        return operators;
    }

    private static bool IsCountableToken(Token t)
    {
        if (t.Kind == TokenKind.Identifier) return true;
        if (t.Kind == TokenKind.Keyword) return StatementKeywords.Contains(t.Text);
        return false;
    }
}
