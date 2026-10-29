using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Talkie.Data;

namespace Talkie.MigrationService;

/// <summary>
/// Applies EF Core migrations and seeds the built-in data, then stops.
/// The Aspire AppHost starts the API only after this worker has completed successfully.
/// </summary>
public sealed class MigrationWorker(
    IServiceProvider services,
    IConfiguration configuration,
    IHostApplicationLifetime lifetime,
    ILogger<MigrationWorker> logger) : BackgroundService
{
    public const string ActivitySourceName = "Talkie.Migrations";
    private static readonly ActivitySource Source = new(ActivitySourceName);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var activity = Source.StartActivity("Migrating database", ActivityKind.Client);
        try
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TalkieDbContext>();

            // SQL Server may still be starting; retry the migration a few times.
            var strategy = new RetryLoop(logger);
            await strategy.RunAsync(() => db.Database.MigrateAsync(stoppingToken), stoppingToken);

            await TalkieSeeder.SeedAsync(db, new SeedOptions(
                OwnerPassword: configuration["Seed:OwnerPassword"] ?? "fwkh837h@m1",
                AdminPassword: configuration["Seed:AdminPassword"] ?? "TalkieAdmin1!"), stoppingToken);

            logger.LogInformation("Database migrated and seeded");
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            logger.LogError(ex, "Database migration failed");
            Environment.ExitCode = 1;
        }

        lifetime.StopApplication();
    }

    private sealed class RetryLoop(ILogger logger)
    {
        public async Task RunAsync(Func<Task> action, CancellationToken ct)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await action();
                    return;
                }
                catch (Exception ex) when (attempt < 10 && ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
                {
                    logger.LogWarning("Database not ready yet (attempt {Attempt}): {Message}", attempt, ex.Message);
                    await Task.Delay(TimeSpan.FromSeconds(3), ct);
                }
            }
        }
    }
}
