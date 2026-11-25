using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SimTrade.Services;

namespace SimTrade.Controls;

public partial class AccountIconBadge : UserControl
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon),
        typeof(string),
        typeof(AccountIconBadge),
        new PropertyMetadata("", OnIconChanged));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size),
        typeof(double),
        typeof(AccountIconBadge),
        new PropertyMetadata(32d, OnSizeChanged));

    private int _request;

    public AccountIconBadge()
    {
        InitializeComponent();
    }

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    private static void OnIconChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is AccountIconBadge badge)
            badge.Load(args.NewValue as string);
    }

    private static void OnSizeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is AccountIconBadge badge && args.NewValue is double size && size > 0)
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
        EmojiText.FontSize = Math.Max(12, size * 0.5);
        IconImage.Margin = new Thickness(Math.Max(2, size * 0.12));
    }

    private async void Load(string? icon)
    {
        var request = ++_request;
        var normalized = AccountIconCache.Normalize(icon);
        var fallback = string.IsNullOrEmpty(normalized) ? "📁" : normalized;
        if (!AccountIconCache.IsUrl(fallback))
            EmojiText.Text = fallback;
        else
            EmojiText.Text = "🖼️";

        Photo.Visibility = Visibility.Collapsed;
        IconImage.Source = null;

        if (string.IsNullOrEmpty(normalized))
            return;

        byte[]? bytes;
        try
        {
            bytes = await AccountIconCache.GetAsync(normalized);
        }
        catch (Exception)
        {
            bytes = null;
        }

        if (request != _request)
            return;

        if (bytes is null)
            return;

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
            IconImage.Source = image;
            Photo.Visibility = Visibility.Visible;
        }
        catch (Exception)
        {
            IconImage.Source = null;
            Photo.Visibility = Visibility.Collapsed;
        }
    }
}
