namespace GilbMetricParser;

/// <summary>
/// Полоса с номерами строк для элемента <see cref="RichTextBox"/>.
/// Отрисовывает номера только видимых строк и синхронизируется с прокруткой.
/// </summary>
public sealed class LineNumberStrip : Control
{
    private readonly RichTextBox _box;

    public LineNumberStrip(RichTextBox box)
    {
        _box = box;
        DoubleBuffered = true;
        Width = 52;
        BackColor = Color.FromArgb(240, 242, 245);
        ForeColor = Color.FromArgb(120, 130, 140);
        Font = box.Font;
        TabStop = false;

        _box.VScroll += (_, _) => Invalidate();
        _box.TextChanged += (_, _) => Invalidate();
        _box.Resize += (_, _) => Invalidate();
        _box.FontChanged += (_, _) =>
        {
            Font = _box.Font;
            Invalidate();
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // Разделительная линия между полосой и текстом.
        using (var pen = new Pen(Color.FromArgb(210, 215, 220)))
            e.Graphics.DrawLine(pen, Width - 1, 0, Width - 1, Height);

        int textLength = _box.TextLength;
        if (textLength == 0)
        {
            // Даже у пустого документа есть первая строка.
            DrawNumber(e.Graphics, 0, 0);
            return;
        }

        int firstChar = _box.GetCharIndexFromPosition(new Point(1, 1));
        int firstLine = _box.GetLineFromCharIndex(firstChar);

        int lastChar = _box.GetCharIndexFromPosition(
            new Point(1, Math.Max(1, _box.ClientSize.Height - 2)));
        int lastLine = _box.GetLineFromCharIndex(lastChar);

        // Запас на случай частично видимой последней строки.
        lastLine = Math.Min(lastLine + 1, _box.GetLineFromCharIndex(textLength) + 1);

        for (int line = firstLine; line <= lastLine; line++)
        {
            int index = _box.GetFirstCharIndexFromLine(line);
            if (index < 0) continue;
            Point pos = _box.GetPositionFromCharIndex(index);
            DrawNumber(e.Graphics, line, pos.Y);
        }
    }

    private void DrawNumber(Graphics g, int zeroBasedLine, int y)
    {
        string text = (zeroBasedLine + 1).ToString();
        SizeF size = g.MeasureString(text, Font);
        float x = Width - size.Width - 8;
        using var brush = new SolidBrush(ForeColor);
        g.DrawString(text, Font, brush, x, y);
    }
}
