using System.Windows;
using System.Windows.Controls;
using SimTrade.Models;

namespace SimTrade.Controls;

public partial class RangeTrack : UserControl
{
    public RangeTrack()
    {
        InitializeComponent();
    }

    private void OnDataChanged(object sender, DependencyPropertyChangedEventArgs e) => Place();

    private void OnLayoutChanged(object sender, SizeChangedEventArgs e) => Place();

    private void Place()
    {
        if (DataContext is not PriceRangeMark mark || Bar.ActualWidth <= 16)
            return;

        var span = Math.Max(0, Bar.ActualWidth - 16);
        var x = 8 + span * Math.Clamp(mark.Marker, 0, 1);
        Dot.Visibility = mark.HasMarker ? Visibility.Visible : Visibility.Collapsed;
        Dot.Margin = new Thickness(Math.Max(0, x - 7), 0, 0, 0);

        Native.Text = mark.CurrentNativeText;
        Mxn.Text = mark.CurrentMxnText;
        Label.Visibility = mark.HasMarker ? Visibility.Visible : Visibility.Collapsed;
        Label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = Label.DesiredSize.Width;
        var host = LabelHost.ActualWidth > 0 ? LabelHost.ActualWidth : Bar.ActualWidth;
        var left = Math.Clamp(x - width / 2, 0, Math.Max(0, host - width));
        Canvas.SetLeft(Label, left);
        Canvas.SetTop(Label, 2);
    }
}
