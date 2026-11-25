using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SimTrade.Controls;

public sealed record MultiChartSeries(
    string Name,
    Brush Stroke,
    IReadOnlyList<ChartSample> Samples);

public class MultiLineChart : FrameworkElement
{
    static readonly Brush GridBrush = Freeze(Color.FromRgb(46, 38, 56));
    static readonly Pen GridPen = FreezePen(GridBrush);
    static readonly Brush LabelBrush = Freeze(Color.FromRgb(122, 112, 136));
    static readonly Typeface Typeface = new("Segoe UI");
    static readonly Typeface ValueTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-MX");
    static readonly Brush HoverBrush = Freeze(Color.FromRgb(244, 241, 248));
    static readonly Brush HoverMutedBrush = Freeze(Color.FromRgb(168, 155, 180));
    static readonly Brush HoverFill = Freeze(Color.FromRgb(25, 20, 31));
    static readonly Brush DotFill = Freeze(Color.FromRgb(17, 14, 20));
    static readonly Pen HoverPen = CreateDashPen(Color.FromArgb(210, 168, 155, 180));
    static readonly Pen HoverBorderPen = FreezePen(Color.FromRgb(46, 38, 56));

    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series),
        typeof(IReadOnlyList<MultiChartSeries>),
        typeof(MultiLineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValueFormatProperty = DependencyProperty.Register(
        nameof(ValueFormat),
        typeof(string),
        typeof(MultiLineChart),
        new FrameworkPropertyMetadata("+0.00;-0.00;0.00", FrameworkPropertyMetadataOptions.AffectsRender));

    Point? _mouse;

    public MultiLineChart()
    {
        SnapsToDevicePixels = true;
        Cursor = Cursors.Cross;
        MouseMove += (_, e) =>
        {
            _mouse = e.GetPosition(this);
            InvalidateVisual();
        };
        MouseLeave += (_, _) =>
        {
            _mouse = null;
            InvalidateVisual();
        };
    }

    public IReadOnlyList<MultiChartSeries>? Series
    {
        get => (IReadOnlyList<MultiChartSeries>?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public string ValueFormat
    {
        get => (string)GetValue(ValueFormatProperty);
        set => SetValue(ValueFormatProperty, value);
    }

    protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters) =>
        new PointHitTestResult(this, hitTestParameters.HitPoint);

    protected override void OnRender(DrawingContext drawingContext)
    {
        var series = Series;
        double width = ActualWidth;
        double height = ActualHeight;
        if (series is null || series.Count == 0 || width < 40 || height < 40)
            return;

        var axis = series[0].Samples;
        if (axis.Count < 2)
            return;

        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;
        foreach (var line in series)
        {
            foreach (var sample in line.Samples)
            {
                if (sample.Value is not double value || !double.IsFinite(value))
                    continue;
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }
        }

        if (!double.IsFinite(min) || !double.IsFinite(max))
            return;

        // Incluye el 0% como referencia visual.
        min = Math.Min(min, 0);
        max = Math.Max(max, 0);
        var pad = Math.Max((max - min) * 0.08, 0.25);
        min -= pad;
        max += pad;
        if (max - min < 1e-9)
        {
            min -= 1;
            max += 1;
        }

        const double padRight = 12;
        const double padTop = 10;
        const double padBottom = 32;
        double plotHeight = Math.Max(1, height - padTop - padBottom);
        var ticks = BuildTicks(min, max, plotHeight);
        var labels = new List<(double Value, FormattedText Text)>(ticks.Count);
        double labelWidth = 0;
        foreach (var tick in ticks)
        {
            var text = MakeText(Format(tick) + "%", 11);
            labelWidth = Math.Max(labelWidth, text.Width);
            labels.Add((tick, text));
        }

        double padLeft = Math.Max(52, labelWidth + 10);
        double plotWidth = Math.Max(1, width - padLeft - padRight);
        int last = axis.Count - 1;

        double X(int index) => padLeft + plotWidth * index / last;
        double Y(double value) => padTop + (max - value) / (max - min) * plotHeight;

        foreach (var (value, text) in labels)
        {
            double y = Y(value);
            drawingContext.DrawLine(GridPen, new Point(padLeft, y), new Point(width - padRight, y));
            drawingContext.DrawText(text, new Point(padLeft - 8 - text.Width, y - text.Height / 2));
        }

        // Línea de referencia en 0%.
        if (0 >= min && 0 <= max)
        {
            var zeroY = Y(0);
            drawingContext.DrawLine(
                new Pen(Freeze(Color.FromArgb(140, 168, 155, 180)), 1),
                new Point(padLeft, zeroY),
                new Point(width - padRight, zeroY));
        }

        DrawDateAxis(drawingContext, axis, X, padLeft, width - padRight, padTop, plotHeight, height);

        foreach (var line in series)
        {
            if (line.Samples.Count != axis.Count)
                continue;

            var geometry = new StreamGeometry();
            var open = false;
            using (var context = geometry.Open())
            {
                for (var i = 0; i < line.Samples.Count; i++)
                {
                    if (line.Samples[i].Value is not double value || !double.IsFinite(value))
                    {
                        open = false;
                        continue;
                    }

                    var point = new Point(X(i), Y(value));
                    if (!open)
                    {
                        context.BeginFigure(point, false, false);
                        open = true;
                    }
                    else
                    {
                        context.LineTo(point, true, false);
                    }
                }
            }

            geometry.Freeze();
            drawingContext.DrawGeometry(null, new Pen(line.Stroke, 2)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            }, geometry);
        }

        if (_mouse is not Point mouse || mouse.X < padLeft || mouse.X > width - padRight)
            return;

        var hoverIndex = (int)Math.Round((mouse.X - padLeft) / plotWidth * last);
        hoverIndex = Math.Clamp(hoverIndex, 0, last);
        DrawHover(drawingContext, series, hoverIndex, X, Y, padLeft, width - padRight, padTop, plotHeight);
    }

    void DrawHover(
        DrawingContext drawingContext,
        IReadOnlyList<MultiChartSeries> series,
        int index,
        Func<int, double> x,
        Func<double, double> y,
        double plotLeft,
        double plotRight,
        double plotTop,
        double plotHeight)
    {
        var day = series[0].Samples[index].Day;
        var atX = x(index);
        drawingContext.DrawLine(HoverPen, new Point(atX, plotTop), new Point(atX, plotTop + plotHeight));

        var rows = new List<(FormattedText Name, FormattedText Value, Brush Stroke, Point? Dot)>();
        foreach (var line in series)
        {
            if (index >= line.Samples.Count)
                continue;
            if (line.Samples[index].Value is not double value || !double.IsFinite(value))
                continue;

            var name = MakeText(line.Name, 11, HoverMutedBrush);
            var valueText = MakeText(Format(value) + "%", 12, HoverBrush, ValueTypeface);
            rows.Add((name, valueText, line.Stroke, new Point(atX, y(value))));
        }

        if (rows.Count == 0)
            return;

        foreach (var row in rows)
        {
            if (row.Dot is Point dot)
                drawingContext.DrawEllipse(DotFill, new Pen(row.Stroke, 2), dot, 4, 4);
        }

        var dateText = MakeText(day.ToString("dd MMM yyyy", Culture), 11, HoverMutedBrush);
        double contentWidth = dateText.Width;
        double contentHeight = dateText.Height + 4;
        foreach (var row in rows)
        {
            contentWidth = Math.Max(contentWidth, row.Name.Width + 18 + row.Value.Width);
            contentHeight += Math.Max(row.Name.Height, row.Value.Height) + 2;
        }

        var boxWidth = contentWidth + 16;
        var boxHeight = contentHeight + 10;
        var left = atX + 14;
        if (left + boxWidth > plotRight)
            left = atX - 14 - boxWidth;
        left = Math.Clamp(left, plotLeft, Math.Max(plotLeft, plotRight - boxWidth));
        var top = plotTop + 8;
        if (top + boxHeight > plotTop + plotHeight)
            top = Math.Max(plotTop, plotTop + plotHeight - boxHeight);

        drawingContext.DrawRoundedRectangle(HoverFill, HoverBorderPen, new Rect(left, top, boxWidth, boxHeight), 6, 6);
        drawingContext.DrawText(dateText, new Point(left + 8, top + 4));
        var cursorY = top + 4 + dateText.Height + 4;
        foreach (var row in rows)
        {
            drawingContext.DrawEllipse(row.Stroke, null, new Point(left + 12, cursorY + row.Name.Height / 2), 3.5, 3.5);
            drawingContext.DrawText(row.Name, new Point(left + 20, cursorY));
            drawingContext.DrawText(row.Value, new Point(left + boxWidth - 8 - row.Value.Width, cursorY));
            cursorY += Math.Max(row.Name.Height, row.Value.Height) + 2;
        }
    }

    void DrawDateAxis(
        DrawingContext drawingContext,
        IReadOnlyList<ChartSample> samples,
        Func<int, double> x,
        double plotLeft,
        double plotRight,
        double plotTop,
        double plotHeight,
        double height)
    {
        var maxLabels = Math.Max(2, (int)((plotRight - plotLeft) / 72));
        var step = 1;
        while (samples.Count / step > maxLabels)
            step *= 2;

        double lastLabelRight = double.NegativeInfinity;
        var ticks = new List<int> { 0 };
        for (var i = step; i < samples.Count - 1; i += step)
            ticks.Add(i);
        if (ticks[^1] != samples.Count - 1)
            ticks.Add(samples.Count - 1);

        for (var i = 0; i < ticks.Count; i++)
        {
            var index = ticks[i];
            var text = MakeText(samples[index].Day.ToString("dd MMM", Culture), 11);
            var at = x(index);
            var left = i == ticks.Count - 1
                ? Math.Min(at, plotRight - text.Width)
                : at;
            left = Math.Max(plotLeft, left);
            if (i < ticks.Count - 1 && left < lastLabelRight + 8)
                continue;

            drawingContext.DrawLine(GridPen, new Point(at, plotTop), new Point(at, plotTop + plotHeight));
            drawingContext.DrawText(text, new Point(left, height - 20));
            lastLabelRight = left + text.Width;
        }
    }

    static List<double> BuildTicks(double min, double max, double plotHeight)
    {
        var span = max - min;
        var rough = span / Math.Max(3, plotHeight / 48);
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(rough, 1e-9))));
        var residual = rough / magnitude;
        var step = residual <= 1 ? magnitude
            : residual <= 2 ? 2 * magnitude
            : residual <= 5 ? 5 * magnitude
            : 10 * magnitude;

        var start = Math.Floor(min / step) * step;
        var ticks = new List<double>();
        for (var value = start; value <= max + step * 0.5; value += step)
        {
            if (value >= min - step * 0.01 && value <= max + step * 0.01)
                ticks.Add(value);
        }

        if (ticks.Count == 0)
            ticks.Add(min);
        return ticks;
    }

    string Format(double value) => value.ToString(ValueFormat, Culture);

    static FormattedText MakeText(string text, double size, Brush? brush = null, Typeface? typeface = null) =>
        new(
            text,
            Culture,
            FlowDirection.LeftToRight,
            typeface ?? Typeface,
            size,
            brush ?? LabelBrush,
            1.25);

    static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    static Pen FreezePen(Brush brush)
    {
        var pen = new Pen(brush, 1);
        pen.Freeze();
        return pen;
    }

    static Pen FreezePen(Color color) => FreezePen(Freeze(color));

    static Pen CreateDashPen(Color color)
    {
        var pen = new Pen(Freeze(color), 1)
        {
            DashStyle = DashStyles.Dash
        };
        pen.Freeze();
        return pen;
    }
}
