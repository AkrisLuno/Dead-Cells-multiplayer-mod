using System.Net;
using Xunit;

namespace DeadCellsMultiplayerMod;

public sealed class LanSessionTests
{
    [Fact]
    public async Task PeersShareSeedAndStateAcrossReconnect()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listening = new TaskCompletionSource<IPEndPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = new LanSession(true, new IPEndPoint(IPAddress.Loopback, 0), message =>
        {
            const string prefix = "LAN listening on ";
            if (message.StartsWith(prefix, StringComparison.Ordinal))
                listening.TrySetResult(IPEndPoint.Parse(message[prefix.Length..].TrimEnd('.')));
        });
        var address = await listening.Task.WaitAsync(deadline.Token);
        var partner = Guid.NewGuid();
        var ready = new LobbyState(true, false);
        var started = new LobbyState(true, true);
        using var client = new LanSession(false, address, _ => { }, partner);
        var fromHost = new PlayerState("PrisonStart", 120, 40, 1, "run", 111) { Layout = 42 };
        var fromClient = new PlayerState("PrisonStart", 100, 40, -1, "idle", 111) { Layout = 42 };
        var door = new DoorIdentity("PrisonStart", 111, 4, 2, "PrisonCourtyard");
        var request = new ExitDecision(door, false);
        var commit = new ExitDecision(door, true);
        var shop = new ShopKey("PrisonStart", 111, 2, 6, 2);
        var reroll = new RerollRequest(shop, 0);
        var stock = new ShopStock(shop, 1, 1, false,
            [new BoothStock(9, 2, 250, new ItemStock(0, "BloodSword", 2, 0, ["Quality"], []), null)]);
        var level = new LootLevel("PrisonStart", 111, 42);
        var batch = new DropBatch(1, 6, 2, 2.0, [new MobDrop(DropKind.Gold, 100), new MobDrop(DropKind.Cell)]);
        var frame = new DropFrame(level, [batch]);
        var receipt = new DropReceipt(level, 1);
        host.Publish(fromHost, commit, started, shopStocks: [stock], dropFrame: frame);
        client.Publish(fromClient, request, ready, reroll: reroll, dropReceipt: receipt);
        await WaitUntil(() => host.Remote?.Player == fromClient && client.Remote?.Player == fromHost, deadline.Token);
        Assert.Equal(request, host.Remote?.Exit);
        Assert.Equal(commit, client.Remote?.Exit);
        Assert.Equal(ready, host.Remote?.Lobby);
        Assert.Equal(started, client.Remote?.Lobby);
        Assert.Equal(reroll, host.Remote?.Reroll);
        Assert.Equal(receipt, host.Remote?.DropReceipt);
        Assert.Equal(batch.Drops, Assert.Single(client.Remote!.DropFrame!.Batches).Drops);
        var received = Assert.Single(client.Remote!.ShopStocks!);
        Assert.Equal(1, received.Revision);
        Assert.Equal(250, Assert.Single(received.Booths).Price);
        Assert.Equal("Quality", Assert.Single(received.Booths[0].Item.Affixes));
        Assert.True(host.TryReservePeerForRun());
        var seed = host.RunSeed;
        Assert.True(seed > 0);
        Assert.Equal(seed, client.RunSeed);

        client.Dispose();
        await WaitUntil(() => host.RunSeed == 0 && host.Remote is null, deadline.Token);
        using (var stranger = new LanSession(false, address, _ => { }))
        {
            await WaitUntil(() => stranger.ConnectionError is not null, deadline.Token);
            Assert.Equal(0, stranger.RunSeed);
            Assert.Null(host.Remote);
        }
        using var rejoined = new LanSession(false, address, _ => { }, partner);
        await WaitUntil(() => rejoined.Remote?.Player == fromHost, deadline.Token);
        Assert.Null(host.Remote?.Player);
        Assert.Equal(seed, rejoined.RunSeed);
        Assert.Equal(batch.Sequence, Assert.Single(rejoined.Remote!.DropFrame!.Batches).Sequence);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HostRejectsClientClaimingAuthority(bool startsRun)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listening = new TaskCompletionSource<IPEndPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
        var rejected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = new LanSession(true, new IPEndPoint(IPAddress.Loopback, 0), message =>
        {
            const string prefix = "LAN listening on ";
            if (message.StartsWith(prefix, StringComparison.Ordinal))
                listening.TrySetResult(IPEndPoint.Parse(message[prefix.Length..].TrimEnd('.')));
            if (message.Contains("Invalid peer snapshot.", StringComparison.Ordinal))
                rejected.TrySetResult();
        });
        using var client = new LanSession(false, await listening.Task.WaitAsync(deadline.Token), _ => { });
        var drop = startsRun ? null : new DropFrame(new LootLevel("PrisonStart", 111, 42),
            [new DropBatch(1, 6, 2, 2.0, [new MobDrop(DropKind.Gold, 100)])]);
        client.Publish(null, lobby: new LobbyState(true, startsRun), dropFrame: drop);
        await rejected.Task.WaitAsync(deadline.Token);
        Assert.Null(host.Remote);
        client.Dispose();
        using var replacement = new LanSession(false, await listening.Task.WaitAsync(deadline.Token), _ => { });
        replacement.Publish(null, lobby: new LobbyState(true, false));
        await WaitUntil(() => host.Remote?.Lobby?.Ready == true, deadline.Token);
    }

    private static async Task WaitUntil(Func<bool> condition, CancellationToken cancellation)
    {
        while (!condition())
            await Task.Delay(20, cancellation);
    }
}
