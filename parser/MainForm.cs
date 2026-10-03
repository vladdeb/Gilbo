using System.Text;

namespace GilbMetricParser;

public sealed class MainForm : Form
{
    private readonly RichTextBox _sourceBox = new();
    private readonly DataGridView _grid = new();
    private readonly Label _clValue = new();
    private readonly Label _clRelValue = new();
    private readonly Label _cliValue = new();
    private readonly Label _operatorsValue = new();
    private readonly Label _fileLabel = new();
    private readonly Label _warningsLabel = new();
    //private readonly ToolStripStatusLabel _status = new();
    private SplitContainer? _mainSplit;
    private SplitContainer? _rightSplit;
    private LineNumberStrip? _lineNumbers;
    private string? _currentPath;

    public MainForm()
    {
        Text = "Метрика Джилба — парсер языка Go";
        Width = 1200;
        Height = 780;
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        BuildMenu();
        BuildLayout();
        //BuildStatusBar();

        Load += (_, _) => ApplyInitialSplitPositions();
        Shown += (_, _) => LoadSampleIfAvailable();
    }

    private void ApplyInitialSplitPositions()
    {
        try
        {
            if (_mainSplit != null)
                _mainSplit.SplitterDistance = Math.Max(320, (int)(_mainSplit.Width * 0.55));
            if (_rightSplit != null)
                _rightSplit.SplitterDistance = Math.Max(240, (int)(_rightSplit.Height * 0.45));
        }
        catch
        {
            // Некритично: при нестандартных размерах оставляем позиции по умолчанию.
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Построение интерфейса
    // ─────────────────────────────────────────────────────────────────────

    private void BuildMenu()
    {
        var menu = new MenuStrip();

        var fileMenu = new ToolStripMenuItem("Файл");
        fileMenu.DropDownItems.Add("Открыть программу на Go…", null, (_, _) => OpenFile());
        fileMenu.DropDownItems.Add("Загрузить демонстрационный пример", null, (_, _) => LoadSampleIfAvailable());
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add("Выход", null, (_, _) => Close());

        var analysisMenu = new ToolStripMenuItem("Анализ");
        analysisMenu.DropDownItems.Add("Выполнить расчёт метрики", null, (_, _) => AnalyzeCurrent());

        menu.Items.Add(fileMenu);
        menu.Items.Add(analysisMenu);
        MainMenuStrip = menu;
        Controls.Add(menu);
    }

    private void BuildLayout()
    {
        /*var toolStrip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, ImageScalingSize = new Size(16, 16) };
        var openButton = new ToolStripButton("Открыть файл…");
        openButton.Click += (_, _) => OpenFile();
        var sampleButton = new ToolStripButton("Демонстрационный пример");
        sampleButton.Click += (_, _) => LoadSampleIfAvailable();
        var analyzeButton = new ToolStripButton("Рассчитать метрику") { Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        analyzeButton.Click += (_, _) => AnalyzeCurrent();
        toolStrip.Items.Add(openButton);
        toolStrip.Items.Add(sampleButton);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(analyzeButton);*/

        _fileLabel.Text = "Файл не загружен";
        _fileLabel.AutoSize = true;
        _fileLabel.Padding = new Padding(6, 6, 6, 2);
        _fileLabel.ForeColor = Color.DimGray;

        var topPanel = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0) };
        topPanel.Controls.Add(_fileLabel);
        //topPanel.Controls.Add(toolStrip);

        // Левая часть — исходный код с полосой номеров строк.
        _sourceBox.Dock = DockStyle.Fill;
        _sourceBox.Font = new Font("Comic Sans MS", 10f);
        //_sourceBox.ReadOnly = true;
        _sourceBox.WordWrap = false;
        _sourceBox.ScrollBars = RichTextBoxScrollBars.Both;
        _sourceBox.BackColor = Color.White;
        _sourceBox.HideSelection = false;
        _sourceBox.BorderStyle = BorderStyle.None;

        _lineNumbers = new LineNumberStrip(_sourceBox) { Dock = DockStyle.Left };

        var editorPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
        };
        editorPanel.Controls.Add(_sourceBox);
        editorPanel.Controls.Add(_lineNumbers);

        var sourceGroup = new GroupBox { Text = "Исходный код программы на Go", Dock = DockStyle.Fill, Padding = new Padding(6) };
        sourceGroup.Controls.Add(editorPanel);

