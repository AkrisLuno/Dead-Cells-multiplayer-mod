using Xunit;

namespace DeadCellsMultiplayerMod;

public sealed class LootStateTests
{
    [Fact]
    public void LootFrameRequiresMatchingLayoutAndOrderedSequences()
    {
        var scope = new LootLevel("PrisonStart", 111, 42);
        var player = new PlayerState("PrisonStart", 10, 20, 1, "idle", 111) { Layout = 42 };
        Assert.True(scope.Matches(player));
        Assert.False(scope.Matches(player with { Layout = 7 }));
        Assert.False(scope.Matches(player with { LevelSeed = 99 }));

        var drop = new MobDrop(DropKind.Heal, Heal: 0.25);
        var batch = new DropBatch(1, 5, 5, null, [drop]);
        Assert.True(new DropFrame(scope, [batch, batch with { Sequence = 2 }]).IsValid);
        Assert.False(new DropFrame(scope, [batch, batch]).IsValid);
        Assert.False(new DropFrame(scope, [batch with { Drops = [drop with { Heal = double.NaN }] }]).IsValid);
    }
}
