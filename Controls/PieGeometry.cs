using System.Windows;
using System.Windows.Media;

namespace SimTrade.Controls;

public static class PieGeometry
{
    public static Geometry Slice(double startDegrees, double sweepDegrees, double radius)
    {
        var center = new Point(radius, radius);
        var drawingRadius = Math.Max(1, radius - 0.5);
        if (sweepDegrees >= 359.99)
        {
            var ellipse = new EllipseGeometry(center, drawingRadius, drawingRadius);
            ellipse.Freeze();
            return ellipse;
        }

        var figure = new PathFigure
        {
            StartPoint = center,
            IsClosed = true,
            IsFilled = true
        };
        figure.Segments.Add(new LineSegment(OnCircle(center, drawingRadius, startDegrees), true));
        figure.Segments.Add(new ArcSegment
        {
            Point = OnCircle(center, drawingRadius, startDegrees + sweepDegrees),
            Size = new Size(drawingRadius, drawingRadius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = sweepDegrees > 180
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private static Point OnCircle(Point center, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180d;
        return new Point(
            center.X + (radius * Math.Cos(radians)),
            center.Y + (radius * Math.Sin(radians)));
    }
}
