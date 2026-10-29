using Microsoft.EntityFrameworkCore;
using Talkie.Api.Infrastructure;
using Talkie.Data;
using Talkie.Shared;

namespace Talkie.Api.Modules;

/// <summary>
/// AFK mode: while AFK a user earns shards every minute (1 per minute by default, times their booster).
/// Earnings are calculated on the server from timestamps, so AFK keeps counting even when the browser is closed,
/// up to the configured maximum number of hours.
/// </summary>
public sealed class AfkService(TalkieDbContext db, TimeProvider clock)
{
    public async Task CreditAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.AfkSince, u.AfkLastCreditedAt, u.ShardMultiplier })
            .FirstOrDefaultAsync(ct);
        if (user?.AfkSince is null || user.AfkLastCreditedAt is null) return;

        var settings = await db.AppSettings.AsNoTracking().FirstAsync(ct);
        var shards = CalculateShards(user.AfkSince.Value, user.AfkLastCreditedAt.Value, clock.GetUtcNow(),
            settings.AfkShardsPerMinute, user.ShardMultiplier, settings.AfkMaxHours, out var minutes);
        if (minutes <= 0) return;

        var last = user.AfkLastCreditedAt.Value;
        var newLast = last.AddMinutes(minutes);
        // Conditional update: two concurrent polls cannot credit the same minutes twice.
        await db.Users
            .Where(u => u.Id == userId && u.AfkLastCreditedAt == last)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Shards, u => u.Shards + shards)
                .SetProperty(u => u.AfkShardsThisSession, u => u.AfkShardsThisSession + shards)
                .SetProperty(u => u.AfkLastCreditedAt, newLast), ct);
    }

    /// <summary>Whole minutes elapsed since the last credit (capped at the AFK limit) times the rate and multiplier.</summary>
    public static long CalculateShards(
        DateTimeOffset since, DateTimeOffset lastCredited, DateTimeOffset now,
        int shardsPerMinute, int multiplier, int maxHours, out long minutes)
    {
        var end = maxHours > 0 ? Min(now, since.AddHours(maxHours)) : now;
        minutes = end <= lastCredited ? 0 : (long)Math.Floor((end - lastCredited).TotalMinutes);
        return minutes * shardsPerMinute * Math.Max(1, multiplier);
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    public async Task<AfkStatusDto> GetStatusAsync(Guid userId, CancellationToken ct)
    {
        var u = await db.Users.AsNoTracking().FirstAsync(x => x.Id == userId, ct);
        var settings = await db.AppSettings.AsNoTracking().FirstAsync(ct);
        return new AfkStatusDto(u.AfkSince is not null, u.AfkSince, u.AfkShardsThisSession, u.ShardMultiplier, settings.AfkShardsPerMinute, u.Shards);
    }

    public async Task StartAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await db.Users.Where(u => u.Id == userId && u.AfkSince == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.AfkSince, now)
                .SetProperty(u => u.AfkLastCreditedAt, now)
                .SetProperty(u => u.AfkShardsThisSession, 0L), ct);
    }

    public async Task StopAsync(Guid userId, CancellationToken ct)
    {
        await CreditAsync(userId, ct);
        await db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.AfkSince, (DateTimeOffset?)null)
                .SetProperty(u => u.AfkLastCreditedAt, (DateTimeOffset?)null), ct);
    }
}

public sealed class AfkModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration) => services.AddScoped<AfkService>();

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/afk").WithTags("AFK").RequireTalkieUser();

        g.MapGet("/", async (CurrentUser current, AfkService afk, CancellationToken ct) =>
        {
            await afk.CreditAsync(current.Id, ct);
            return await afk.GetStatusAsync(current.Id, ct);
        });

        g.MapPost("/start", async (CurrentUser current, AfkService afk, CancellationToken ct) =>
        {
            await afk.StartAsync(current.Id, ct);
            return await afk.GetStatusAsync(current.Id, ct);
        });

        g.MapPost("/stop", async (CurrentUser current, AfkService afk, CancellationToken ct) =>
        {
            await afk.StopAsync(current.Id, ct);
            // ShardsEarnedThisSession still holds what the session that just ended earned.
            return await afk.GetStatusAsync(current.Id, ct);
        });
    }
}
