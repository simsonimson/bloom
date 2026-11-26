using System.Net.Http.Headers;
using Bloom.Activity;
using Bloom.Core;
using Bloom.Core.Events;
using Bloom.Core.Roblox;
using Bloom.Core.Settings;
using Bloom.Data;
using Bloom.Launcher.Bootstrapper;
using Bloom.Launcher.FastFlags;
using Bloom.Launcher.Installation;
using Bloom.Voice;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Bloom.App.Services;

/// <summary>Composition root. Feature modules register their services here.</summary>
public static class AppServices
{
    public const string ApiClient = "api";
    public const string DownloadClient = "downloads";

    public static IServiceProvider Build(BloomPaths paths)
    {
        paths.EnsureCreated();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProperty("Version", typeof(AppServices).Assembly.GetName().Version?.ToString() ?? "?")
            .WriteTo.File(
                Path.Combine(paths.Logs, "bloom-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(b => b.ClearProviders().AddSerilog(dispose: true));
        services.AddSingleton(paths);

        // Data
        services.AddSingleton(_ =>
        {
            var db = new BloomDatabase(paths.Database);
            db.Migrate();
            return db;
        });
        services.AddSingleton<SqliteSettingsStore>();
        services.AddSingleton<ISettingsStore>(sp => sp.GetRequiredService<SqliteSettingsStore>());
        services.AddSingleton<IExperienceCache, ExperienceCacheStore>();
        services.AddSingleton<ISessionStore, SessionStore>();
        services.AddSingleton<IFavoritesStore, FavoritesStore>();

        // Core
        services.AddSingleton<IEventBus>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<EventBus>>();
            return new EventBus((ex, type) => logger.LogError(ex, "Event handler for {Event} failed", type.Name));
        });
        services.AddSingleton<AppSettings>();
        services.AddKeyedSingleton(ApiClient, (_, _) => CreateHttpClient(TimeSpan.FromSeconds(30)));
        services.AddKeyedSingleton(DownloadClient, (_, _) => CreateHttpClient(Timeout.InfiniteTimeSpan));

        // Launcher
        services.AddSingleton(sp => new RobloxBootstrapper(
            sp.GetRequiredKeyedService<HttpClient>(DownloadClient), paths.Roblox, LogTo<RobloxBootstrapper>(sp)));
        services.AddSingleton(sp => new FastFlagService(
            sp.GetRequiredKeyedService<HttpClient>(ApiClient), paths.Data, LogTo<FastFlagService>(sp)));
        services.AddSingleton<LaunchCoordinator>();

        // Activity
        services.AddSingleton(sp => new ProcessWatcher(sp.GetRequiredService<IEventBus>(), LogTo<ProcessWatcher>(sp)));
        services.AddSingleton(sp => new LogWatcher(sp.GetRequiredService<IEventBus>(), RobloxInstallation.OfficialLogs, LogTo<LogWatcher>(sp)));
        services.AddSingleton(sp => new ExperienceResolver(
            sp.GetRequiredKeyedService<HttpClient>(ApiClient), sp.GetRequiredService<IExperienceCache>(), LogTo<ExperienceResolver>(sp)));
        services.AddSingleton(sp => new ActivityTracker(
            sp.GetRequiredService<IEventBus>(), sp.GetRequiredService<ISessionStore>(),
            sp.GetRequiredService<ExperienceResolver>(), LogTo<ActivityTracker>(sp)));

        // Voice
        services.AddSingleton(_ => new JournalStore(paths.AudioJournal));
        services.AddSingleton(sp => new VoiceService(
            sp.GetRequiredService<IEventBus>(), sp.GetRequiredService<JournalStore>(), LogTo<VoiceService>(sp)));
        services.AddSingleton<VoiceCoordinator>();

        // App
        services.AddSingleton<UpdateService>();
        services.AddSingleton<BackupService>();
        return services.BuildServiceProvider();
    }

    /// <summary>Starts background services after the UI exists.</summary>
    public static void StartBackground(IServiceProvider services)
    {
        var settings = services.GetRequiredService<AppSettings>();
        services.GetRequiredService<ActivityTracker>(); // subscribes before the first process event
        var logWatcher = services.GetRequiredService<LogWatcher>();
        logWatcher.Enabled = settings.LogDetectionEnabled;
        services.GetRequiredService<IEventBus>().Subscribe<SettingChanged>(e =>
        {
            if (e.Key == AppSettings.Keys.LogDetectionEnabled) logWatcher.Enabled = settings.LogDetectionEnabled;
        });
        var processes = services.GetRequiredService<ProcessWatcher>();
        // Voice subscribes to process events before the watcher reports the first Roblox process.
        services.GetRequiredService<VoiceCoordinator>().Start(processes.RunningProcessIds);
        processes.Start();
        services.GetRequiredService<LaunchCoordinator>().EnsureProtocolRegistration();
    }

    private static HttpClient CreateHttpClient(TimeSpan timeout)
    {
        var client = new HttpClient { Timeout = timeout };
        var version = typeof(AppServices).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("bloom", version));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/simsonimson/bloom)"));
        return client;
    }

    private static Action<string, Exception?> LogTo<T>(IServiceProvider sp)
    {
        var logger = sp.GetRequiredService<ILogger<T>>();
        return (message, ex) =>
        {
            if (ex is null) logger.LogInformation("{Message}", message);
            else logger.LogWarning(ex, "{Message}", message);
        };
    }
}
