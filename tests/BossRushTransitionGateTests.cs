using System;
using DeadCellsMultiplayerMod.Network;
using DeadCellsMultiplayerMod.PortableCore;
using Xunit;

namespace DeadCellsMultiplayerMod.ProtocolReplay.Tests;

public sealed class BossRushTransitionGateTests : IDisposable
{
    public BossRushTransitionGateTests()
    {
        Cleanup();
    }

    public void Dispose()
    {
        Cleanup();
    }

    private static void Cleanup()
    {
        LobbySession.NetRef = null;
        LobbySession.ClearPrecommittedHostBossRushLaunchForTesting();
        RunLaunchCoordinator.ResetForTesting(NetRole.None);
    }

    [Theory]
    [InlineData("4", true, 4)]
    [InlineData("BossRushDoorLH", true, 3)]
    [InlineData("Tier 5", false, 5)]
    public void TryParseBossRushDoorIndex_ParsesAndValidates(string? input, bool expectedSuccess, int expectedIndex)
    {
        var success = LobbySession.TryParseBossRushDoorIndex(input, out var index);
        Assert.Equal(expectedSuccess, success);
        Assert.Equal(expectedIndex, index);
    }

    [Theory]
    [InlineData("BossRushZone", true)]
    [InlineData("BossRushZone|Injected", false)]
    public void IsValidBossRushLevelId_ValidatesLevelIdentifiers(string? levelId, bool expected)
    {
        var result = LobbySession.IsValidBossRushLevelId(levelId);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void HostWatchdog_RemoteDisconnect_RecoversCommittedLaunchPhase()
    {
        RunLaunchCoordinator.ResetForTesting(NetRole.Host);

        // Commit host launch to put session into LaunchCommitted
        var desc = RunLaunchCoordinator.CreateOrReuseHostDescriptor(
            seed: 7777,
            sequence: 1,
            launchKind: "dc.LaunchMode+BossRush",
            initialLevelId: "BossRushZone",
            initialLevelSeed: null,
            difficulty: 0,
            bossCells: 0,
            dlcFlags: 0,
            saveSlot: 0,
            bossRushSeed: 7777,
            bossRushTier: 1,
            route: "BossRushDoorSH");

        Assert.Equal(CoopSessionPhase.LaunchCommitted, RunLaunchCoordinator.CurrentPhase);

        // Remote peer drops connection during launch loading
        RunLaunchCoordinator.OnRemoteDisconnected(NetRole.Host);

        // Host session phase must cleanly return to Lobby instead of staying frozen in LaunchCommitted
        Assert.Equal(CoopSessionPhase.Lobby, RunLaunchCoordinator.CurrentPhase);
    }

    [Fact]
    public void HostWatchdog_RemoteDisconnect_DuringLoadingLevel_RecoversSessionPhase()
    {
        RunLaunchCoordinator.ResetForTesting(NetRole.Host);

        // Commit host launch
        var desc = RunLaunchCoordinator.CreateOrReuseHostDescriptor(
            seed: 8888,
            sequence: 1,
            launchKind: "dc.LaunchMode+BossRush",
            initialLevelId: "BossRushZone",
            initialLevelSeed: null,
            difficulty: 0,
            bossCells: 0,
            dlcFlags: 0,
            saveSlot: 0,
            bossRushSeed: 8888,
            bossRushTier: 1,
            route: "BossRushDoorSH");

        // Advance to LoadingLevel phase
        RunLaunchCoordinator.MarkLocalLoading(1, "host_start_loading");
        Assert.Equal(CoopSessionPhase.LoadingLevel, RunLaunchCoordinator.CurrentPhase);

        // Remote peer drops connection during active loading
        RunLaunchCoordinator.OnRemoteDisconnected(NetRole.Host);

        // Host session phase must cleanly return to Lobby instead of staying frozen in LoadingLevel
        Assert.Equal(CoopSessionPhase.Lobby, RunLaunchCoordinator.CurrentPhase);
    }

    [Fact]
    public void HostWatchdog_ClientRejectionAck_AbortsCommittedLaunch()
    {
        RunLaunchCoordinator.ResetForTesting(NetRole.Host);

        var desc = RunLaunchCoordinator.CreateOrReuseHostDescriptor(
            seed: 9999,
            sequence: 1,
            launchKind: "dc.LaunchMode+BossRush",
            initialLevelId: "BossRushZone",
            initialLevelSeed: null,
            difficulty: 0,
            bossCells: 0,
            dlcFlags: 0,
            saveSlot: 0,
            bossRushSeed: 9999,
            bossRushTier: 1,
            route: "BossRushDoorSH");

        Assert.Equal(CoopSessionPhase.LaunchCommitted, RunLaunchCoordinator.CurrentPhase);

        // Client rejects launch with error
        var rejectionAck = new RunLaunchAck(
            desc.SessionId,
            desc.RunId,
            desc.Sequence,
            false,
            "missing required Boss Rush DLC");

        RunLaunchCoordinator.ReceiveHostAck(rejectionAck);

        // Host must abort committed launch and return to Lobby immediately
        Assert.Equal(CoopSessionPhase.Lobby, RunLaunchCoordinator.CurrentPhase);
    }
}
