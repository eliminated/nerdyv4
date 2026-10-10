using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Nerdy.App.Views.Dialogs;

/// <summary>Keyboard shortcuts, a few tips, and the app version.</summary>
public sealed class HelpDialog : ContentDialog
{
    private static readonly (string Keys, string Action)[] Shortcuts =
    [
        ("Ctrl + K", "Search notes, topics, reminders"),
        ("Ctrl + B", "Collapse or expand the sidebar"),
        ("Ctrl + ,", "Open settings"),
        ("Ctrl + N", "New item on the current page"),
        ("Enter", "Add a reminder from the quick-add box")
    ];

    public HelpDialog()
    {
        Title = "Help & feedback";
        CloseButtonText = "Close";
        DefaultButton = ContentDialogButton.Close;

        var panel = new StackPanel { Spacing = 14, MinWidth = 380 };
        panel.Children.Add(Header("Keyboard shortcuts"));
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < Shortcuts.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var keys = new Border { Style = (Style)Application.Current.Resources["NerdyKbdStyle"], Child = new TextBlock { Text = Shortcuts[i].Keys, FontSize = 12 } };
            var text = new TextBlock { Text = Shortcuts[i].Action, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(keys, i);
            Grid.SetRow(text, i);
            Grid.SetColumn(text, 1);
            grid.Children.Add(keys);
            grid.Children.Add(text);
        }
        panel.Children.Add(grid);

        panel.Children.Add(Header("Focus sessions"));
        panel.Children.Add(Body("Focus hides or closes apps you haven't allowed (choose which in Settings). Ultra-Focus stays full screen until the timer ends; " +
                                "the emergency exit asks for a reason so you can look back on it in Statistics & History. Task Manager (Ctrl+Shift+Esc) always works."));

        panel.Children.Add(Header("Feedback"));
        panel.Children.Add(Body("Found a bug or have an idea? Your app log is in %LOCALAPPDATA%\\Nerdy\\logs — attach it when you report a problem."));

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        panel.Children.Add(new TextBlock { Text = $"Nerdy {version?.ToString(3)}", Style = (Style)Application.Current.Resources["NerdyCaptionStyle"] });
        Content = panel;
    }

    private static TextBlock Header(string text) => new() { Text = text, Style = (Style)Application.Current.Resources["NerdySectionLabelStyle"] };

    private static TextBlock Body(string text) => new() { Text = text, Style = (Style)Application.Current.Resources["NerdySecondaryWrapStyle"] };
}
