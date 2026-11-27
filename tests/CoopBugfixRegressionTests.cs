using System;
using DeadCellsMultiplayerMod.Ghost;
using DeadCellsMultiplayerMod.Interaction;
using DeadCellsMultiplayerMod.Mobs.MobsSynchronization;
using Xunit;

namespace DeadCellsMultiplayerMod.ProtocolReplay.Tests;

public sealed class CoopBugfixRegressionTests
{
    [Theory]
    [InlineData(13.0, 1.0, 0.25)]     // Distance 13 (half of 26) -> 0.25 volume
    [InlineData(23.0, 1.0, null)]     // Distance 23 -> (1 - 23/26)^2 = 0.0133 < 0.02 cutoff -> null
    public void CalculateSpatializedVolume_AttenuationAndCutoffBoundaries(double distance, double initialVol, double? expectedVol)
    {
        var result = CombatSyncHelpers.CalculateSpatializedVolume(
            heroX: 0.0,
            heroY: 0.0,
            srcX: distance,
            srcY: 0.0,
            originalVolume: initialVol,
            maxDistanceCases: 26.0);

        if (expectedVol == null)
        {
            Assert.Null(result);
        }
        else
        {
            Assert.NotNull(result);
            Assert.Equal(expectedVol.Value, result.Value, precision: 2);
        }
    }

    [Fact]
    public void CalculateSpatializedVolume_VerticalDistance_AttenuatesFasterThanHorizontal()
    {
        // Bug 9: Multi-floor weapon sounds in Biome 2 bleed across map unless vertical distance is weighted.
        // At horizontal delta = 15, dy = 0:
        var horizontal = CombatSyncHelpers.CalculateSpatializedVolume(0, 0, 15, 0, 1.0, 26.0);
        // At vertical delta = 15, dx = 0: effective distance is 15 * 1.35 = 20.25:
        var vertical = CombatSyncHelpers.CalculateSpatializedVolume(0, 0, 0, 15, 1.0, 26.0);

        Assert.NotNull(horizontal);
        Assert.NotNull(vertical);
        // Vertical volume must be substantially lower than horizontal volume at the same coordinate distance
        Assert.True(vertical.Value < horizontal.Value);
        Assert.True(vertical.Value < 0.06); // ~0.049 vs ~0.179

        // At vertical delta = 18: effective distance is 18 * 1.35 = 24.3.
        // (1 - 24.3/26)^2 = 0.0042 < 0.035 cutoff threshold -> should be suppressed (null)
        var verticalSuppressed = CombatSyncHelpers.CalculateSpatializedVolume(0, 0, 0, 18, 1.0, 26.0);
        Assert.Null(verticalSuppressed);

        // While horizontal delta = 18 is still audible
        var horizontalAudible = CombatSyncHelpers.CalculateSpatializedVolume(0, 0, 18, 0, 1.0, 26.0);
        Assert.NotNull(horizontalAudible);
        Assert.True(horizontalAudible.Value > 0.035);
    }

    [Fact]
    public void MUser_ResolveSlotFromSources_FollowsStrictPriorityHierarchy()
    {
        // Bug 8: Priority 1: Explicit slot
        Assert.Equal(2, DeadCellsMultiplayerMod.MUser.ResolveSlotFromSources(2, "3", 1, () => 0));

        // Priority 2: DCCM_SAVE_SLOT environment variable (per-process isolation for co-op)
        Assert.Equal(3, DeadCellsMultiplayerMod.MUser.ResolveSlotFromSources(null, "3", 1, () => 0));

        // Priority 3: MultiplayerSettingsStorage profile slot (persisted Player 2 settings)
        Assert.Equal(1, DeadCellsMultiplayerMod.MUser.ResolveSlotFromSources(null, null, 1, () => 0));

        // Priority 4: Game options fallback
        Assert.Equal(4, DeadCellsMultiplayerMod.MUser.ResolveSlotFromSources(null, null, -1, () => 4));

        // Priority 5: Default slot 0
        Assert.Equal(0, DeadCellsMultiplayerMod.MUser.ResolveSlotFromSources(null, null, -1, null));
    }