        // Правая часть — результаты и список конструкций.
        var rightSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
        };
        _rightSplit = rightSplit;

        rightSplit.Panel1.Controls.Add(BuildResultsPanel());

        var gridGroup = new GroupBox { Text = "Найденные управляющие конструкции", Dock = DockStyle.Fill, Padding = new Padding(6) };
        BuildGrid();
        gridGroup.Controls.Add(_grid);
        rightSplit.Panel2.Controls.Add(gridGroup);

        var mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
        };
        _mainSplit = mainSplit;
        mainSplit.Panel1.Controls.Add(sourceGroup);
        mainSplit.Panel2.Controls.Add(rightSplit);

        Controls.Add(mainSplit);
        Controls.Add(topPanel);
    }

    private Control BuildResultsPanel()
    {
        var group = new GroupBox { Text = "Результаты расчёта метрики Джилба", Dock = DockStyle.Fill, Padding = new Padding(8) };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(4),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddResultRow(layout, 0, "CL — абсолютная сложность", _clValue, Color.FromArgb(0, 90, 160));
        AddResultRow(layout, 1, "cl — относительная сложность", _clRelValue, Color.FromArgb(0, 120, 60));
        AddResultRow(layout, 2, "CLI — максимальный уровень вложенности", _cliValue, Color.FromArgb(170, 60, 0));
        AddResultRow(layout, 3, "Всего операторов программы", _operatorsValue, Color.FromArgb(70, 70, 70));

        _warningsLabel.Dock = DockStyle.Fill;
        _warningsLabel.ForeColor = Color.Firebrick;
        _warningsLabel.AutoSize = false;
        _warningsLabel.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(_warningsLabel, 0, 4);
        layout.SetColumnSpan(_warningsLabel, 2);

        group.Controls.Add(layout);
        return group;
    }

    private static void AddResultRow(TableLayoutPanel layout, int row, string caption, Label valueLabel, Color color)
    {
        var captionLabel = new Label
        {
            Text = caption,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false,
            Font = new Font("Segoe UI", 9.5f),
        };
        valueLabel.Text = "—";
        valueLabel.Dock = DockStyle.Fill;
        valueLabel.TextAlign = ContentAlignment.MiddleLeft;
        valueLabel.AutoSize = false;
        valueLabel.Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold);
        valueLabel.ForeColor = color;

        layout.Controls.Add(captionLabel, 0, row);
        layout.Controls.Add(valueLabel, 1, row);
    }

    private void BuildGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.BackgroundColor = Color.White;
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(245, 248, 252);

        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Kind", HeaderText = "Тип", FillWeight = 18 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Line", HeaderText = "Строка", FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Level", HeaderText = "Уровень влож.", FillWeight = 16 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Contribution", HeaderText = "Вклад в CL", FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Detail", HeaderText = "Детали", FillWeight = 40 });

        _grid.SelectionChanged += (_, _) => HighlightSelectedLine();
    }

    /*private void BuildStatusBar()
    {
        var strip = new StatusStrip();
        _status.Text = "Готово";
        strip.Items.Add(_status);
        Controls.Add(strip);
    }*/

    // ─────────────────────────────────────────────────────────────────────
    //  Действия
    // ─────────────────────────────────────────────────────────────────────

    private void OpenFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Выберите программу на языке Go",
            Filter = "Программы на Go (*.go)|*.go|Все файлы (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            LoadFile(dialog.FileName);
        }
    }

    private void LoadFile(string path)
    {
        try
        {
            _sourceBox.Text = File.ReadAllText(path);
            _currentPath = path;
            _fileLabel.Text = $"Файл: {path}";
            _fileLabel.ForeColor = SystemColors.ControlText;
            //_status.Text = "Файл загружен. Нажмите «Рассчитать метрику».";
            AnalyzeCurrent();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось открыть файл:\n" + ex.Message, "Ошибка",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadSampleIfAvailable()
    {
        string? sample = LocateSampleFile();
        if (sample == null)
        {
            MessageBox.Show(this,
                "Демонстрационный файл analyzed_program.go не найден рядом с приложением.\n" +
                "Откройте его вручную через «Файл → Открыть программу на Go…».",
                "Файл не найден", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        LoadFile(sample);
    }

    private static string? LocateSampleFile()
    {
        string baseDir = AppContext.BaseDirectory;
        string cwd = Directory.GetCurrentDirectory();
        string[] candidates =
        {
            Path.Combine(baseDir, "analyzed_program.go"),
            Path.Combine(baseDir, "..", "..", "..", "..", "go", "analyzed_program.go"),
            Path.Combine(baseDir, "..", "..", "..", "go", "analyzed_program.go"),
            Path.Combine(cwd, "go", "analyzed_program.go"),
            Path.Combine(cwd, "analyzed_program.go"),
        };
        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }
        return null;
    }

    private void AnalyzeCurrent()
    {
        string source = _sourceBox.Text;
        if (string.IsNullOrWhiteSpace(source))
        {
            MessageBox.Show(this, "Сначала загрузите программу на языке Go.", "Нет данных",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        AnalysisResult result = GoAnalyzer.Analyze(source);

        _clValue.Text = result.AbsoluteComplexity.ToString();
        _clRelValue.Text = result.RelativeComplexity.ToString("F4");
        _cliValue.Text = result.MaxNestingLevel.ToString();
        _operatorsValue.Text = result.TotalOperators.ToString();

        _grid.Rows.Clear();
        foreach (Construct c in result.Constructs)
        {
            _grid.Rows.Add(c.KindName, c.Line, c.Level, c.ClContribution, c.Detail);
        }
        _grid.ClearSelection();

        if (result.Warnings.Count > 0)
        {
            _warningsLabel.Text = "⚠ " + string.Join("  ", result.Warnings);
        }
        else
        {
            _warningsLabel.Text = string.Empty;
        }

        //_status.Text = $"Готово. Найдено конструкций: {result.Constructs.Count}. " +
        //               $"CL = {result.AbsoluteComplexity}, cl = {result.RelativeComplexity:F4}, CLI = {result.MaxNestingLevel}.";
    }

    private void HighlightSelectedLine()
    {
        if (_grid.SelectedRows.Count == 0) return;
        if (!int.TryParse(Convert.ToString(_grid.SelectedRows[0].Cells["Line"].Value), out int line))
            return;

        try
        {
            int index = _sourceBox.GetFirstCharIndexFromLine(Math.Max(0, line - 1));
            if (index < 0) return;
            _sourceBox.Select(index, _sourceBox.Lines[Math.Max(0, line - 1)].Length);
            _sourceBox.ScrollToCaret();
            _lineNumbers?.Invalidate();
        }
        catch
        {
            // игнорируем ошибки позиционирования
        }
    }
}
