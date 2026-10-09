namespace SimpleBassShakerRouter;

sealed class BandViewPanel : Control
{
    readonly float[] _pass = new float[EnergyHistory.WindowSeconds * EnergyHistory.SamplesPerSecond];
    readonly float[] _reject = new float[EnergyHistory.WindowSeconds * EnergyHistory.SamplesPerSecond];
    int _count;
    bool _active;

    public BandViewPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.FromArgb(28, 30, 34);
        MinimumSize = new Size(100, 120);
    }

    public void SetActive(bool active)
    {
        if (_active == active)
            return;
        _active = active;
        if (!active)
            _count = 0;
        Invalidate();
    }

    public void UpdateHistory(EnergyHistory history)
    {
        history.CopyTo(_pass, _reject, out _count);
        Invalidate();
    }

    public void Clear()
    {
        _count = 0;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        var bounds = ClientRectangle;
        bounds.Inflate(-8, -8);
        if (bounds.Width < 20 || bounds.Height < 20)
            return;

        using var grid = new Pen(Color.FromArgb(55, 58, 64));
        for (int i = 1; i < 4; i++)
        {
            float y = bounds.Top + bounds.Height * i / 4f;
            g.DrawLine(grid, bounds.Left, y, bounds.Right, y);
        }

        float xStep = bounds.Width / (float)Math.Max(1, EnergyHistory.WindowSeconds);
        using var axisFont = new Font(Font.FontFamily, 8f);
        using var axisBrush = new SolidBrush(Color.FromArgb(150, 154, 160));
        for (int second = 0; second <= EnergyHistory.WindowSeconds; second++)
        {
            float x = bounds.Right - second * xStep;
            g.DrawLine(grid, x, bounds.Top, x, bounds.Bottom);
            string label = second == 0 ? "now" : $"-{second}s";
            SizeF size = g.MeasureString(label, axisFont);
            g.DrawString(label, axisFont, axisBrush, x - size.Width / 2f, bounds.Bottom - size.Height);
        }

        if (!_active)
        {
            DrawCentered(g, bounds, "Live view is off. Turn it on when you want pass vs reject energy.");
            return;
        }

        if (_count < 2)
        {
            DrawCentered(g, bounds, "Waiting for audio…");
            DrawLegend(g, bounds);
            return;
        }

        float max = 0.05f;
        for (int i = 0; i < _count; i++)
            max = Math.Max(max, Math.Max(_pass[i], _reject[i]));

        using var rejectBrush = new SolidBrush(Color.FromArgb(120, 140, 145, 155));
        using var passBrush = new SolidBrush(Color.FromArgb(170, 70, 180, 120));
        using var rejectPen = new Pen(Color.FromArgb(200, 170, 175, 185), 1.5f);
        using var passPen = new Pen(Color.FromArgb(230, 90, 210, 140), 1.8f);

        DrawFilled(g, bounds, _reject, _count, max, rejectBrush, rejectPen);
        DrawFilled(g, bounds, _pass, _count, max, passBrush, passPen);
        DrawLegend(g, bounds);
    }

    static void DrawFilled(Graphics g, Rectangle bounds, float[] values, int count, float max, Brush brush, Pen pen)
    {
        float chartBottom = bounds.Bottom - 16;
        float chartHeight = chartBottom - bounds.Top;
        if (chartHeight < 10)
            return;

        var points = new PointF[count + 2];
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            float x = bounds.Left + t * bounds.Width;
            float y = chartBottom - Math.Clamp(values[i] / max, 0f, 1f) * chartHeight;
            points[i] = new PointF(x, y);
        }

        points[count] = new PointF(bounds.Right, chartBottom);
        points[count + 1] = new PointF(bounds.Left, chartBottom);
        g.FillPolygon(brush, points);
        g.DrawLines(pen, points.AsSpan(0, count).ToArray());
    }

    void DrawLegend(Graphics g, Rectangle bounds)
    {
        using var font = new Font(Font.FontFamily, 8.5f);
        using var text = new SolidBrush(Color.FromArgb(210, 214, 220));
        using var pass = new SolidBrush(Color.FromArgb(90, 210, 140));
        using var reject = new SolidBrush(Color.FromArgb(170, 175, 185));
        const int box = 10;
        int y = bounds.Top + 4;
        int x = bounds.Left + 4;
        g.FillRectangle(pass, x, y, box, box);
        g.DrawString("Passes cutoff → shaker", font, text, x + box + 4, y - 2);
        SizeF size = g.MeasureString("Passes cutoff → shaker", font);
        x += (int)size.Width + box + 24;
        g.FillRectangle(reject, x, y, box, box);
        g.DrawString("Filtered out", font, text, x + box + 4, y - 2);
    }

    static void DrawCentered(Graphics g, Rectangle bounds, string message)
    {
        using var font = new Font("Segoe UI", 9.5f);
        using var brush = new SolidBrush(Color.FromArgb(170, 174, 180));
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        g.DrawString(message, font, brush, bounds, format);
    }
}
