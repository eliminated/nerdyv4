using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Nerdy.App.Services;
using Nerdy.App.Services.Api;
using Nerdy.App.Services.Auth;
using Nerdy.App.Services.Classes;
using Nerdy.App.Services.Data;
using Nerdy.App.Services.Sync;
using Nerdy.App.Services.Theming;
using Nerdy.App.ViewModels;
using Nerdy.Core.Time;

namespace Nerdy.App;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        InitializeComponent();

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.Logging.AddDebug();
        builder.Logging.AddProvider(new FileLoggerProvider(AppPaths.LogsDirectory));
        ConfigureServices(builder.Services);
        _host = builder.Build();
        Services = _host.Services;

        UnhandledException += (_, e) =>
        {
            GetService<ILogger<App>>().LogError(e.Exception, "Unhandled UI exception");
            e.Handled = true;
            GetService<ToastService>().Show("Something went wrong. It has been logged.", ToastKind.Error);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            GetService<ILogger<App>>().LogError(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };
    }

    public static IServiceProvider Services { get; private set; } = null!;
    public static MainWindow MainWindow { get; private set; } = null!;

    public static T GetService<T>() where T : notnull => Services.GetRequiredService<T>();

    private static void ConfigureServices(IServiceCollection s)
    {
        s.AddSingleton(_ => AppConfig.Load());
        s.AddSingleton<IClock, SystemClock>();
        s.AddSingleton<ApiClient>();
        s.AddSingleton<TokenStore>();
        s.AddSingleton<AuthService>();
        s.AddSingleton<DataService>();
        s.AddSingleton<SyncService>();
        s.AddSingleton<ClassesService>();
        s.AddSingleton<ThemeService>();
        s.AddSingleton<PreferencesService>();
        s.AddSingleton<NavigationService>();
        s.AddSingleton<ToastService>();
        s.AddSingleton<DialogService>();
        s.AddSingleton<SessionController>();
        s.AddSingleton<SearchService>();
        s.AddSingleton<AgendaService>();
        s.AddSingleton<Services.Focus.FocusSessionService>();
        s.AddSingleton<Services.Notifications.NotificationService>();
        s.AddSingleton<Services.Notifications.ReminderScheduler>();
        s.AddSingleton<Services.Shell.AppLifetime>();
        s.AddSingleton<Services.Updates.UpdateService>();

        s.AddSingleton<ShellViewModel>();
        s.AddTransient<AuthViewModel>();
        s.AddTransient<OnboardingViewModel>();
        s.AddTransient<HomeViewModel>();
        s.AddTransient<CalendarViewModel>();
        s.AddTransient<ScheduleViewModel>();
        s.AddTransient<RemindersViewModel>();
        s.AddTransient<SubjectsViewModel>();
        s.AddTransient<NotesViewModel>();
        s.AddTransient<FocusSetupViewModel>();
        s.AddTransient<FocusSessionViewModel>();
        s.AddTransient<StatsViewModel>();
        s.AddTransient<SettingsViewModel>();
        s.AddTransient<ClassesViewModel>();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var navigation = GetService<NavigationService>();
        navigation.RegisterRoot<Views.AuthPage>(RootView.Auth);
        navigation.RegisterRoot<Views.ShellPage>(RootView.Shell);
        navigation.RegisterRoot<Views.OnboardingPage>(RootView.Onboarding);
        navigation.RegisterSection<Views.HomePage>(Models.NavSections.Home);
        navigation.RegisterSection<Views.CalendarPage>(Models.NavSections.Calendar);
        navigation.RegisterSection<Views.SchedulePage>(Models.NavSections.Schedule);
        navigation.RegisterSection<Views.RemindersPage>(Models.NavSections.Reminders);
        navigation.RegisterSection<Views.SubjectsPage>(Models.NavSections.Subjects);
        navigation.RegisterSection<Views.NotesPage>(Models.NavSections.Notes);
        navigation.RegisterSection<Views.FocusSetupPage>(Models.NavSections.Focus);
        navigation.RegisterSection<Views.StatsPage>(Models.NavSections.Stats);
        navigation.RegisterSection<Views.SettingsPage>(Models.NavSections.Settings);
        navigation.RegisterSection<Views.ClassesPage>(Models.NavSections.Classes);
        navigation.RegisterRoot<Views.FocusSessionPage>(RootView.Focus);
        navigation.RegisterRoot<Views.UltraFocusPage>(RootView.UltraFocus);

        GetService<Services.Notifications.NotificationService>().Initialize();
        _ = GetService<Services.Notifications.ReminderScheduler>(); // listens for sign-in and notification clicks

        MainWindow = new MainWindow();
        var lifetime = GetService<Services.Shell.AppLifetime>();
        lifetime.Attach(MainWindow);
        MainWindow.Activate();

        // A second launch is redirected here by Program.Main: bring the window back (e.g. from the notification area).
        Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().Activated += (_, _) =>
            MainWindow.DispatcherQueue.TryEnqueue(lifetime.ShowMainWindow);

        _ = GetService<Services.Updates.UpdateService>().RunAsync();
        await GetService<SessionController>().StartAsync();
    }
}
