using System;
using System.Collections.Generic;
using DeadCellsMultiplayerMod.Interaction;
using Xunit;

namespace DeadCellsMultiplayerMod.ProtocolReplay.Tests;

public sealed class ShopAndMerchantSyncTests
{
    [Fact]
    public void ValidateGoldTransaction_ValidPurchase_CalculatesCorrectRemainder()
    {
        var success = ShopSyncHelpers.ValidateGoldTransaction(
            currentMoney: 1500,
            price: 450,
            out var newMoney,
            out var rejectionReason);

        Assert.True(success);
        Assert.Null(rejectionReason);
        Assert.Equal(1050, newMoney);
    }

    [Fact]
    public void ValidateGoldTransaction_InsufficientFunds_RejectsWithZeroGoldSpent()
    {
        var success = ShopSyncHelpers.ValidateGoldTransaction(
            currentMoney: 200,
            price: 500,
            out var newMoney,
            out var rejectionReason);

        Assert.False(success);
        Assert.NotNull(rejectionReason);
        Assert.Contains("Insufficient funds", rejectionReason, StringComparison.OrdinalIgnoreCase);
        // Guaranteed: 0 gold spent, money untouched
        Assert.Equal(200, newMoney);
    }

    [Theory]
    [InlineData(-1)]
    public void ValidateGoldTransaction_NegativePrice_StrictlyRejected(int negativePrice)
    {
        var success = ShopSyncHelpers.ValidateGoldTransaction(
            currentMoney: 1000,
            price: negativePrice,
            out var newMoney,
            out var rejectionReason);

        Assert.False(success);
        Assert.NotNull(rejectionReason);
        Assert.Contains("cannot be negative", rejectionReason, StringComparison.OrdinalIgnoreCase);
        Assert.True(newMoney >= 0);
    }

    [Theory]
    [InlineData(1, 0, true)]
    public void ValidateRerollCounter_PositiveRemaining_DecrementsSafely(
        int current, int expectedNew, bool expectedSuccess)
    {
        var success = ShopSyncHelpers.ValidateRerollCounter(
            current,
            out var newUses,
            out var rejectionReason);

        Assert.Equal(expectedSuccess, success);
        Assert.Null(rejectionReason);
        Assert.Equal(expectedNew, newUses);
    }

    [Theory]
    [InlineData(0)]
    public void ValidateRerollCounter_ZeroOrNegative_RejectsAndClampsToZero(int exhausted)
    {
        var success = ShopSyncHelpers.ValidateRerollCounter(
            exhausted,
            out var newUses,
            out var rejectionReason);

        Assert.False(success);
        Assert.NotNull(rejectionReason);
        Assert.Contains("No reroll uses", rejectionReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, newUses);
    }

    [Fact]
    public void ShopReservationManager_LinkedAltars_EnforcesMutualExclusion()
    {
        var manager = new ShopReservationManager();
        var levelId = "TreasureRoom";

        // Altar A at (100, 200) and linked Altar B at (160, 200)
        var linkedToA = new List<(double X, double Y)> { (160.0, 200.0) };
        var linkedToB = new List<(double X, double Y)> { (100.0, 200.0) };

        // Player 1 claims Altar A (and by link, Altar B is claimed)
        var p1Claim = manager.TryClaimLinked(
            levelId,
            primaryX: 100.0,
            primaryY: 200.0,
            linkedPositions: linkedToA,
            userId: 1,
            sequence: 50L);
        Assert.True(p1Claim);

        // Player 2 simultaneously attempts to claim Altar B
        var p2Claim = manager.TryClaimLinked(
            levelId,
            primaryX: 160.0,
            primaryY: 200.0,
            linkedPositions: linkedToB,
            userId: 2,
            sequence: 51L);
        Assert.False(p2Claim); // Clean rejection: 1-choice rule preserved!

        // Direct claim on Altar B also rejected
        Assert.False(manager.TryClaim(levelId, 160.0, 200.0, userId: 2, sequence: 52L));
    }

    [Fact]
    public void ShopReservationManager_LevelScopingAndClear_IsolatesBiomes()
    {
        var manager = new ShopReservationManager();

        // Same coordinates in different biomes
        Assert.True(manager.TryClaim("Biome1", 100.0, 100.0, userId: 1, sequence: 1L));
        Assert.True(manager.TryClaim("Biome2", 100.0, 100.0, userId: 2, sequence: 2L));

        Assert.True(manager.IsClaimed("Biome1", 100.0, 100.0));
        Assert.True(manager.IsClaimed("Biome2", 100.0, 100.0));

        // Clear only Biome1
        manager.ClearLevel("Biome1");
        Assert.False(manager.IsClaimed("Biome1", 100.0, 100.0));
        Assert.True(manager.IsClaimed("Biome2", 100.0, 100.0));

        // Clear all
        manager.ClearAll();
        Assert.False(manager.IsClaimed("Biome2", 100.0, 100.0));
        Assert.Equal(0, manager.ActiveClaimCount);
    }

