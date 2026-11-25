using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SimTrade.Controls;

public sealed record ChartSample(DateOnly Day, double? Value, DateTime? Stamp = null);

public sealed record ChartOverlay(string Name, Brush Stroke, IReadOnlyList<ChartSample> Samples);

public enum ChartFill
{
    None,
    Up,
    Down,
    Flat,
    Rsi
}

public class LineChart : FrameworkElement
{
    static readonly Brush GridBrush = Freeze(Color.FromRgb(46, 38, 56));
    static readonly Pen GridPen = FreezePen(GridBrush);
    static readonly Brush LabelBrush = Freeze(Color.FromRgb(122, 112, 136));
    static readonly Brush BuyBandBrush = Freeze(Color.FromArgb(90, 52, 211, 153));
    static readonly Brush SellBandBrush = Freeze(Color.FromArgb(70, 248, 113, 113));
    static readonly Pen BuyPen = FreezePen(Color.FromRgb(52, 211, 153));
    static readonly Pen SellPen = FreezePen(Color.FromRgb(248, 113, 113));
    static readonly Typeface Typeface = new("Segoe UI");
    static readonly Typeface ValueTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-MX");
    static readonly Brush HoverBrush = Freeze(Color.FromRgb(244, 241, 248));
    static readonly Brush HoverMutedBrush = Freeze(Color.FromRgb(168, 155, 180));
    static readonly Brush HoverFill = Freeze(Color.FromRgb(25, 20, 31));
    static readonly Brush DotFill = Freeze(Color.FromRgb(17, 14, 20));
    static readonly Pen HoverPen = CreateDashPen(Color.FromArgb(210, 168, 155, 180));
    static readonly Pen HoverBorderPen = FreezePen(Color.FromRgb(46, 38, 56));

    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples),
        typeof(IReadOnlyList<ChartSample>),
        typeof(LineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSamplesChanged));

    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush),
        typeof(Brush),
        typeof(LineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FixedMinimumProperty = DependencyProperty.Register(
        nameof(FixedMinimum),
        typeof(double),
        typeof(LineChart),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FixedMaximumProperty = DependencyProperty.Register(
        nameof(FixedMaximum),
        typeof(double),
        typeof(LineChart),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LowerGuideProperty = DependencyProperty.Register(
        nameof(LowerGuide),
        typeof(double),
        typeof(LineChart),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UpperGuideProperty = DependencyProperty.Register(
        nameof(UpperGuide),
        typeof(double),
        typeof(LineChart),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillKindProperty = DependencyProperty.Register(
        nameof(FillKind),
        typeof(ChartFill),
        typeof(LineChart),
        new FrameworkPropertyMetadata(ChartFill.None, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValueFormatProperty = DependencyProperty.Register(
        nameof(ValueFormat),
        typeof(string),
        typeof(LineChart),
        new FrameworkPropertyMetadata("auto", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OverlaysProperty = DependencyProperty.Register(
        nameof(Overlays),
        typeof(IReadOnlyList<ChartOverlay>),
        typeof(LineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    Point? _mouse;
    DateTime? _mouseMark;
    DateTime? _linkedMark;

    public event EventHandler<DateTime?>? HoverDateChanged;

    public LineChart()
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
            _mouseMark = null;
            InvalidateVisual();
            HoverDateChanged?.Invoke(this, null);
        };
    }

    public void LinkHover(DateTime? mark)
    {
        if (_mouse is not null || _linkedMark == mark)
            return;

        _linkedMark = mark;
        InvalidateVisual();
    }

    protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters) =>
        new PointHitTestResult(this, hitTestParameters.HitPoint);

    static void OnSamplesChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var chart = (LineChart)sender;
        chart._linkedMark = null;
        chart._mouseMark = null;
    }

    public IReadOnlyList<ChartSample>? Samples
    {
        get => (IReadOnlyList<ChartSample>?)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public Brush? LineBrush
    {
        get => (Brush?)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public double FixedMinimum
    {
        get => (double)GetValue(FixedMinimumProperty);
        set => SetValue(FixedMinimumProperty, value);
    }

    public double FixedMaximum
    {
        get => (double)GetValue(FixedMaximumProperty);
        set => SetValue(FixedMaximumProperty, value);
    }

    public double LowerGuide
    {
        get => (double)GetValue(LowerGuideProperty);
        set => SetValue(LowerGuideProperty, value);
    }

    public double UpperGuide
    {
        get => (double)GetValue(UpperGuideProperty);
        set => SetValue(UpperGuideProperty, value);
    }

    public string ValueFormat
    {
        get => (string)GetValue(ValueFormatProperty);
        set => SetValue(ValueFormatProperty, value);
    }

    public ChartFill FillKind
    {
        get => (ChartFill)GetValue(FillKindProperty);
        set => SetValue(FillKindProperty, value);
    }

    public IReadOnlyList<ChartOverlay>? Overlays
    {
        get => (IReadOnlyList<ChartOverlay>?)GetValue(OverlaysProperty);
        set => SetValue(OverlaysProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var samples = Samples;
        double width = ActualWidth;
        double height = ActualHeight;
        if (samples is null || samples.Count < 2 || width < 40 || height < 40)
            return;

        double min = FixedMinimum;
        double max = FixedMaximum;
        if (!double.IsFinite(min) || !double.IsFinite(max))
        {
            min = double.PositiveInfinity;
            max = double.NegativeInfinity;
            foreach (var sample in samples)
            {
                if (sample.Value is not double value || !double.IsFinite(value))
                    continue;
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }

            if (Overlays is not null)
            {
                foreach (var overlay in Overlays)
                {
                    foreach (var sample in overlay.Samples)
                    {
                        if (sample.Value is not double value || !double.IsFinite(value))
                            continue;
                        min = Math.Min(min, value);
                        max = Math.Max(max, value);
                    }
                }
            }

            if (!double.IsFinite(min) || !double.IsFinite(max))
                return;

            var pad = Math.Max((max - min) * 0.08, Math.Abs(max) * 0.01);
            if (pad < 1e-9)
                pad = 1;
            min -= pad;
            max += pad;
        }

        if (max - min < 1e-9)
        {
            min -= 1;
            max += 1;
        }

        const double padRight = 12;
        const double padTop = 10;
        const double padBottom = 32;
        double plotHeight = Math.Max(1, height - padTop - padBottom);
        var ticks = PriceTicks(min, max, plotHeight);
        var labels = new List<(double Value, FormattedText Text)>(ticks.Count);
        double labelWidth = 0;
        foreach (var tick in ticks)
        {
            var text = Text(Format(tick, ticks.Count < 2 ? 1 : StepOf(ticks)), 11);
            labelWidth = Math.Max(labelWidth, text.Width);
            labels.Add((tick, text));
        }

        double padLeft = Math.Max(52, labelWidth + 10);
        double plotWidth = Math.Max(1, width - padLeft - padRight);
        int last = samples.Count - 1;

        double X(int index) => padLeft + plotWidth * index / last;
        double Y(double value) => padTop + (max - value) / (max - min) * plotHeight;

        var baseline = padTop + plotHeight;
        var lowerLevel = double.IsFinite(LowerGuide) ? LowerGuide : 30;
        var upperLevel = double.IsFinite(UpperGuide) ? UpperGuide : 70;
        if (FillKind == ChartFill.Rsi)
            DrawSignalZones(drawingContext, padLeft, plotWidth, padTop, baseline, Y, lowerLevel, upperLevel);
        else if (FillKind is ChartFill.Up or ChartFill.Down or ChartFill.Flat)
            DrawArea(drawingContext, samples, X, Y, baseline, FadeFill(AreaColor(LineBrush, FillKind)));
        else if (double.IsFinite(LowerGuide) || double.IsFinite(UpperGuide))
        {
            if (double.IsFinite(LowerGuide))
            {
                double y = Y(LowerGuide);
                drawingContext.DrawRectangle(BuyBandBrush, null, new Rect(padLeft, y, plotWidth, baseline - y));
            }

            if (double.IsFinite(UpperGuide))
            {
                double y = Y(UpperGuide);
                drawingContext.DrawRectangle(SellBandBrush, null, new Rect(padLeft, padTop, plotWidth, Math.Max(0, y - padTop)));
            }
        }

        foreach (var (value, text) in labels)
        {
            double y = Y(value);
            drawingContext.DrawLine(GridPen, new Point(padLeft, y), new Point(width - padRight, y));
            drawingContext.DrawText(text, new Point(padLeft - 8 - text.Width, y - text.Height / 2));
        }

        if (double.IsFinite(LowerGuide))
            drawingContext.DrawLine(BuyPen, new Point(padLeft, Y(LowerGuide)), new Point(width - padRight, Y(LowerGuide)));
        if (double.IsFinite(UpperGuide))
            drawingContext.DrawLine(SellPen, new Point(padLeft, Y(UpperGuide)), new Point(width - padRight, Y(UpperGuide)));

        DrawDateAxis(drawingContext, samples, X, padLeft, width - padRight, padTop, plotHeight, height);

        var geometry = new StreamGeometry();
        bool open = false;
        using (var context = geometry.Open())
        {
            for (int i = 0; i < samples.Count; i++)
            {
                if (samples[i].Value is not double value || !double.IsFinite(value))
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
        var stroke = LineBrush ?? Brushes.White;
        if (FillKind == ChartFill.Rsi)
            DrawSignalLine(drawingContext, samples, X, Y, lowerLevel, upperLevel);
        else
        {
            drawingContext.DrawGeometry(null, new Pen(stroke, 1.8)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            }, geometry);
        }

        if (Overlays is not null)
        {
            foreach (var overlay in Overlays)
                DrawOverlayLine(drawingContext, overlay.Samples, X, Y, overlay.Stroke);
        }

        var hoverIndex = -1;
        if (_mouse is Point mouse && mouse.X >= padLeft && mouse.X <= width - padRight)
            hoverIndex = IndexAt(mouse.X, padLeft, plotWidth, last, samples);
        else if (_mouse is null && _linkedMark is DateTime linked)
            hoverIndex = IndexOf(samples, linked);

        DateTime? hoverMark = null;
        if (hoverIndex >= 0 && samples[hoverIndex].Value is double hovered && double.IsFinite(hovered))
        {
            hoverMark = Mark(samples[hoverIndex]);
            if (FillKind == ChartFill.Rsi)
                stroke = Freeze(SignalColor(hovered, lowerLevel, upperLevel));
            DrawHover(
                drawingContext,
                hoverMark.Value,
                samples[hoverIndex].Stamp is not null,
                hovered,
                new Point(X(hoverIndex), Y(hovered)),
                padLeft,
                width - padRight,
                padTop,
                plotHeight,
                stroke);
        }

        if (_mouse is not null && _mouseMark != hoverMark)
        {
            _mouseMark = hoverMark;
            HoverDateChanged?.Invoke(this, hoverMark);
        }
    }

    static readonly Color UpColor = Color.FromRgb(52, 211, 153);
    static readonly Color DownColor = Color.FromRgb(248, 113, 113);
    static readonly Color FlatColor = Color.FromRgb(244, 241, 248);
    static readonly Color NeutralColor = Color.FromRgb(244, 241, 248);
    static readonly Color OversoldColor = Color.FromRgb(52, 211, 153);
    static readonly Color OverboughtColor = Color.FromRgb(248, 113, 113);

    static Color AreaColor(Brush? lineBrush, ChartFill fill) =>
        lineBrush is SolidColorBrush solid
            ? solid.Color
            : fill switch
            {
                ChartFill.Up => UpColor,
                ChartFill.Down => DownColor,
                _ => FlatColor
            };

    static void DrawOverlayLine(
        DrawingContext drawingContext,
        IReadOnlyList<ChartSample> samples,
        Func<int, double> x,
        Func<double, double> y,
        Brush stroke)
    {
        var geometry = new StreamGeometry();
        var open = false;
        using (var context = geometry.Open())
        {
            for (var i = 0; i < samples.Count; i++)
            {
                if (samples[i].Value is not double value || !double.IsFinite(value))
                {
                    open = false;
                    continue;
                }

                var point = new Point(x(i), y(value));
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
        drawingContext.DrawGeometry(null, new Pen(stroke, 1.5)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
            DashStyle = DashStyles.Dash
        }, geometry);
    }

    static void DrawArea(
        DrawingContext drawingContext,
        IReadOnlyList<ChartSample> samples,
        Func<int, double> x,
        Func<double, double> y,
        double baseline,
        Brush fill)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var start = -1;
            for (var i = 0; i < samples.Count; i++)
            {
                if (samples[i].Value is not double value || !double.IsFinite(value))
                {
                    if (start >= 0)
                        CloseArea(context, x(start), x(i - 1), baseline);
                    start = -1;
                    continue;
                }

                var point = new Point(x(i), y(value));
                if (start < 0)
                {
                    context.BeginFigure(point, true, true);
                    start = i;
                }
                else
                {
                    context.LineTo(point, true, false);
                }
            }

            if (start >= 0)
                CloseArea(context, x(start), x(samples.Count - 1), baseline);
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(fill, null, geometry);
    }

    static void CloseArea(StreamGeometryContext context, double startX, double endX, double baseline)
    {
        context.LineTo(new Point(endX, baseline), true, false);
        context.LineTo(new Point(startX, baseline), true, false);
    }

    static Brush FadeFill(Color color)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(168, color.R, color.G, color.B), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(72, color.R, color.G, color.B), 0.42));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
        brush.Freeze();
        return brush;
    }

    static void DrawSignalZones(
        DrawingContext drawingContext,
        double plotLeft,
        double plotWidth,
        double plotTop,
        double baseline,
        Func<double, double> y,
        double lower,
        double upper)
    {
        if (double.IsFinite(lower))
        {
            var edge = y(lower);
            var height = baseline - edge;
            if (height > 0)
                drawingContext.DrawRectangle(ZoneBrush(OversoldColor, edge, baseline), null, new Rect(plotLeft, edge, plotWidth, height));
        }

        if (double.IsFinite(upper))
        {
            var edge = y(upper);
            var height = edge - plotTop;
            if (height > 0)
                drawingContext.DrawRectangle(ZoneBrush(OverboughtColor, edge, plotTop), null, new Rect(plotLeft, plotTop, plotWidth, height));
        }
    }

    static Brush ZoneBrush(Color color, double fadeY, double strongY)
    {
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, fadeY),
            EndPoint = new Point(0, strongY)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(18, color.R, color.G, color.B), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(70, color.R, color.G, color.B), 0.4));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(155, color.R, color.G, color.B), 1));
        brush.Freeze();
        return brush;
    }

    static void DrawSignalLine(
        DrawingContext drawingContext,
        IReadOnlyList<ChartSample> samples,
        Func<int, double> x,
        Func<double, double> y,
        double lower,
        double upper)
    {
        var buy = new List<List<Point>>();
        var neutral = new List<List<Point>>();
        var sell = new List<List<Point>>();
        List<Point>? run = null;
        var runZone = 0;

        void Ensure(int zone, Point point)
        {
            if (run is not null && runZone == zone)
            {
                run.Add(point);
                return;
            }

            run = [point];
            runZone = zone;
            (zone < 0 ? buy : zone > 0 ? sell : neutral).Add(run);
        }

        Point? previous = null;
        var previousValue = 0d;
        for (var i = 0; i < samples.Count; i++)
        {
            if (samples[i].Value is not double value || !double.IsFinite(value))
            {
                run = null;
                previous = null;
                continue;
            }

            var point = new Point(x(i), y(value));
            if (previous is not Point from)
            {
                Ensure(Zone(value, lower, upper), point);
            }
            else
            {
                Link(from, previousValue, point, value);
            }

            previous = point;
            previousValue = value;
        }

        void Link(Point from, double fromValue, Point to, double toValue)
        {
            var upward = toValue >= fromValue;
            var marks = new List<double>(2);
            if (upward)
            {
                if (fromValue < lower && toValue >= lower)
                    marks.Add(lower);
                if (fromValue < upper && toValue >= upper)
                    marks.Add(upper);
            }
            else
            {
                if (fromValue > upper && toValue <= upper)
                    marks.Add(upper);
                if (fromValue > lower && toValue <= lower)
                    marks.Add(lower);
            }

            var cursorZone = Zone(fromValue, lower, upper);
            foreach (var level in marks)
            {
                var span = toValue - fromValue;
                var t = Math.Abs(span) < 1e-9 ? 0 : Math.Clamp((level - fromValue) / span, 0, 1);
                var cross = new Point(from.X + ((to.X - from.X) * t), from.Y + ((to.Y - from.Y) * t));
                Ensure(cursorZone, cross);
                run = null;
                cursorZone = NextZone(level, lower, upward);
                Ensure(cursorZone, cross);
            }

            Ensure(Zone(toValue, lower, upper), to);
        }

        Paint(drawingContext, buy, UpColor);
        Paint(drawingContext, neutral, NeutralColor);
        Paint(drawingContext, sell, DownColor);
    }

    static int Zone(double value, double lower, double upper)
    {
        if (value <= lower)
            return -1;
        if (value >= upper)
            return 1;
        return 0;
    }

    static int NextZone(double level, double lower, bool upward)
    {
        if (Math.Abs(level - lower) < 0.0001)
            return upward ? 0 : -1;
        return upward ? 1 : 0;
    }

    static Color SignalColor(double value, double lower, double upper) =>
        Zone(value, lower, upper) switch
        {
            < 0 => UpColor,
            > 0 => DownColor,
            _ => NeutralColor
        };

    static void Paint(DrawingContext drawingContext, List<List<Point>> runs, Color color)
    {
        var geometry = new StreamGeometry();
        var any = false;
        using (var context = geometry.Open())
        {
            foreach (var run in runs)
            {
                if (run.Count < 2)
                    continue;
                any = true;
                context.BeginFigure(run[0], false, false);
                for (var i = 1; i < run.Count; i++)
                    context.LineTo(run[i], true, false);
            }
        }

        if (!any)
            return;

        geometry.Freeze();
        drawingContext.DrawGeometry(null, RoundPen(Color.FromArgb(90, color.R, color.G, color.B), 6.5), geometry);
        drawingContext.DrawGeometry(null, GradientPen(color), geometry);
    }

    static Pen GradientPen(Color color)
    {
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };
        brush.GradientStops.Add(new GradientStop(Lighten(color, 0.62), 0));
        brush.GradientStops.Add(new GradientStop(color, 0.48));
        brush.GradientStops.Add(new GradientStop(Darken(color, 0.28), 1));
        brush.Freeze();
        return RoundPen(brush, 2.3);
    }

    static Color Lighten(Color color, double amount)
    {
        byte Mix(byte channel) => (byte)Math.Round(channel + ((255 - channel) * amount));
        return Color.FromRgb(Mix(color.R), Mix(color.G), Mix(color.B));
    }

    static Color Darken(Color color, double amount)
    {
        byte Mix(byte channel) => (byte)Math.Round(channel * (1d - amount));
        return Color.FromRgb(Mix(color.R), Mix(color.G), Mix(color.B));
    }

    static Pen RoundPen(Color color, double thickness) => RoundPen(Freeze(color), thickness);

    static Pen RoundPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();
        return pen;
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
        if (samples[0].Stamp is not null)
        {
            DrawTimeAxis(drawingContext, samples, x, plotLeft, plotRight, plotTop, plotHeight, height);
            return;
        }

        var months = new List<int>();
        var previous = -1;
        for (var i = 0; i < samples.Count; i++)
        {
            var day = samples[i].Day;
            var key = day.Year * 12 + day.Month;
            if (key == previous)
                continue;
            previous = key;
            months.Add(i);
        }

        if (months.Count == 0)
            return;

        var maxLabels = Math.Max(2, (int)((plotRight - plotLeft) / 74));
        var step = 1;
        while (step < 12 && (int)Math.Ceiling(months.Count / (double)step) > maxLabels)
        {
            step = step switch
            {
                1 => 3,
                3 => 6,
                _ => 12
            };
        }

        var ticks = new List<int>();
        for (var i = 0; i < months.Count; i += step)
            ticks.Add(months[i]);
        if (ticks[^1] != months[^1])
            ticks.Add(months[^1]);

        double lastLabelRight = double.NegativeInfinity;
        for (var i = 0; i < ticks.Count; i++)
        {
            var index = ticks[i];
            var label = samples[index].Day.ToString("MMM yyyy", Culture);
            var text = Text(label, 11);
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

    void DrawTimeAxis(
        DrawingContext drawingContext,
        IReadOnlyList<ChartSample> samples,
        Func<int, double> x,
        double plotLeft,
        double plotRight,
        double plotTop,
        double plotHeight,
        double height)
    {
        var maxLabels = Math.Max(2, (int)((plotRight - plotLeft) / 64));
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
            var stamp = samples[index].Stamp ?? samples[index].Day.ToDateTime(TimeOnly.MinValue);
            var text = Text(stamp.ToString("HH:mm", Culture), 11);
            var at = x(index);
            var left = i == ticks.Count - 1 ? Math.Min(at, plotRight - text.Width) : at;
            left = Math.Max(plotLeft, left);
            if (i < ticks.Count - 1 && left < lastLabelRight + 8)
                continue;

            drawingContext.DrawLine(GridPen, new Point(at, plotTop), new Point(at, plotTop + plotHeight));
            drawingContext.DrawText(text, new Point(left, height - 20));
            lastLabelRight = left + text.Width;
        }
    }

    void DrawHover(
        DrawingContext drawingContext,
        DateTime mark,
        bool clock,
        double value,
        Point point,
        double plotLeft,
        double plotRight,
        double plotTop,
        double plotHeight,
        Brush stroke)
    {
        drawingContext.DrawLine(HoverPen, new Point(point.X, plotTop), new Point(point.X, plotTop + plotHeight));
        drawingContext.DrawLine(HoverPen, new Point(plotLeft, point.Y), new Point(plotRight, point.Y));
        drawingContext.DrawEllipse(DotFill, new Pen(stroke, 2), point, 4.5, 4.5);

        var dateText = Text(clock ? mark.ToString("HH:mm", Culture) : DateOnly.FromDateTime(mark).ToString("dd MMM yyyy", Culture), 11, HoverMutedBrush);
        var priceText = Text(HoverFormat(value), 13, HoverBrush, ValueTypeface);
        var boxWidth = Math.Max(dateText.Width, priceText.Width) + 16;
        var boxHeight = dateText.Height + priceText.Height + 10;
        var left = point.X + 14;
        if (left + boxWidth > plotRight)
            left = point.X - 14 - boxWidth;
        var top = point.Y - boxHeight - 12;
        if (top < plotTop)
            top = point.Y + 12;
        left = Math.Clamp(left, plotLeft, Math.Max(plotLeft, plotRight - boxWidth));
        top = Math.Clamp(top, plotTop, Math.Max(plotTop, plotTop + plotHeight - boxHeight));

        drawingContext.DrawRoundedRectangle(HoverFill, HoverBorderPen, new Rect(left, top, boxWidth, boxHeight), 6, 6);
        drawingContext.DrawText(dateText, new Point(left + 8, top + 4));
        drawingContext.DrawText(priceText, new Point(left + 8, top + 4 + dateText.Height));
    }

    string HoverFormat(double value)
    {
        if (string.Equals(ValueFormat, "0", StringComparison.Ordinal))
            return value.ToString("0.00", Culture);
        if (!string.Equals(ValueFormat, "auto", StringComparison.Ordinal))
            return value.ToString(ValueFormat, Culture);

        var abs = Math.Abs(value);
        if (abs >= 1)
            return value.ToString("N2", Culture);
        if (abs >= 0.01)
            return value.ToString("0.0000", Culture);
        return value.ToString("0.000000", Culture);
    }

    static int IndexAt(double x, double padLeft, double plotWidth, int last, IReadOnlyList<ChartSample> samples)
    {
        var guess = (int)Math.Round((x - padLeft) / plotWidth * last);
        return NearestFinite(samples, Math.Clamp(guess, 0, last));
    }

    static DateTime Mark(ChartSample sample) =>
        sample.Stamp ?? sample.Day.ToDateTime(TimeOnly.MinValue);

    static int IndexOf(IReadOnlyList<ChartSample> samples, DateTime mark)
    {
        var low = 0;
        var high = samples.Count - 1;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            var compare = Mark(samples[mid]).CompareTo(mark);
            if (compare == 0)
                return NearestFinite(samples, mid);
            if (compare < 0)
                low = mid + 1;
            else
                high = mid - 1;
        }

        var after = Math.Clamp(low, 0, samples.Count - 1);
        var before = Math.Clamp(high, 0, samples.Count - 1);
        var choice = (mark - Mark(samples[before])).Duration() <= (Mark(samples[after]) - mark).Duration()
            ? before
            : after;
        return NearestFinite(samples, choice);
    }

    static int NearestFinite(IReadOnlyList<ChartSample> samples, int guess)
    {
        if (samples[guess].Value is double value && double.IsFinite(value))
            return guess;

        for (var distance = 1; distance < samples.Count; distance++)
        {
            var right = guess + distance;
            if (right < samples.Count && samples[right].Value is double rightValue && double.IsFinite(rightValue))
                return right;
            var left = guess - distance;
            if (left >= 0 && samples[left].Value is double leftValue && double.IsFinite(leftValue))
                return left;
        }

        return -1;
    }

    string Format(double value, double step)
    {
        if (!string.Equals(ValueFormat, "auto", StringComparison.Ordinal))
            return value.ToString(ValueFormat, Culture);

        var decimals = 0;
        var probe = Math.Abs(step);
        while (decimals < 6 && Math.Abs(probe - Math.Round(probe)) > 1e-6)
        {
            probe *= 10;
            decimals++;
        }

        return value.ToString("N" + decimals, Culture);
    }

    static List<double> PriceTicks(double min, double max, double plotHeight)
    {
        var span = max - min;
        var target = Math.Clamp((int)Math.Floor(plotHeight / 32d), 5, 8);
        var step = NiceStep(span / target);
        while (span / step > plotHeight / 24d && step < span)
            step = CoarserStep(step);

        var first = Math.Ceiling(min / step - 1e-8) * step;
        var values = new List<double>();
        for (var i = 0; i < 24; i++)
        {
            var value = first + i * step;
            if (value > max + step * 1e-6)
                break;
            if (value >= min - step * 1e-8)
                values.Add(value);
        }

        if (values.Count >= 2)
            return values;

        return [min, (min + max) / 2, max];
    }

    static double StepOf(List<double> ticks) =>
        ticks.Count < 2 ? 1 : ticks[1] - ticks[0];

    static double NiceStep(double raw)
    {
        if (raw <= 0 || !double.IsFinite(raw))
            return 1;

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var fraction = raw / magnitude;
        var nice = fraction <= 1 ? 1d
            : fraction <= 2 ? 2d
            : fraction <= 2.5 ? 2.5d
            : fraction <= 5 ? 5d
            : 10d;
        return nice * magnitude;
    }

    static double CoarserStep(double step)
    {
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(step) + 1e-9));
        var fraction = step / magnitude;
        if (fraction < 1.5)
            return 2 * magnitude;
        if (fraction < 2.25)
            return 2.5 * magnitude;
        if (fraction < 3.5)
            return 5 * magnitude;
        if (fraction < 7.5)
            return 10 * magnitude;
        return 20 * magnitude;
    }

    FormattedText Text(string text, double size, Brush? brush = null, Typeface? typeface = null) =>
        new(text, Culture, FlowDirection.LeftToRight, typeface ?? Typeface, size, brush ?? LabelBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    static Pen FreezePen(Color color) => FreezePen(Freeze(color));

    static Pen FreezePen(Brush brush)
    {
        var pen = new Pen(brush, 1);
        pen.Freeze();
        return pen;
    }

    static Pen CreateDashPen(Color color)
    {
        var pen = new Pen(Freeze(color), 1) { DashStyle = DashStyles.Dash };
        pen.Freeze();
        return pen;
    }
}
