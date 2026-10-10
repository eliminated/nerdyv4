using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Nerdy.App.ViewModels;
using Nerdy.App.ViewModels.Items;
using Windows.System;

namespace Nerdy.App.Views;

public sealed partial class RemindersPage : Page
{
    public RemindersPage()
    {
        ViewModel = App.GetService<RemindersViewModel>();
        InitializeComponent();
        var add = new KeyboardAccelerator { Modifiers = VirtualKeyModifiers.Control, Key = VirtualKey.N };
        add.Invoked += (_, e) => { QuickAdd.Focus(FocusState.Keyboard); e.Handled = true; };
        KeyboardAccelerators.Add(add);
    }

    public RemindersViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e) => ViewModel.OnNavigatedTo(e.Parameter);

    protected override void OnNavigatedFrom(NavigationEventArgs e) => ViewModel.OnNavigatedFrom();

    private void OnQuickAddKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        ViewModel.AddCommand.Execute(null);
    }

    private async void OnEdit(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ReminderItemViewModel item) await ViewModel.EditAsync(item);
    }
}
