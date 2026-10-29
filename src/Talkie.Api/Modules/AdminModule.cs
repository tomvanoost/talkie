using Microsoft.EntityFrameworkCore;
using Talkie.Api.Infrastructure;
using Talkie.Data;
using Talkie.Shared;
using static Talkie.Api.Infrastructure.EndpointExtensions;

namespace Talkie.Api.Modules;

/// <summary>App configuration, only for admin@talkie.com (the pencil button in the top bar).</summary>
public sealed class AdminModule : IModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/admin").WithTags("Admin").RequireTalkieUser();
        g.AddEndpointFilter(async (ctx, next) =>
        {
            var current = ctx.HttpContext.RequestServices.GetRequiredService<CurrentUser>();
            var me = await current.GetAsync(ctx.HttpContext.RequestAborted);
            return CurrentUser.IsAdmin(me) ? await next(ctx) : Forbidden("Only admin@talkie.com can configure the app.");
        });

        g.MapGet("/settings", async (TalkieDbContext db, CancellationToken ct) => ToDto(await db.AppSettings.AsNoTracking().FirstAsync(ct)));
        g.MapPut("/settings", SaveSettings);

        g.MapGet("/companies", async (TalkieDbContext db, CancellationToken ct) =>
            await db.Companies.AsNoTracking().OrderBy(c => c.Name)
                .Select(c => new AdminCompanyDto(c.Id, c.Name,
                    c.Members.Where(m => m.IsOwner).Select(m => m.Email).FirstOrDefault() ?? "",
                    c.Members.Count, c.CreatedAt))
                .ToListAsync(ct));

        g.MapDelete("/companies/{id:guid}", async (Guid id, TalkieDbContext db, DeletionService deletion, CancellationToken ct) =>
        {
            var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
            if (company is null) return NotFound("Company not found.");
            if (company.Name == TalkieConstants.DefaultCompanyName) return BadRequest("The Talkie company cannot be deleted.");
            await deletion.DeleteCompanyAsync(id, ct);
            return Results.NoContent();
        });

        g.MapPost("/grant", async (GrantRequest request, TalkieDbContext db, CancellationToken ct) =>
        {
            var email = Validation.NormalizeEmail(request.Email ?? "");
            var updated = await db.Users.Where(u => u.Email == email)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.Money, u => u.Money + request.Money < 0 ? 0 : u.Money + request.Money)
                    .SetProperty(u => u.Shards, u => u.Shards + request.Shards < 0 ? 0 : u.Shards + request.Shards), ct);
            return updated == 0 ? NotFound("No user with this e-mail exists.") : Results.NoContent();
        });
    }

    private static AppSettingsDto ToDto(AppSettings s) => new(
        s.AppName, s.Announcement, s.AllowCompanyCreation, s.StartingMoney, s.StartingShards, s.AfkShardsPerMinute, s.AfkMaxHours,
        s.MoneyPerShardBuy, s.MoneyPerShardSell, s.CommonBoxPrice, s.RareBoxPrice, s.ShardBoxPrice);

    private static async Task<IResult> SaveSettings(AppSettingsDto dto, TalkieDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.AppName) || dto.AppName.Length > 100) return BadRequest("App name is required.");
        if (dto.StartingMoney < 0 || dto.StartingShards < 0) return BadRequest("Starting balances cannot be negative.");
        if (dto.AfkShardsPerMinute is < 0 or > 1_000_000) return BadRequest("AFK shards per minute is out of range.");
        if (dto.AfkMaxHours is < 0 or > 24 * 365) return BadRequest("AFK maximum hours is out of range.");
        if (dto.MoneyPerShardBuy <= 0 || dto.MoneyPerShardSell <= 0) return BadRequest("Exchange rates must be positive.");
        if (dto.MoneyPerShardSell > dto.MoneyPerShardBuy) return BadRequest("Selling shards cannot pay more than buying them costs.");
        if (dto.CommonBoxPrice <= 0 || dto.RareBoxPrice <= 0 || dto.ShardBoxPrice <= 0) return BadRequest("Box prices must be positive.");

        var s = await db.AppSettings.FirstAsync(ct);
        s.AppName = dto.AppName.Trim();
        s.Announcement = string.IsNullOrWhiteSpace(dto.Announcement) ? null : dto.Announcement.Trim();
        s.AllowCompanyCreation = dto.AllowCompanyCreation;
        s.StartingMoney = dto.StartingMoney;
        s.StartingShards = dto.StartingShards;
        s.AfkShardsPerMinute = dto.AfkShardsPerMinute;
        s.AfkMaxHours = dto.AfkMaxHours;
        s.MoneyPerShardBuy = dto.MoneyPerShardBuy;
        s.MoneyPerShardSell = dto.MoneyPerShardSell;
        s.CommonBoxPrice = dto.CommonBoxPrice;
        s.RareBoxPrice = dto.RareBoxPrice;
        s.ShardBoxPrice = dto.ShardBoxPrice;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(s));
    }
}
