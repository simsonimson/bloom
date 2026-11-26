using Bloom.App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Velopack;

namespace Bloom.App;

/// <summary>
/// Custom entry point (DISABLE_XAML_GENERATED_MAIN):
/// 1. Velopack install/update hooks must run before anything else.
/// 2. Single instance: a second start forwards its activation to the running instance and exits.
/// </summary>
public static class Program
{
    private const string InstanceKey = "bloom-main";

    [STAThread]
    private static int Main(string[] args)
    {
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => UninstallCleanup.Run())
            .Run();

        WinRT.ComWrappersSupport.InitializeComWrappers();

        var instance = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!instance.IsCurrent)
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            // Redirect on a worker thread; blocking the STA thread on the async call can deadlock.
            Task.Run(() => instance.RedirectActivationToAsync(activation).AsTask()).Wait(TimeSpan.FromSeconds(5));
            return 0;
        }

        Application.Start(_p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App(args);
        });
        return 0;
    }
}
