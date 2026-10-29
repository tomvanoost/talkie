namespace Talkie.Shared;

public enum Currency { Money, Shards }

public sealed record BoxReward(long Amount, int Weight);

public sealed record BoxDefinition(
    string Key,
    string Name,
    string Description,
    long Price,
    Currency RewardCurrency,
    string Color,
    IReadOnlyList<BoxReward> Rewards);

public sealed record EffectDefinition(string Key, string Name, string Description, long Price, string Icon);

public sealed record BoosterDefinition(string Key, string Name, int Multiplier, long Price);

/// <summary>
/// The shop catalog. Shared between the API (which enforces prices) and the client (which renders the shop).
/// </summary>
public static class Catalog
{
    /// <summary>How many taps a box needs before it opens.</summary>
    public const int TapsToOpen = 10;

    public static readonly IReadOnlyList<BoxDefinition> Boxes =
    [
        new("common", "Common Box", "Win $100, $200, $500 or $1K", 200, Currency.Money, "#90CAF9",
        [
            new(100, 50), new(200, 30), new(500, 15), new(1_000, 5),
        ]),
        new("rare", "Rare Box", "Win $200, $500, $1K, $5K or $10K", 500, Currency.Money, "#B39DDB",
        [
            new(200, 45), new(500, 30), new(1_000, 17), new(5_000, 6), new(10_000, 2),
        ]),
        new("shard", "Shard Box", "Win 100, 200, 500, 1K or 5K shards", 1_000, Currency.Shards, "#80DEEA",
        [
            new(100, 40), new(200, 30), new(500, 18), new(1_000, 10), new(5_000, 2),
        ]),
    ];

    public static readonly IReadOnlyList<EffectDefinition> Effects =
    [
        new("lightning", "Energy Lightning", "Small yellow energy bolts strike around your avatar.", 2_500, "bolt"),
        new("fire", "Blazing Fire", "Your avatar is wrapped in a flickering flame ring.", 5_000, "local_fire_department"),
        new("sparkles", "Sparkles", "Glittering stars twinkle around your avatar.", 1_500, "auto_awesome"),
        new("rainbow", "Rainbow Halo", "A rotating rainbow ring circles your avatar.", 7_500, "looks"),
        new("frost", "Frost Aura", "A cool icy glow pulses around your avatar.", 3_500, "ac_unit"),
        new("hearts", "Floating Hearts", "Little hearts float up from your avatar.", 2_000, "favorite"),
        new("galaxy", "Galaxy Orbit", "Planets orbit your avatar.", 15_000, "public"),
    ];

    public static readonly IReadOnlyList<BoosterDefinition> Boosters =
    [
        new("x2", "Shard Booster x2", 2, 10_000),
        new("x4", "Shard Booster x4", 4, 50_000),
    ];

    public static BoxDefinition? FindBox(string key) => Boxes.FirstOrDefault(b => b.Key == key);
    public static EffectDefinition? FindEffect(string key) => Effects.FirstOrDefault(e => e.Key == key);
    public static BoosterDefinition? FindBooster(string key) => Boosters.FirstOrDefault(b => b.Key == key);

    /// <summary>Picks a weighted reward. <paramref name="roll"/> must be in [0, total weight).</summary>
    public static BoxReward PickReward(BoxDefinition box, int roll)
    {
        var total = box.Rewards.Sum(r => r.Weight);
        ArgumentOutOfRangeException.ThrowIfNegative(roll);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(roll, total);

        foreach (var reward in box.Rewards)
        {
            if (roll < reward.Weight) return reward;
            roll -= reward.Weight;
        }
        return box.Rewards[^1];
    }

    public static int TotalWeight(BoxDefinition box) => box.Rewards.Sum(r => r.Weight);
}
