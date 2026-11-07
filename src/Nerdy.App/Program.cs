using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Velopack;

namespace Nerdy.App;

public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // First, before anything else: the installer runs Nerdy with special arguments during install, update and
        // uninstall, and Velopack handles those and exits. It also applies an update that finished downloading.
        VelopackApp.Build().Run();

        WinRT.ComWrappersSupport.InitializeComWrappers();

        // Single instance: a second launch is redirected to the running app, which shows its window. A test build with its
        // own data folder (NERDY_DATA_DIR) is an instance of its own.
        var key = Services.AppPaths.IsSeparate
            ? "Nerdy.Main." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Services.AppPaths.Root.ToLowerInvariant())))[..12]
            : "Nerdy.Main";
        var mainInstance = AppInstance.FindOrRegisterForKey(key);
        if (!mainInstance.IsCurrent)
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            mainInstance.RedirectActivationToAsync(activation).AsTask().GetAwaiter().GetResult();
            return 0;
        }

        Application.Start(static p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }
}
