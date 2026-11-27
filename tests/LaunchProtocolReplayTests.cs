using System.Net;
using System.Net.Sockets;
using DeadCellsMultiplayerMod.PortableCore;
using DeadCellsMultiplayerMod.Network;
using DeadCellsMultiplayerMod.Tools;
using Serilog;
using Xunit;

namespace DeadCellsMultiplayerMod.ProtocolReplay.Tests;

public sealed class LaunchProtocolReplayTests
{
    [Fact]
    public void CommitLine_RoundTripsDescriptor()
    {
        var descriptor = CreateDescriptor();

        var line = RunLaunchWireCodec.BuildCommitLine(descriptor);
        Assert.StartsWith("RUNCOMMIT|", line, StringComparison.Ordinal);
        Assert.True(
            RunLaunchWireCodec.TryDecodeCommit(line["RUNCOMMIT|".Length..], out var decoded, out var error),
            error);
        Assert.NotNull(decoded);
        Assert.Equal(descriptor, decoded);
    }

    [Fact]
    public void ReplayRejectsStaleAndIllegalTransitions()
    {
        var state = new CoopSessionStateMachine();

        Assert.True(state.TryTransition(CoopSessionPhase.Lobby, 1, "connected", out var error), error);
        Assert.True(state.TryTransition(CoopSessionPhase.LaunchCommitted, 2, "commit", out error), error);
        Assert.False(state.TryTransition(CoopSessionPhase.Playing, 2, "stale", out error));
        Assert.Contains("Stale transition", error, StringComparison.Ordinal);
        Assert.False(state.TryTransition(CoopSessionPhase.Disconnected, 3, "illegal", out error));
        Assert.Contains("Illegal co-op session transition", error, StringComparison.Ordinal);
        Assert.Equal(CoopSessionPhase.LaunchCommitted, state.Phase);
    }

    [Fact]
    public void MalformedAndOversizedCommitPayloadsAreRejected()
    {
        Assert.False(RunLaunchWireCodec.TryDecodeCommit("not-base64", out _, out var malformedError));
        Assert.False(string.IsNullOrWhiteSpace(malformedError));

        var oversized = new string('A', 64 * 1024 + 1);
        Assert.False(RunLaunchWireCodec.TryDecodeCommit(oversized, out _, out var oversizedError));
        Assert.Contains("exceeds", oversizedError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RealtimeBudgetDropsOnlyWhenWindowIsFull()
    {
        var budget = new NetPacketBudget(maxBytes: 10, windowMilliseconds: 1000);

        Assert.True(budget.TryConsume(6));
        Assert.False(budget.TryConsume(5));
        Assert.Equal(1, budget.DroppedPackets);
        Assert.Equal(5, budget.DroppedBytes);
    }

    [Fact]
    public void LifecycleTrackerInvalidatesOldGenerationOnStop()
    {
        var tracker = new LifecycleTracker("replay");
        var generation = tracker.Start();

        Assert.True(tracker.IsCurrent(generation));
        Assert.True(tracker.TryBeginStop());
        Assert.False(tracker.IsCurrent(generation));

        tracker.MarkDisposed();
        Assert.Equal(LifecycleState.Disposed, tracker.Snapshot.State);
    }

    [Fact]
    public void MobLifecycleLedgerIsIdempotentPerGeneration()
    {
        var ledger = new MobLifecycleLedger();

        ledger.MarkActive(10, 7);
        Assert.False(ledger.IsDead(10, 7));

        ledger.MarkDead(10, 7);
        Assert.True(ledger.IsDead(10, 7));

        // A stale active observation must not resurrect a confirmed death.
        ledger.MarkActive(10, 7);
        Assert.True(ledger.IsDead(10, 7));

        // The same NetId is valid again only in a new level generation.
        Assert.False(ledger.IsDead(11, 7));
    }

    [Theory]
    [InlineData("255.255.255.255")]
    [InlineData("192.168.1.2")]
    public void RoomCode_RoundTrips_IPv4Addresses(string ip)
    {
        var address = IPAddress.Parse(ip);
        var code = LanRoomCode.Format(address, LanRoomCode.DefaultPort);
        Assert.Matches("^[0-9A-Z]+$", code);

        Assert.True(LanRoomCode.TryParse(code, out var endpoint));
        Assert.Equal(new IPEndPoint(address, 1234), endpoint);
    }

    [Fact]
    public void RoomCode_HandlesPrefixAndCaseAndWhitespace()
    {
        var address = IPAddress.Parse("100.64.0.20");
        var code = LanRoomCode.Format(address, 4321);
        Assert.True(LanRoomCode.TryParse($"  DC-{code.ToLowerInvariant()}  ", out var endpoint));
        Assert.Equal(new IPEndPoint(address, 4321), endpoint);
    }

    [Fact]
    public void RoomCode_DirectIpAndPortParsing()
    {
        Assert.True(LanRoomCode.TryParse("100.64.0.20:4321", out var endpoint));
        Assert.Equal(IPEndPoint.Parse("100.64.0.20:4321"), endpoint);

        Assert.True(LanRoomCode.TryParse("  192.168.1.50  ", out endpoint));
        Assert.Equal(IPEndPoint.Parse("192.168.1.50:1234"), endpoint);
    }

    [Theory]
    [InlineData("ZZZZZZZ")]
    [InlineData("127.0.0.1:65536")]
    [InlineData("::1")]
    public void RoomCode_RejectsInvalidAddressesAndPorts(string? input)
    {
        Assert.False(LanRoomCode.TryParse(input, out _));
    }

    [Fact]
    public async Task RoomCode_ConnectsToHostOnItsActualTcpPort()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var logger = new LoggerConfiguration().CreateLogger();
        using var host = NetNode.CreateHost(logger, new IPEndPoint(IPAddress.Loopback, 0));
        using var client = new TcpClient(AddressFamily.InterNetwork);
        var code = LanRoomCode.Format(IPAddress.Loopback, host.ListenerEndpoint!.Port);
        Assert.True(LanRoomCode.TryParse(code, out var endpoint));

        await client.ConnectAsync(endpoint, timeout.Token);
        using var reader = new StreamReader(client.GetStream());
        Assert.Equal($"WELCOME|{BuildInfo.NetworkProtocolVersion}|{BuildInfo.Version}", await reader.ReadLineAsync(timeout.Token));
        Assert.Equal("ID|2", await reader.ReadLineAsync(timeout.Token));
    }

    private static RunLaunchDescriptor CreateDescriptor()
    {
        return new RunLaunchDescriptor(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            RunId: 7,
            Sequence: 3,
            ProtocolVersion: 17,
            RunSeed: 12345,
            LaunchKind: "normal",
            InitialLevelId: "PrisonStart",
            InitialLevelSeed: 42.5,
            Difficulty: 2,
            BossCells: 2,
            BossRush: false,
            DlcFlags: 0,
            SaveSlot: 1,
            BossRushSeed: 0,
            LevelGenSeed: 0,
            BossRushTier: 0,
            Route: string.Empty,
            BossSequence: string.Empty,
            Modifiers: 0,
            TargetArena: "PrisonStart");
    }
}
