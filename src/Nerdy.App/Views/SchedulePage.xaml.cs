using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Nerdy.App.Controls;
using Nerdy.App.Services;
using Nerdy.App.ViewModels;

namespace Nerdy.App.Views;

public sealed partial class SchedulePage : Page
{
    public SchedulePage()
    {
        ViewModel = App.GetService<ScheduleViewModel>();
        InitializeComponent();
        ViewModel.ConflictsNeedAttention += OnConflictsNeedAttention;
    }

    public ScheduleViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e) => ViewModel.OnNavigatedTo(e.Parameter);

    protected override void OnNavigatedFrom(NavigationEventArgs e) => ViewModel.OnNavigatedFrom();

    /// <summary>The preview shows only the study day (panel templates can't bind to the page, so set it here).</summary>
    private void OnPreviewPanelLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TimeGridPanel panel) return;
        panel.FirstHour = ViewModel.PreviewFirstHour;
        panel.Hours = ViewModel.PreviewHourCount;
    }

    private async void OnConflictsNeedAttention(object? sender, EventArgs e)
    {
        var result = await App.GetService<DialogService>().ShowAsync(ConflictDialog);
        if (result == ContentDialogResult.Primary) ViewModel.UseSuggestionsAndAdd();
        else if (result == ContentDialogResult.Secondary) ViewModel.SkipConflictsAndAdd();
    }
}
