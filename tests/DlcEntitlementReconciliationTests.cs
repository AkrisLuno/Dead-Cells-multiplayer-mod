using System;
using System.Collections.Generic;
using DeadCellsMultiplayerMod.PortableCore;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace DeadCellsMultiplayerMod.ProtocolReplay.Tests;

public sealed class DlcEntitlementReconciliationTests : IDisposable
{
    public DlcEntitlementReconciliationTests()
    {
        DlcEntitlementManager.Reset();
    }

    public void Dispose()
    {
        DlcEntitlementManager.Reset();
        RunLaunchCoordinator.ResetForTesting(NetRole.None);
    }

    [Theory]
    // Rise of the Giant
    [InlineData("Cavern", DlcEntitlementManager.DlcMask.RiseOfTheGiant, "RiseOfTheGiant", "Rise of the Giant")]
    // The Bad Seed
    [InlineData("Greenhouse", DlcEntitlementManager.DlcMask.TheBadSeed, "TheBadSeed", "The Bad Seed")]
    // Fatal Falls
    [InlineData("Tumulus", DlcEntitlementManager.DlcMask.FatalFalls, "FatalFalls", "Fatal Falls")]
    // The Queen and the Sea
    [InlineData("Shipwreck", DlcEntitlementManager.DlcMask.TheQueenAndTheSea, "TheQueenAndTheSea", "The Queen and the Sea")]
    // Return to Castlevania ("Purple")
    [InlineData("PurpleGarden", DlcEntitlementManager.DlcMask.ReturnToCastlevania, "Purple", "Return to Castlevania")]
    public void RequiresDlc_OfficialDlcLevels_ResolvesCorrectDlcPack(
        string levelId,
        DlcEntitlementManager.DlcMask expectedMask,
        string expectedInternalId,
        string expectedDisplayName)
    {
        var requires = DlcEntitlementManager.RequiresDlc(levelId, out var mask, out var internalId, out var dispName);
        Assert.True(requires);
        Assert.Equal(expectedMask, mask);
        Assert.Equal(expectedInternalId, internalId);
        Assert.Equal(expectedDisplayName, dispName);
    }

    [Fact]
    public void RunLaunchCoordinator_DLCValidation_RejectsInaccessibleBiomes()
    {
        RunLaunchCoordinator.ResetForTesting(NetRole.Client);

        // Client has 0 DLCs installed
        DlcEntitlementManager.SetLocalDlcMaskOverride(0);

        // Host launches into base game level (PrisonStart) -> Allowed!
        var baseGameDescriptor = new RunLaunchDescriptor(
            Guid.NewGuid(),
            RunId: 100,
            Sequence: 1,
            ProtocolVersion: BuildInfo.NetworkProtocolVersion,
            RunSeed: 12345,
            LaunchKind: "normal",
            InitialLevelId: "PrisonStart",
            InitialLevelSeed: 1.0,
            Difficulty: 0,
            BossCells: 0,
            BossRush: false,
            DlcFlags: (ulong)DlcEntitlementManager.DlcMask.All,
            SaveSlot: 0,
            BossRushSeed: 0,
            LevelGenSeed: 0,
            BossRushTier: 0,
            Route: string.Empty,
            BossSequence: string.Empty,
            Modifiers: 0,
            TargetArena: "PrisonStart");

        var acceptedBase = RunLaunchCoordinator.TryAcceptRemoteDescriptor(baseGameDescriptor, out var ackBase, out _);
        Assert.True(acceptedBase);
        Assert.True(ackBase.Accepted);
        Assert.Empty(ackBase.Error);

        // Host launches into Castlevania level (PurpleGarden) -> Client lacks DLC, must cleanly reject!
        RunLaunchCoordinator.ResetForTesting(NetRole.Client);
        var castlevaniaDescriptor = new RunLaunchDescriptor(
            Guid.NewGuid(),
            RunId: 101,
            Sequence: 2,
            ProtocolVersion: BuildInfo.NetworkProtocolVersion,
            RunSeed: 54321,
            LaunchKind: "normal",
            InitialLevelId: "PurpleGarden",
            InitialLevelSeed: 1.0,
            Difficulty: 0,
            BossCells: 0,
            BossRush: false,
            DlcFlags: (ulong)DlcEntitlementManager.DlcMask.ReturnToCastlevania,
            SaveSlot: 0,
            BossRushSeed: 0,
            LevelGenSeed: 0,
            BossRushTier: 0,
            Route: string.Empty,
            BossSequence: string.Empty,
            Modifiers: 0,
            TargetArena: "PurpleGarden");

        var acceptedCastlevania = RunLaunchCoordinator.TryAcceptRemoteDescriptor(castlevaniaDescriptor, out var ackCastlevania, out _);
        Assert.False(acceptedCastlevania);
        Assert.False(ackCastlevania.Accepted);
        Assert.Contains("Return to Castlevania", ackCastlevania.Error, StringComparison.OrdinalIgnoreCase);

        // Grant Castlevania to client -> Client now accepts!
        DlcEntitlementManager.SetLocalDlcMaskOverride((int)DlcEntitlementManager.DlcMask.ReturnToCastlevania);
        var acceptedWithDlc = RunLaunchCoordinator.TryAcceptRemoteDescriptor(castlevaniaDescriptor, out var ackWithDlc, out _);
        Assert.True(acceptedWithDlc);
        Assert.True(ackWithDlc.Accepted);
    }

