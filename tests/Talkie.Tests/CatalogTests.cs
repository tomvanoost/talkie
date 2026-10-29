using Talkie.Shared;

namespace Talkie.Tests;

public class CatalogTests
{
    [Fact]
    public void Boxes_match_the_specification()
    {
        Assert.Equal([100L, 200, 500, 1_000], Catalog.FindBox("common")!.Rewards.Select(r => r.Amount));
        Assert.Equal(200, Catalog.FindBox("common")!.Price);

        Assert.Equal([200L, 500, 1_000, 5_000, 10_000], Catalog.FindBox("rare")!.Rewards.Select(r => r.Amount));
        Assert.Equal(500, Catalog.FindBox("rare")!.Price);

        var shard = Catalog.FindBox("shard")!;
        Assert.Equal([100L, 200, 500, 1_000, 5_000], shard.Rewards.Select(r => r.Amount));
        Assert.Equal(1_000, shard.Price);
        Assert.Equal(Currency.Shards, shard.RewardCurrency);
    }

    [Fact]
    public void PickReward_covers_every_reward_exactly_by_weight()
    {
        foreach (var box in Catalog.Boxes)
        {
            var counts = Enumerable.Range(0, Catalog.TotalWeight(box))
                .GroupBy(roll => Catalog.PickReward(box, roll).Amount)
                .ToDictionary(g => g.Key, g => g.Count());
            foreach (var reward in box.Rewards)
            {
                Assert.Equal(reward.Weight, counts[reward.Amount]);
            }
        }
    }

    [Fact]
    public void PickReward_rejects_out_of_range_rolls()
    {
        var box = Catalog.Boxes[0];
        Assert.Throws<ArgumentOutOfRangeException>(() => Catalog.PickReward(box, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Catalog.PickReward(box, Catalog.TotalWeight(box)));
    }

    [Fact]
    public void Shop_sells_lightning_effect_and_x2_x4_boosters()
    {
        Assert.NotNull(Catalog.FindEffect("lightning"));
        Assert.Equal(2, Catalog.FindBooster("x2")!.Multiplier);
        Assert.Equal(4, Catalog.FindBooster("x4")!.Multiplier);
    }
}