    [Fact]
    public void NetNode_InterShopPurchasePayload_ParsesAndValidates()
    {
        var node = new NetNode(1, NetRole.Host);

        // Valid purchase line: userId|x|y|price|sequence|isMimic|levelId
        var line = "2|150.5|200.0|1200|42|0|Promenade";
        Assert.True(node.TestTryParseInterShopPurchase(line, senderId: 2, forceSenderId: true, out var ev));
        Assert.Equal(2, ev.UserId);
        Assert.Equal(150.5, ev.X);
        Assert.Equal(200.0, ev.Y);
        Assert.Equal(1200, ev.Price);
        Assert.Equal(42L, ev.Sequence);
        Assert.False(ev.IsMimic);
        Assert.Equal("Promenade", ev.LevelId);

        // Mimic purchase
        var mimicLine = "3|100.0|50.0|0|10|1|Bank";
        Assert.True(node.TestTryParseInterShopPurchase(mimicLine, senderId: null, forceSenderId: false, out var mimicEv));
        Assert.Equal(3, mimicEv.UserId);
        Assert.True(mimicEv.IsMimic);

        // Rejections: negative price, invalid coords, malformed
        Assert.False(node.TestTryParseInterShopPurchase("2|150|200|-50|1|0|Level", null, false, out _));
        Assert.False(node.TestTryParseInterShopPurchase("2|NaN|200|50|1|0|Level", null, false, out _));
        Assert.False(node.TestTryParseInterShopPurchase("0|100|100|50|1|0|Level", null, false, out _));
        Assert.False(node.TestTryParseInterShopPurchase(string.Empty, null, false, out _));
        Assert.False(node.TestTryParseInterShopPurchase(null!, null, false, out _));
    }

    [Fact]
    public void NetNode_InterShopRerollPayload_ParsesAndValidates()
    {
        var node = new NetNode(1, NetRole.Host);

        // Valid reroll line: userId|x|y|cost|remainUses|sequence|levelId
        var line = "1|350.0|400.0|150|2|88|Passage";
        Assert.True(node.TestTryParseInterShopReroll(line, senderId: 1, forceSenderId: true, out var ev));
        Assert.Equal(1, ev.UserId);
        Assert.Equal(350.0, ev.X);
        Assert.Equal(400.0, ev.Y);
        Assert.Equal(150, ev.Cost);
        Assert.Equal(2, ev.RemainUses);
        Assert.Equal(88L, ev.Sequence);
        Assert.Equal("Passage", ev.LevelId);

        // Rejections: negative cost or uses
        Assert.False(node.TestTryParseInterShopReroll("1|350|400|-10|2|1|Level", null, false, out _));
        Assert.False(node.TestTryParseInterShopReroll("1|350|400|0|-1|1|Level", null, false, out _));
        Assert.False(node.TestTryParseInterShopReroll(string.Empty, null, false, out _));
    }

    [Fact]
    public void NetNode_InterPedestalPickupPayload_ParsesAndValidates()
    {
        var node = new NetNode(1, NetRole.Host);

        // Valid pedestal pickup: userId|x|y|sequence|levelId
        var line = "4|220.0|180.0|15|ClockTower";
        Assert.True(node.TestTryParseInterPedestalPickup(line, senderId: 4, forceSenderId: true, out var ev));
        Assert.Equal(4, ev.UserId);
        Assert.Equal(220.0, ev.X);
        Assert.Equal(180.0, ev.Y);
        Assert.Equal(15L, ev.Sequence);
        Assert.Equal("ClockTower", ev.LevelId);

        // Rejections: invalid userId, bad coords
        Assert.False(node.TestTryParseInterPedestalPickup("0|220|180|1|Level", null, false, out _));
        Assert.False(node.TestTryParseInterPedestalPickup("1|Infinity|180|1|Level", null, false, out _));
        Assert.False(node.TestTryParseInterPedestalPickup("short", null, false, out _));
    }

    [Fact]
    public void ShopReservationManager_LinkedAltarRelease_AtomicallyReleasesAllPedestals()
    {
        var manager = new ShopReservationManager();
        var levelId = "ChoiceRoom";

        // Claim altar A linked to altar B
        var linkedToA = new List<(double X, double Y)> { (250.0, 100.0) };
        Assert.True(manager.TryClaimLinked(levelId, 200.0, 100.0, linkedToA, userId: 1, sequence: 10L));

        // Both are claimed
        Assert.True(manager.IsClaimed(levelId, 200.0, 100.0));
        Assert.True(manager.IsClaimed(levelId, 250.0, 100.0));

        // Release primary altar A -> both A and linked altar B must be released!
        Assert.True(manager.Release(levelId, 200.0, 100.0));
        Assert.False(manager.IsClaimed(levelId, 200.0, 100.0));
        Assert.False(manager.IsClaimed(levelId, 250.0, 100.0)); // B released too!

        // Now player 2 can successfully claim either pedestal
        Assert.True(manager.TryClaim(levelId, 250.0, 100.0, userId: 2, sequence: 20L));
    }

    [Fact]
    public void ShopReservationManager_ConcurrentThreadsRacing_GuaranteesSingleWinner()
    {
        var manager = new ShopReservationManager();
        var levelId = "PrisonersQuarters";
        const int threadCount = 30;
        int winners = 0;
        int losers = 0;
        var barrier = new System.Threading.Barrier(threadCount);
        var threads = new System.Threading.Thread[threadCount];

        for (int i = 0; i < threadCount; i++)
        {
            int userId = i + 1;
            threads[i] = new System.Threading.Thread(() =>
            {
                barrier.SignalAndWait();
                if (manager.TryClaim(levelId, 300.0, 150.0, userId, sequence: userId))
                {
                    System.Threading.Interlocked.Increment(ref winners);
                }
                else
                {
                    System.Threading.Interlocked.Increment(ref losers);
                }
            });
            threads[i].Start();
        }

        for (int i = 0; i < threadCount; i++)
        {
            threads[i].Join();
        }

        // Exactly one winner, everyone else cleanly rejected
        Assert.Equal(1, winners);
        Assert.Equal(threadCount - 1, losers);
        Assert.Equal(1, manager.ActiveClaimCount);
    }
}
