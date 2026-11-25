using System.Windows;

namespace SimTrade;

public partial class JournalNoteWindow : Window
{
    private JournalNoteWindow(string symbol, string side, string when, string notes)
    {
        InitializeComponent();
        SubtitleText.Text = side + " · " + symbol + " · " + when;
        NoteBox.Text = notes ?? "";
        NoteBox.CaretIndex = NoteBox.Text.Length;
        Loaded += (_, _) => NoteBox.Focus();
    }

    public static string? Prompt(string symbol, string side, string when, string notes)
    {
        var owner = Application.Current?.MainWindow;
        var window = new JournalNoteWindow(symbol, side, when, notes);
        if (owner is { IsLoaded: true })
            window.Owner = owner;
        return window.ShowDialog() == true ? window.NoteBox.Text : null;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
