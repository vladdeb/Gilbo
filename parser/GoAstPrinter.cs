using System.Text;

namespace GilbMetricParser;

/// <summary>
/// Отладочная функция: текстовое представление AST программы в виде дерева.
/// Используется для проверки разбора (пункт меню «Показать AST»).
/// </summary>
public static class GoAstPrinter
{
    public static string Print(GoFile file)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"GoFile  (объявлений: {file.Decls.Count})");
        foreach (GoDecl decl in file.Decls)
            PrintDecl(decl, sb, 1);
        return sb.ToString();
    }

    private static void Indent(StringBuilder sb, int level) => sb.Append(new string(' ', level * 2));

    private static void PrintDecl(GoDecl decl, StringBuilder sb, int level)
    {
        switch (decl)
        {
            case GoFuncDecl f:
                Indent(sb, level);
                sb.AppendLine($"FuncDecl \"{f.Name}\"  @{f.Line}");
                PrintBlock(f.Body, sb, level + 1);
                break;

            case GoGenDecl g:
                Indent(sb, level);
                sb.AppendLine($"{Capitalize(g.Keyword)}Decl  @{g.Line}");
                break;

            default:
                Indent(sb, level);
                sb.AppendLine($"Decl  @{decl.Line}");
                break;
        }
    }

    private static void PrintBlock(GoBlock block, StringBuilder sb, int level)
    {
        foreach (GoStmt stmt in block.Statements)
            PrintStmt(stmt, sb, level);
    }

    private static void PrintStmt(GoStmt stmt, StringBuilder sb, int level)
    {
        switch (stmt)
        {
            case GoIfStmt ifs:
                PrintIf(ifs, sb, level, isElseIf: false);
                break;

            case GoForStmt fs:
                Indent(sb, level);
                sb.AppendLine($"{(fs.IsRange ? "ForRangeStmt" : "ForStmt")}  @{fs.Line}");
                PrintBlock(fs.Body, sb, level + 1);
                break;

            case GoSwitchStmt sw:
                PrintSwitch(sw, sb, level);
                break;

            case GoBlockStmt blk:
                Indent(sb, level);
                sb.AppendLine($"BlockStmt  @{blk.Line}");
                PrintBlock(blk.Block, sb, level + 1);
                break;

            case GoLabeledStmt lbl:
                Indent(sb, level);
                sb.AppendLine($"LabeledStmt  @{lbl.Line}");
                if (lbl.Inner != null)
                    PrintStmt(lbl.Inner, sb, level + 1);
                break;

            case GoReturnStmt:
                Indent(sb, level);
                sb.AppendLine($"ReturnStmt  @{stmt.Line}");
                break;

            case GoBranchStmt:
                Indent(sb, level);
                sb.AppendLine($"BranchStmt  @{stmt.Line}");
                break;

            case GoDeclStmt:
                Indent(sb, level);
                sb.AppendLine($"DeclStmt  @{stmt.Line}");
                break;

            default:
                Indent(sb, level);
                sb.AppendLine($"ExprStmt  @{stmt.Line}");
                break;
        }
    }

    private static void PrintIf(GoIfStmt ifs, StringBuilder sb, int level, bool isElseIf)
    {
        Indent(sb, level);
        sb.AppendLine($"{(isElseIf ? "ElseIfStmt" : "IfStmt")}  @{ifs.Line}");
        PrintBlock(ifs.Body, sb, level + 1);

        if (ifs.Else is GoIfStmt elseIf)
        {
            PrintIf(elseIf, sb, level, isElseIf: true);
        }
        else if (ifs.Else is GoBlockStmt block)
        {
            Indent(sb, level);
            sb.AppendLine($"Else  @{block.Line}");
            PrintBlock(block.Block, sb, level + 1);
        }
    }

    private static void PrintSwitch(GoSwitchStmt sw, StringBuilder sb, int level)
    {
        string kind = sw.IsSelect ? "SelectStmt"
            : sw.IsTypeSwitch ? "TypeSwitchStmt"
            : "SwitchStmt";
        Indent(sb, level);
        sb.AppendLine($"{kind}  @{sw.Line}  (ветвей: {sw.Cases.Count})");

        foreach (GoCaseClause clause in sw.Cases)
        {
            Indent(sb, level + 1);
            sb.AppendLine($"{(clause.IsDefault ? "DefaultClause" : "CaseClause")}  @{clause.Line}");
            PrintBlock(clause.Body, sb, level + 2);
        }
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
