using Bloom.Core;
using Bloom.Core.Events;
using Bloom.Core.Settings;
using Bloom.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Bloom.App.Services;

/// <summary>Composition root. Feature modules register their services here from Phase 2 on.</summary>
public static class AppServices
{
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
        services.AddSingleton(_ =>
        {
            var db = new BloomDatabase(paths.Database);
            db.Migrate();
            return db;
        });
        services.AddSingleton<ISettingsStore, SqliteSettingsStore>();
        services.AddSingleton<IEventBus>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<EventBus>>();
            return new EventBus((ex, type) => logger.LogError(ex, "Event handler for {Event} failed", type.Name));
        });
        services.AddSingleton<AppSettings>();
        services.AddSingleton<UpdateService>();
        return services.BuildServiceProvider();
    }
}
