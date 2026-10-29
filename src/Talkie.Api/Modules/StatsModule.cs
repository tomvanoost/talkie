using Microsoft.EntityFrameworkCore;
using Talkie.Api.Infrastructure;
using Talkie.Data;
using Talkie.Shared;

namespace Talkie.Api.Modules;

/// <summary>Personal stats and the company leaderboard.</summary>
public sealed class StatsModule : IModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/stats").WithTags("Stats").RequireTalkieUser();

        g.MapGet("/", async (CurrentUser current, AfkService afk, CancellationToken ct) =>
        {
            await afk.CreditAsync(current.Id, ct);
            var u = await current.GetAsync(ct);
            return new StatsDto(u.Money, u.Shards, u.PlaytimeSeconds, u.MessagesSent, u.BoxesOpened, u.ShardMultiplier, u.AfkSince is not null);
        });

        g.MapGet("/leaderboard", async (CurrentUser current, TalkieDbContext db, CancellationToken ct) =>
        {
            var me = await current.GetAsync(ct);
            var users = await db.Users.AsNoTracking().Include(u => u.Company)
                .Where(u => u.CompanyId == me.CompanyId)
                .OrderByDescending(u => u.Money).ThenByDescending(u => u.Shards)
                .Take(10).ToListAsync(ct);
            return users.Select(u => new LeaderboardEntryDto(u.ToSummary(), u.Money, u.Shards, u.MessagesSent)).ToList();
        });
    }
}
