using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Nerdy.App.ViewModels;

namespace Nerdy.App.Views;

public sealed partial class CalendarPage : Page
{
    public CalendarPage()
    {
        ViewModel = App.GetService<CalendarViewModel>();
        InitializeComponent();
        ViewModel.RangeChanged += (_, _) => ScrollToStudyDay();
        Loaded += (_, _) => ScrollToStudyDay();
    }

    public CalendarViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e) => ViewModel.OnNavigatedTo(e.Parameter);

    protected override void OnNavigatedFrom(NavigationEventArgs e) => ViewModel.OnNavigatedFrom();

    private void ScrollToStudyDay() =>
        DispatcherQueue.TryEnqueue(() => TimeScroll.ChangeView(null, ViewModel.InitialScroll, null, disableAnimation: true));

    /// <summary>Double-clicking an empty spot in a day column creates an event at that time.</summary>
    private async void OnColumnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DayColumnViewModel column } element) return;
        var hour = e.GetPosition(element).Y / CalendarViewModel.HourHeight;
        e.Handled = true;
        await ViewModel.NewEventAtAsync(column.Date, hour);
    }
}
