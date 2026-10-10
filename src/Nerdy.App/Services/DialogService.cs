using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Nerdy.App.Services;

/// <summary>Shows ContentDialogs on the main window (only one can be open at a time in WinUI).</summary>
public sealed class DialogService
{
    private XamlRoot? _root;
    private ContentDialog? _open;

    public void Attach(XamlRoot root) => _root = root;

    public async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        if (_root is null) return ContentDialogResult.None;
        _open?.Hide();
        dialog.XamlRoot = _root;
        dialog.Style ??= (Style)Application.Current.Resources["DefaultContentDialogStyle"];
        _open = dialog;
        try { return await dialog.ShowAsync(); }
        finally { if (_open == dialog) _open = null; }
    }

    /// <summary>Closes the open dialog, if any (e.g. before a scheduled focus session takes over the window).</summary>
    public async Task CloseOpenAsync()
    {
        if (_open is not { } open) return;
        var closed = new TaskCompletionSource();
        open.Closed += (_, _) => closed.TrySetResult();
        open.Hide();
        await Task.WhenAny(closed.Task, Task.Delay(1000));
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirm, bool destructive = false, string cancel = "Cancel")
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = confirm,
            CloseButtonText = cancel,
            DefaultButton = destructive ? ContentDialogButton.Close : ContentDialogButton.Primary
        };
        if (destructive) dialog.PrimaryButtonStyle = (Style)Application.Current.Resources["NerdyDangerButtonStyle"];
        return await ShowAsync(dialog) == ContentDialogResult.Primary;
    }
}
