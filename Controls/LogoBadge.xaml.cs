using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SimTrade.Services;

namespace SimTrade.Controls;

public partial class LogoBadge : UserControl
{
    public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(
        nameof(Symbol),
        typeof(string),
        typeof(LogoBadge),
        new PropertyMetadata(null, OnSymbolChanged));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size),
        typeof(double),
        typeof(LogoBadge),
        new PropertyMetadata(32d, OnSizeChanged));

    private int _request;

    public LogoBadge()
    {
        InitializeComponent();
    }

    public string? Symbol
    {
        get => (string?)GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    private static void OnSymbolChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is LogoBadge badge)
            badge.Load(args.NewValue as string);
    }

    private static void OnSizeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is LogoBadge badge && args.NewValue is double size && size > 0)
            badge.ApplySize(size);
    }

    private void ApplySize(double size)
    {
        Width = size;
        Height = size;
        var radius = Math.Max(6, size * 0.25);
        Fallback.CornerRadius = new CornerRadius(radius);
        Photo.CornerRadius = new CornerRadius(radius);
        Photo.Clip = new RectangleGeometry(new Rect(0, 0, size, size), radius, radius);
        Initial.FontSize = Math.Max(11, size * 0.38);
        Logo.Margin = new Thickness(Math.Max(1, size * 0.06));
    }

    private static string InitialOf(string? symbol)
    {
        if (string.IsNullOrEmpty(symbol))
            return "";

        foreach (var character in symbol)
        {
            if (char.IsLetterOrDigit(character))
                return char.ToUpperInvariant(character).ToString();
        }

        return symbol[0].ToString();
    }

    private async void Load(string? symbol)
    {
        var request = ++_request;
        var normalized = string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim().ToUpperInvariant();
        Initial.Text = InitialOf(normalized);

        if (normalized is null)
        {
            Logo.Source = null;
            Photo.Visibility = Visibility.Collapsed;
            return;
        }

        byte[]? bytes;
        try
        {
            bytes = await CompanyLogoCache.GetAsync(normalized);
        }
        catch (Exception)
        {
            bytes = null;
        }

        if (request != _request)
            return;

        if (bytes is null)
        {
            Logo.Source = null;
            Photo.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.DecodePixelWidth = Math.Max(64, (int)(Size * 2));
            image.EndInit();
            image.Freeze();
            Logo.Source = image;
            Photo.Visibility = Visibility.Visible;
        }
        catch (Exception)
        {
            Logo.Source = null;
            Photo.Visibility = Visibility.Collapsed;
        }
    }
}