    [Theory]
    [InlineData("10:20", "16:26", 6, true)] // Exactly 6 tiles (dx=6, dy=6)
    [InlineData("10:20", "17:20", 6, false)] // dx=7, exceeds 6
    public void CombatSyncHelpers_IsDoorProximityMatch_ValidatesProximity(string? k1, string? k2, int threshold, bool expected)
    {
        var result = CombatSyncHelpers.IsDoorProximityMatch(k1!, k2!, threshold);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void MUser_ResolveSlotFromSources_HandlesThrowingGameOptionsFallback()
    {
        var slot = DeadCellsMultiplayerMod.MUser.ResolveSlotFromSources(
            null,
            null,
            -1,
            () => throw new InvalidOperationException("Simulated early game options uninitialized state"));

        Assert.Equal(0, slot);
    }

    [Fact]
    public void BossCinePayload_TryParse_RejectsNonFiniteCoordinates()
    {
        var raw = "BossLevel|roomEvent|NaN|200.0|1|150.0|Infinity|-1";
        var parsed = CombatSyncHelpers.TryParseBossCinePayload(
            raw,
            out var levelId,
            out var eventId,
            out var snapX,
            out var snapY,
            out var snapDir,
            out var finalX,
            out var finalY,
            out var finalDir);

        Assert.True(parsed);
        Assert.Equal("BossLevel", levelId);
        Assert.Equal("roomEvent", eventId);
        Assert.Null(snapX); // NaN rejected
        Assert.Equal(200.0, snapY);
        Assert.Equal(1, snapDir);
        Assert.Equal(150.0, finalX);
        Assert.Null(finalY); // Infinity rejected
        Assert.Equal(-1, finalDir);
    }

    [Fact]
    public void MobWireBinary_TryParseMobStatesBinary_RejectsNonFiniteFloats()
    {
        var states = new List<NetNode.MobStateSnapshot>
        {
            new NetNode.MobStateSnapshot(1, 100.0, 200.0, 1, 50, 50, "idle", "", "", 0, 1.0, 0.0, 0.0)
        };

        var ok = MobWireBinary.TryBuildMobStatesBinary(states, out var bytes);
        Assert.True(ok);
        Assert.NotNull(bytes);

        // Valid roundtrip
        var dest = new List<NetNode.MobStateSnapshot>();
        Assert.True(MobWireBinary.TryParseMobStatesBinary(bytes, dest));
        Assert.Single(dest);
        Assert.Equal(100.0, dest[0].X);

        // Corrupt x with double.NaN
        var corrupted = (byte[])bytes.Clone();
        var nanBytes = BitConverter.GetBytes(double.NaN);
        // ver(1) + count(2) + index(4) + gen(4) = 11 offset for x
        Array.Copy(nanBytes, 0, corrupted, 11, 8);

        var corruptDest = new List<NetNode.MobStateSnapshot>();
        var corruptOk = MobWireBinary.TryParseMobStatesBinary(corrupted, corruptDest);
        Assert.False(corruptOk);
        Assert.Empty(corruptDest);
    }

    [Fact]
    public void MobWireBinary_TryBuildAndParse_RoundtripsAndSanitizesNonFiniteCoordinates()
    {
        var inputStates = new List<NetNode.MobStateSnapshot>
        {
            new NetNode.MobStateSnapshot(
                index: 10,
                x: double.NaN, // Non-finite coordinate
                y: 250.5,
                dir: -1,
                life: 100,
                maxLife: 200,
                animPayload: "walk",
                type: "Zombie",
                statePayload: "PrisonersQuarters",
                generation: 1,
                time: 0.5,
                dx: double.PositiveInfinity,
                dy: -1.5)
        };

        Assert.True(MobWireBinary.TryBuildMobStatesBinary(inputStates, out var bytes));
        Assert.NotNull(bytes);

        var outputStates = new List<NetNode.MobStateSnapshot>();
        Assert.True(MobWireBinary.TryParseMobStatesBinary(bytes, outputStates));
        Assert.Single(outputStates);

        var parsed = outputStates[0];
        Assert.Equal(10, parsed.Index);
        Assert.Equal(0.0, parsed.X); // NaN sanitized to 0.0
        Assert.Equal(250.5, parsed.Y);
        Assert.Equal(-1, parsed.Dir);
        Assert.Equal(100, parsed.Life);
        Assert.Equal(200, parsed.MaxLife);
        Assert.Equal("walk", parsed.AnimPayload);
        Assert.Equal("Zombie", parsed.Type);
        Assert.Equal("PrisonersQuarters", parsed.StatePayload);
        Assert.Equal(1, parsed.Generation);
        Assert.Equal(0.0, parsed.Dx); // PositiveInfinity sanitized to 0.0
        Assert.Equal(-1.5, parsed.Dy);
    }

    [Fact]
    public void MobWireBinary_TryParseMobStatesBinary_DetectsTruncatedRecordData()
    {
        var inputStates = new List<NetNode.MobStateSnapshot>
        {
            new NetNode.MobStateSnapshot(1, 10.0, 20.0, 1, 50, 50, "idle", "Zombie", "Level1", 1, 0.0, 0.0, 0.0)
        };

        Assert.True(MobWireBinary.TryBuildMobStatesBinary(inputStates, out var bytes));
        Assert.NotNull(bytes);

        // Truncate half of the byte buffer
        var truncated = new byte[bytes.Length / 2];
        Array.Copy(bytes, truncated, truncated.Length);

        var dest = new List<NetNode.MobStateSnapshot>();
        Assert.False(MobWireBinary.TryParseMobStatesBinary(truncated, dest));
        Assert.Empty(dest);
    }

    [Theory]
    // Host + 2 clients (1 alive, 1 stale) -> 2
    [InlineData(true, 1, false, new int[] { 1, 2, 3 }, new int[] { }, new int[] { 1, 2 }, 2)]
    // Client + Host downed -> 1
    [InlineData(false, 2, false, new int[] { 1, 2 }, new int[] { 1 }, new int[] { 1, 2 }, 1)]
    public void CombatSyncHelpers_ComputeExpectedExitPlayerCount_HandlesDownedAndStalePeers(
        bool isHost,
        int localId,
        bool isLocalDowned,
        int[] activePeerIds,
        int[] downedPeerIds,
        int[] freshPeerIds,
        int expected)
    {
        var downedSet = new HashSet<int>(downedPeerIds);
        var freshSet = new HashSet<int>(freshPeerIds);

        var result = CombatSyncHelpers.ComputeExpectedExitPlayerCount(
            isHost,
            localId,
            isLocalDowned,
            activePeerIds,
            peerId => downedSet.Contains(peerId),
            peerId => freshSet.Contains(peerId));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void BossStemCell_NetNode_HostAuthorityRejection()
    {
        var hostNode = new NetNode(1, NetRole.Host);

        // Host rejects client-originated BOSSRUNE packet
        var handledBossRune = hostNode.HandleLine("BOSSRUNE|5", senderId: 2, out var forwardLine1);
        Assert.True(handledBossRune);
        Assert.Null(forwardLine1);

        // Host rejects client-originated BOSSRUNE_UPDATE_CELLS packet
        var handledUpdate = hostNode.HandleLine("BOSSRUNE_UPDATE_CELLS|100|200|1|PrisonStart", senderId: 2, out var forwardLine2);
        Assert.True(handledUpdate);
        Assert.Null(forwardLine2);
    }

    [Fact]
    public void BossStemCell_TryParseInterBossRuneUpdateCellsPayload_ExtendedFormats()
    {
        // 5-part without uid: x | y | add | levelId | targetBc
        var parsed5 = NetNode.TryParseInterBossRuneUpdateCellsPayload("120.0|240.5|1|PrisonStart|2", out var ev5);
        Assert.True(parsed5);
        Assert.Equal(0, ev5.UserId);
        Assert.Equal(120.0, ev5.X);
        Assert.Equal(240.5, ev5.Y);
        Assert.True(ev5.Add);
        Assert.Equal("PrisonStart", ev5.LevelId);
        Assert.Equal(2, ev5.TargetBossCells);
        Assert.Null(ev5.MaxAllowedBossCells);

        // 6-part without uid: x | y | add | levelId | targetBc | maxAllowed
        var parsed6 = NetNode.TryParseInterBossRuneUpdateCellsPayload("120.0|240.5|1|PrisonStart|4|3", out var ev6);
        Assert.True(parsed6);
        Assert.Equal(0, ev6.UserId);
        Assert.Equal(3, ev6.TargetBossCells); // Clamped to maxAllowed 3
        Assert.Equal(3, ev6.MaxAllowedBossCells);

        // 7-part with uid: uid | x | y | add | levelId | targetBc | maxAllowed
        var parsed7 = NetNode.TryParseInterBossRuneUpdateCellsPayload("2|120.0|240.5|0|PrisonStart|5|4", out var ev7);
        Assert.True(parsed7);
        Assert.Equal(2, ev7.UserId);
        Assert.False(ev7.Add);
        Assert.Equal(4, ev7.TargetBossCells); // Clamped to maxAllowed 4
        Assert.Equal(4, ev7.MaxAllowedBossCells);
    }

    [Fact]
    public void BossStemCell_RunLaunchCommit_ReconcilesStaleRemoteBossRune()
    {
        RunLaunchCoordinator.ResetForTesting(NetRole.Client);
        GameDataSync.ResetBossRuneStateForTesting();

        try
        {
            // Simulate client having stale BC 1
            GameDataSync.ReceiveBossRune("1|1");
            Assert.True(GameDataSync.TryGetRemoteBossRune(out var bc));
            Assert.Equal(1, bc);

            // Host commits run launch with BC 3
            var descriptor = new DeadCellsMultiplayerMod.PortableCore.RunLaunchDescriptor(
                Guid.NewGuid(),
                RunId: 100,
                Sequence: 2,
                ProtocolVersion: DeadCellsMultiplayerMod.BuildInfo.NetworkProtocolVersion,
                RunSeed: 54321,
                LaunchKind: "normal",
                InitialLevelId: "PrisonStart",
                InitialLevelSeed: 123.0,
                Difficulty: 3,
                BossCells: 3,
                BossRush: false,
                DlcFlags: 0,
                SaveSlot: 0,
                BossRushSeed: 0,
                LevelGenSeed: 0,
                BossRushTier: 0,
                Route: string.Empty,
                BossSequence: string.Empty,
                Modifiers: 0,
                TargetArena: string.Empty);

            var commitWire = DeadCellsMultiplayerMod.RunLaunchWireCodec.BuildCommitLine(descriptor)["RUNCOMMIT|".Length..];
            LobbySession.ReceiveRunLaunchCommitPayload(commitWire);

            // Reconciled to descriptor's 3 BC
            Assert.True(GameDataSync.TryGetRemoteBossRune(out bc));
            Assert.Equal(3, bc);
        }
        finally
        {
            GameDataSync.ResetBossRuneStateForTesting();
            RunLaunchCoordinator.ResetForTesting(NetRole.None);
        }
    }

    [Theory]
    [InlineData(true, 2.5, 2.5, true)]    // Reviver downed exactly at boundary (2.5s): honor revive
    [InlineData(true, 2.6, 2.5, false)]   // Reviver downed 2.6s ago (> grace): reject stale revive
    public void CombatSyncHelpers_CanHonorSimultaneousRevive_HandlesRaceConditions(
        bool reviverIsDowned,
        double reviverDownedDurationSec,
        double simultaneousGraceSec,
        bool expectedResult)
    {
        var result = CombatSyncHelpers.CanHonorSimultaneousRevive(reviverIsDowned, reviverDownedDurationSec, simultaneousGraceSec);
        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public void NetNode_ClearPendingStateForRunRestart_ClearsAllPendingQueuesIdempotently()
    {
        var node = new NetNode(1, NetRole.Host);

        // Populate various pending queues via HandleLine
        node.HandleLine("PDOWN|2|1|100|200|PrisonStart", senderId: 2, out _);
        node.HandleLine("PREVIVE|2|1", senderId: 2, out _);
        node.HandleLine("INTER_COLLECTOR_LOCK|2|1|Collector|1", senderId: 2, out _);
        node.HandleLine("INTER_COLLECTOR_DEPOSIT|2|1|Sword|5", senderId: 2, out _);
        node.HandleLine("INTER_BLUEPRINT_UNLOCK|2|1|Bow", senderId: 2, out _);
        node.HandleLine("INTER_FORGE_UPGRADE|2|1|1|10", senderId: 2, out _);

        // Client node for exit commit and boss rune cell updates
        var clientNode = new NetNode(2, NetRole.Client);
        clientNode.HandleLine("EXITCOMMIT|1|PrisonStart|Promenade|100|200", senderId: 1, out _);
        clientNode.HandleLine("BOSSRUNE_UPDATE_CELLS|100|200|1|PrisonStart", senderId: 1, out _);

        // Clear for run restart
        node.ClearPendingStateForRunRestart();
        clientNode.ClearPendingStateForRunRestart();

        // Assert host queues are completely cleared
        Assert.False(node.TryConsumePlayerDownStates(out _));
        Assert.False(node.TryConsumePlayerReviveRequests(out _));
        Assert.False(node.TryConsumeInterCollectorLockEvents(out _));
        Assert.False(node.TryConsumeInterCollectorDepositEvents(out _));
        Assert.False(node.TryConsumeInterBlueprintUnlockEvents(out _));
        Assert.False(node.TryConsumeInterForgeUpgradeEvents(out _));

        // Assert client queues are completely cleared
        Assert.False(clientNode.TryConsumeExitTransitionCommits(out _));
        Assert.False(clientNode.TryConsumeBossRuneUpdateCells(out _));

        // Idempotent calls must execute cleanly without exceptions
        node.ClearPendingStateForRunRestart();
        node.ClearPendingStateForRunRestart();
        clientNode.ClearPendingStateForRunRestart();

        Assert.False(node.TryConsumePlayerDownStates(out _));
        Assert.False(clientNode.TryConsumeExitTransitionCommits(out _));
    }
}