    [Fact]
    public void IsLevelAccessibleForParty_ValidatesBothLocalAndRemotePeers()
    {
        var party = new[] { 1, 2 }; // 1 = Host, 2 = Client

        // Case 1: Local player (1) lacks DLC -> missingPeerId = 1
        DlcEntitlementManager.SetLocalDlcMaskOverride(0);
        DlcEntitlementManager.SetPeerDlcMask(2, (int)DlcEntitlementManager.DlcMask.All);
        Assert.False(DlcEntitlementManager.IsLevelAccessibleForParty(party, 1, "PurpleGarden", out var missing1));
        Assert.Equal(1, missing1);

        // Case 2: Local player (1) has DLC, but Client (2) lacks DLC -> missingPeerId = 2
        DlcEntitlementManager.SetLocalDlcMaskOverride((int)DlcEntitlementManager.DlcMask.All);
        DlcEntitlementManager.SetPeerDlcMask(2, 0);
        Assert.False(DlcEntitlementManager.IsLevelAccessibleForParty(party, 1, "PurpleGarden", out var missing2));
        Assert.Equal(2, missing2);

        // Case 3: Both possess DLC -> Returns true
        DlcEntitlementManager.SetPeerDlcMask(2, (int)DlcEntitlementManager.DlcMask.ReturnToCastlevania);
        Assert.True(DlcEntitlementManager.IsLevelAccessibleForParty(party, 1, "PurpleGarden", out var missing3));
        Assert.Equal(0, missing3);

        // Case 4: Base game level -> Returns true regardless of DLC
        DlcEntitlementManager.SetLocalDlcMaskOverride(0);
        DlcEntitlementManager.SetPeerDlcMask(2, 0);
        Assert.True(DlcEntitlementManager.IsLevelAccessibleForParty(party, 1, "PrisonStart", out var missing4));
        Assert.Equal(0, missing4);
    }

    [Fact]
    public void CreateOrReuseHostDescriptor_ValidatesHostLocalEntitlement()
    {
        RunLaunchCoordinator.ResetForTesting(NetRole.Host);

        // Host lacks Castlevania
        DlcEntitlementManager.SetLocalDlcMaskOverride(0);

        // Starting in Castlevania level should throw
        var ex = Assert.Throws<InvalidOperationException>(() =>
            RunLaunchCoordinator.CreateOrReuseHostDescriptor(
                seed: 12345,
                sequence: 1,
                launchKind: "normal",
                initialLevelId: "PurpleGarden",
                initialLevelSeed: null,
                difficulty: 0,
                bossCells: 0,
                dlcFlags: 0,
                saveSlot: 0));

        Assert.Contains("Return to Castlevania", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Grant Castlevania -> Succeeds!
        DlcEntitlementManager.SetLocalDlcMaskOverride((int)DlcEntitlementManager.DlcMask.ReturnToCastlevania);
        var descriptor = RunLaunchCoordinator.CreateOrReuseHostDescriptor(
            seed: 12345,
            sequence: 1,
            launchKind: "normal",
            initialLevelId: "PurpleGarden",
            initialLevelSeed: null,
            difficulty: 0,
            bossCells: 0,
            dlcFlags: (ulong)DlcEntitlementManager.DlcMask.ReturnToCastlevania,
            saveSlot: 0);

        Assert.NotNull(descriptor);
        Assert.Equal("PurpleGarden", descriptor.InitialLevelId);
    }
}
