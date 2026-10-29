using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Talkie.Api.Infrastructure;
using Talkie.Data;
using Talkie.Shared;
using static Talkie.Api.Infrastructure.EndpointExtensions;

namespace Talkie.Api.Modules;

/// <summary>
/// The shop: exchange money and shards, buy and open boxes, buy avatar effects and shard boosters.
/// All balance changes are single conditional UPDATE statements so a balance can never go negative,
/// even with concurrent requests.
/// </summary>
public sealed class ShopModule : IModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/shop").WithTags("Shop").RequireTalkieUser();
        g.MapGet("/", GetShop);
        g.MapPost("/shards/buy", BuyShards);
        g.MapPost("/shards/sell", SellShards);
        g.MapPost("/boxes/{key}/buy", BuyBox);
        g.MapPost("/boxes/{id:guid}/open", OpenBox);
        g.MapPost("/effects/{key}/buy", BuyEffect);
        g.MapPut("/effects/active", EquipEffect);
        g.MapPost("/boosters/{key}/buy", BuyBooster);
    }

    internal static async Task<WalletDto> WalletAsync(TalkieDbContext db, Guid userId, CancellationToken ct)
    {
        var u = await db.Users.AsNoTracking().FirstAsync(x => x.Id == userId, ct);
        var owned = await db.OwnedItems.AsNoTracking().Where(o => o.UserId == userId).ToListAsync(ct);
        var boxes = await db.UnopenedBoxes.AsNoTracking().Where(b => b.UserId == userId).OrderBy(b => b.PurchasedAt).ToListAsync(ct);
        return new WalletDto(
            u.Money, u.Shards, u.ShardMultiplier, u.ActiveEffect,
            owned.Where(o => o.Kind == OwnedItemKind.Effect).Select(o => o.Key).ToList(),
            owned.Where(o => o.Kind == OwnedItemKind.Booster).Select(o => o.Key).ToList(),
            boxes.Select(b => new UnopenedBoxDto(b.Id, b.BoxKey, b.PurchasedAt)).ToList());
    }

    private static async Task<ShopDto> GetShop(CurrentUser current, TalkieDbContext db, CancellationToken ct)
    {
        var s = await db.AppSettings.AsNoTracking().FirstAsync(ct);
        return new ShopDto(new ExchangeRatesDto(s.MoneyPerShardBuy, s.MoneyPerShardSell), await WalletAsync(db, current.Id, ct),
            s.CommonBoxPrice, s.RareBoxPrice, s.ShardBoxPrice);
    }

    private static Task<int> TrySpendMoney(TalkieDbContext db, Guid userId, long amount, CancellationToken ct) =>
        db.Users.Where(u => u.Id == userId && u.Money >= amount)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Money, u => u.Money - amount), ct);

    private static async Task<IResult> BuyShards(ExchangeRequest request, CurrentUser current, TalkieDbContext db, CancellationToken ct)
    {
        if (request.Amount is <= 0 or > 1_000_000_000) return BadRequest("Enter a valid amount of shards.");
        var rate = (await db.AppSettings.AsNoTracking().FirstAsync(ct)).MoneyPerShardBuy;
        var cost = checked(request.Amount * rate);
        var updated = await db.Users.Where(u => u.Id == current.Id && u.Money >= cost)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Money, u => u.Money - cost)
                .SetProperty(u => u.Shards, u => u.Shards + request.Amount), ct);
        if (updated == 0) return BadRequest($"You need {NumberFormatter.Money(cost)} to buy {NumberFormatter.Compact(request.Amount)} shards.");
        return Results.Ok(await WalletAsync(db, current.Id, ct));
    }

    private static async Task<IResult> SellShards(ExchangeRequest request, CurrentUser current, TalkieDbContext db, CancellationToken ct)
    {
        if (request.Amount is <= 0 or > 1_000_000_000) return BadRequest("Enter a valid amount of shards.");
        var rate = (await db.AppSettings.AsNoTracking().FirstAsync(ct)).MoneyPerShardSell;
        var payout = checked(request.Amount * rate);
        var updated = await db.Users.Where(u => u.Id == current.Id && u.Shards >= request.Amount)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Shards, u => u.Shards - request.Amount)
                .SetProperty(u => u.Money, u => u.Money + payout), ct);
        if (updated == 0) return BadRequest("You don't have that many shards.");
        return Results.Ok(await WalletAsync(db, current.Id, ct));
    }

    internal static long BoxPrice(AppSettings s, string key) => key switch
    {
        "common" => s.CommonBoxPrice,
        "rare" => s.RareBoxPrice,
        "shard" => s.ShardBoxPrice,
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    private static async Task<IResult> BuyBox(string key, CurrentUser current, TalkieDbContext db, CancellationToken ct)
    {
        var box = Catalog.FindBox(key);
        if (box is null) return NotFound("Unknown box.");
        var price = BoxPrice(await db.AppSettings.AsNoTracking().FirstAsync(ct), key);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (await TrySpendMoney(db, current.Id, price, ct) == 0) return BadRequest($"You need {NumberFormatter.Money(price)} for a {box.Name}.");
        var unopened = new UnopenedBox { UserId = current.Id, BoxKey = key };
        db.UnopenedBoxes.Add(unopened);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Results.Ok(new UnopenedBoxDto(unopened.Id, unopened.BoxKey, unopened.PurchasedAt));
    }

    /// <summary>The reward is rolled on the server when the box is opened, after the user tapped it open.</summary>
    private static async Task<IResult> OpenBox(Guid id, CurrentUser current, TalkieDbContext db, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var unopened = await db.UnopenedBoxes.FirstOrDefaultAsync(b => b.Id == id && b.UserId == current.Id, ct);
        if (unopened is null) return NotFound("Box not found.");
        var box = Catalog.FindBox(unopened.BoxKey)!;

        // Deleting first guarantees a box can only be opened once.
        if (await db.UnopenedBoxes.Where(b => b.Id == id).ExecuteDeleteAsync(ct) == 0) return NotFound("Box not found.");

        var reward = Catalog.PickReward(box, RandomNumberGenerator.GetInt32(Catalog.TotalWeight(box)));
        var amount = reward.Amount;
        if (box.RewardCurrency == Currency.Money)
        {
            await db.Users.Where(u => u.Id == current.Id).ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Money, u => u.Money + amount)
                .SetProperty(u => u.BoxesOpened, u => u.BoxesOpened + 1), ct);
        }
        else
        {
            await db.Users.Where(u => u.Id == current.Id).ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Shards, u => u.Shards + amount)
                .SetProperty(u => u.BoxesOpened, u => u.BoxesOpened + 1), ct);
        }
        await tx.CommitAsync(ct);

        return Results.Ok(new BoxOpenResultDto(id, box.Key, amount, box.RewardCurrency, await WalletAsync(db, current.Id, ct)));
    }

    private static async Task<IResult> BuyEffect(string key, CurrentUser current, TalkieDbContext db, CancellationToken ct)
    {
        var effect = Catalog.FindEffect(key);
        if (effect is null) return NotFound("Unknown effect.");
        if (await db.OwnedItems.AnyAsync(o => o.UserId == current.Id && o.Kind == OwnedItemKind.Effect && o.Key == key, ct))
            return BadRequest("You already own this effect.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (await TrySpendMoney(db, current.Id, effect.Price, ct) == 0) return BadRequest($"You need {NumberFormatter.Money(effect.Price)} for {effect.Name}.");
        db.OwnedItems.Add(new OwnedItem { UserId = current.Id, Kind = OwnedItemKind.Effect, Key = key });
        await db.SaveChangesAsync(ct);
        // A freshly bought effect is equipped straight away.
        await db.Users.Where(u => u.Id == current.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.ActiveEffect, key), ct);
        await tx.CommitAsync(ct);
        return Results.Ok(await WalletAsync(db, current.Id, ct));
    }

    public sealed record EquipRequest(string? Key);

    private static async Task<IResult> EquipEffect(EquipRequest request, CurrentUser current, TalkieDbContext db, CancellationToken ct)
    {
        if (request.Key is not null &&
            !await db.OwnedItems.AnyAsync(o => o.UserId == current.Id && o.Kind == OwnedItemKind.Effect && o.Key == request.Key, ct))
        {
            return BadRequest("You don't own this effect.");
        }
        await db.Users.Where(u => u.Id == current.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.ActiveEffect, request.Key), ct);
        return Results.Ok(await WalletAsync(db, current.Id, ct));
    }

    private static async Task<IResult> BuyBooster(string key, CurrentUser current, TalkieDbContext db, AfkService afk, CancellationToken ct)
    {
        var booster = Catalog.FindBooster(key);
        if (booster is null) return NotFound("Unknown booster.");
        if (await db.OwnedItems.AnyAsync(o => o.UserId == current.Id && o.Kind == OwnedItemKind.Booster && o.Key == key, ct))
            return BadRequest("You already own this booster.");

        // Credit AFK time earned so far at the old multiplier before the new one applies.
        await afk.CreditAsync(current.Id, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (await TrySpendMoney(db, current.Id, booster.Price, ct) == 0) return BadRequest($"You need {NumberFormatter.Money(booster.Price)} for {booster.Name}.");
        db.OwnedItems.Add(new OwnedItem { UserId = current.Id, Kind = OwnedItemKind.Booster, Key = key });
        await db.SaveChangesAsync(ct);
        // Boosters are permanent; the best one you own is the one that counts.
        var multiplier = booster.Multiplier;
        await db.Users.Where(u => u.Id == current.Id && u.ShardMultiplier < multiplier)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.ShardMultiplier, multiplier), ct);
        await tx.CommitAsync(ct);
        return Results.Ok(await WalletAsync(db, current.Id, ct));
    }
}
