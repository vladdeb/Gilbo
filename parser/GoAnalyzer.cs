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
    /// <summary>CL — абсолютная сложность (количество условных операторов).</summary>
    public int AbsoluteComplexity { get; set; }

    /// <summary>cl — относительная сложность (CL / общее число операторов).</summary>
    public double RelativeComplexity { get; set; }

    /// <summary>CLI — максимальный уровень вложенности условного оператора.</summary>
    public int MaxNestingLevel { get; set; }

    /// <summary>Общее количество операторов программы.</summary>
    public int TotalOperators { get; set; }

    public List<Construct> Constructs { get; } = new();
    public List<string> Warnings { get; } = new();
}

/// <summary>
/// Расчёт метрики Джилба по абстрактному синтаксическому дереву программы.
///
/// CL  — абсолютная сложность: количество условных операторов.
///       Условными считаются операторы ветвления (if / else if) и циклы
///       (for во всех формах). Оператор множественного выбора switch/select
///       с n ветвями эквивалентен (n - 1) условным операторам.
/// cl  — относительная сложность: CL / (общее число операторов).
/// CLI — максимальный уровень вложенности условного оператора.
/// </summary>
public static class GoAnalyzer
{
    public static AnalysisResult Analyze(string source)
    {
        var result = new AnalysisResult();
        List<Token> tokens = GoLexer.Tokenize(source);

        if (tokens.Count == 0)
        {
            result.Warnings.Add("Исходный текст пуст или не содержит токенов.");
            return result;
        }

        var parser = new GoParser(tokens);
        GoFile file = parser.ParseFile();
        result.Warnings.AddRange(parser.Warnings);

        var calculator = new MetricVisitor(result);
        calculator.Run(file);

        result.RelativeComplexity = result.TotalOperators > 0
            ? (double)result.AbsoluteComplexity / result.TotalOperators
            : 0.0;

        if (result.TotalOperators == 0)
            result.Warnings.Add("Не удалось подсчитать операторы программы (cl не определена).");

        result.Constructs.Sort((a, b) => a.Line.CompareTo(b.Line));
        return result;
    }

    /// <summary>Обход AST и вычисление CL, CLI и общего числа операторов.</summary>
    private sealed class MetricVisitor
    {
        private readonly AnalysisResult _result;
        private int _cl;
        private int _cli;
        private int _operators;

        public MetricVisitor(AnalysisResult result) => _result = result;

        public void Run(GoFile file)
        {
            foreach (GoDecl decl in file.Decls)
            {
                if (decl is GoFuncDecl f)
                    VisitBlock(f.Body, 0);
            }

            _result.AbsoluteComplexity = _cl;
            _result.MaxNestingLevel = _cli;
            _result.TotalOperators = _operators;
        }

        private void VisitBlock(GoBlock block, int depth)
        {
            foreach (GoStmt stmt in block.Statements)
                VisitStmt(stmt, depth);
        }

        private void VisitStmt(GoStmt stmt, int depth)
        {
            switch (stmt)
            {
                case GoIfStmt ifs:
                    VisitIf(ifs, depth, isElseIf: false);
                    break;

                case GoForStmt fs:
                    _operators++;
                    int forLevel = depth + 1;
                    _cl++;
                    if (forLevel > _cli) _cli = forLevel;
                    AddConstruct(fs, fs.IsRange ? ConstructKind.ForRange : ConstructKind.For,
                        forLevel, 1, 0);
                    VisitBlock(fs.Body, depth + 1);
                    break;

                case GoSwitchStmt sw:
                    VisitSwitch(sw, depth);
                    break;

                case GoBlockStmt blk:
                    VisitBlock(blk.Block, depth);
                    break;

                case GoLabeledStmt lbl:
                    if (lbl.Inner != null)
                        VisitStmt(lbl.Inner, depth);
                    break;

                default:
                    // Простой оператор, return, break/continue/goto, объявление.
                    _operators++;
                    break;
            }
        }

        private void VisitIf(GoIfStmt ifs, int depth, bool isElseIf)
        {
            _operators++;
            int level = depth + 1;
            _cl++;
            if (level > _cli) _cli = level;

            AddConstruct(ifs, isElseIf ? ConstructKind.ElseIf : ConstructKind.If,
                level, 1, 0);

            VisitBlock(ifs.Body, depth + 1);

            if (ifs.Else is GoIfStmt elseIf)
                VisitIf(elseIf, depth + 1, isElseIf: true);
            else if (ifs.Else != null)
                VisitStmt(ifs.Else, depth + 1);
        }

        private void VisitSwitch(GoSwitchStmt sw, int depth)
        {
            _operators++;

            // Условиями считаются ветви case; default — это аналог else.
            int caseCount = 0;
            foreach (GoCaseClause clause in sw.Cases)
                if (!clause.IsDefault)
                    caseCount++;

            int contribution = caseCount;
            _cl += contribution;

            // Оператор выбора с k ветвями case эквивалентен цепочке из k
            // условных операторов if / else if.
            int equivalentLevel = depth + Math.Max(1, caseCount);
            if (equivalentLevel > _cli) _cli = equivalentLevel;

            ConstructKind kind = sw.IsSelect
                ? ConstructKind.Select
                : sw.IsTypeSwitch ? ConstructKind.TypeSwitch : ConstructKind.Switch;

            AddConstruct(sw, kind, equivalentLevel, contribution, sw.Cases.Count);

            foreach (GoCaseClause clause in sw.Cases)
                VisitBlock(clause.Body, depth + 1);
        }

        private void AddConstruct(GoNode node, ConstructKind kind, int level,
            int contribution, int branchCount)
        {
            _result.Constructs.Add(new Construct
            {
                Kind = kind,
                Line = node.Line,
                Column = node.Column,
                Level = level,
                BranchCount = branchCount,
                ClContribution = contribution,
            });
        }
    }
}
