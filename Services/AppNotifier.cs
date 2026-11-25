namespace SimTrade.Services;

public static class AppNotifier
{
    private static NoticeWindow? _window;

    public static void Show(string body)
    {
        var text = body.Trim();
        if (text.Length == 0)
            return;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        if (!dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => Show(text));
            return;
        }

        if (_window is null)
        {
            _window = new NoticeWindow();
            _window.Closed += (_, _) => _window = null;
        }

        _window.Present(text);
    }
}
