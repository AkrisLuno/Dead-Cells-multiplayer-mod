using Xunit;

namespace DeadCellsMultiplayerMod;

public sealed class EnemyStateTests
{
    [Fact]
    public void EnemyWorldAndHitsHaveSeparateAuthoritiesAndRejectDuplicateEntries()
    {
        var level = new LootLevel("PrisonStart", 24, 1);
        var key = new EnemyKey("Zombie", 10, 20, 0);
        var enemy = new EnemyState(key, 240, 480, 1, 20, 40, "idle", 1, 1, 1, false,
            new EnemyImpact(1, 20, 1, false));
        var world = new EnemyWorld(level, 4, [enemy]);
        var hit = new EnemyHit(5, key, 12, 1, false);
        var hits = new EnemyHits(level, [hit]);
        Assert.True(new EnemySync(World: world).IsValidFor(receiverIsHost: false));
        Assert.False(new EnemySync(World: world).IsValidFor(receiverIsHost: true));
        Assert.True(new EnemySync(Hits: hits).IsValidFor(receiverIsHost: true));
        Assert.False(new EnemySync(Hits: hits).IsValidFor(receiverIsHost: false));
        Assert.False((world with { Enemies = [enemy, enemy] }).IsValid);
        Assert.False((hits with { Hits = [hit, hit] }).IsValid);
        Assert.False((hit with { Damage = int.MaxValue }).IsValid);
        Assert.False((enemy with { Life = 41 }).IsValid);
    }

    [Fact]
    public void EnemyStrikesRequireAnAuthoritativeAttackerAndOrderedReceipts()
    {
        var level = new LootLevel("PrisonStart", 24, 1);
        var key = new EnemyKey("Zombie", 10, 20, 0);
        var enemy = new EnemyState(key, 240, 480, 1, 40, 40, "idle", 1, 1, 1, false, null);
        var strike = new EnemyStrike(1, key, 20, 0, 250, 480);
        var world = new EnemyWorld(level, 0, [enemy], [strike]);
        Assert.True(world.IsValid);
        Assert.False((world with { Enemies = [] }).IsValid);
        Assert.False((world with { Strikes = [strike, strike] }).IsValid);
        Assert.False((strike with { Power = double.NaN }).IsValid);
        Assert.False((strike with { DamageType = 12 }).IsValid);
        Assert.True(new EnemySync(ReceivedStrike: 1).IsValidFor(receiverIsHost: true));
        Assert.False(new EnemySync(ReceivedStrike: 1).IsValidFor(receiverIsHost: false));
    }
}
