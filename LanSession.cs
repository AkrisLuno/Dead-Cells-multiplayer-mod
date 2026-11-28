using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DeadCellsMultiplayerMod;

internal sealed class LanSession : IDisposable
{
    private const int ProtocolVersion = 7;
    private const int MaximumFrameBytes = 256 * 1024;
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50);
    private readonly CancellationTokenSource shutdown = new();
    private readonly Action<string> report;
    private readonly Task worker;
    private readonly Guid playerId;
    private readonly object admission = new();
    private Connection? connection;
    private PeerState? local;
    private Guid reservedPeer;
    private HostIdentity? pinnedHost;
    private string? connectionError;
    private int disposed;

    public bool IsHost { get; }
    public int RunSeed => Volatile.Read(ref connection)?.Seed ?? 0;
    public PeerState? Remote => Volatile.Read(ref connection)?.Latest?.State;
    public string? ConnectionError => Volatile.Read(ref connectionError);

    public LanSession(bool isHost, IPEndPoint endpoint, Action<string> report, Guid? playerId = null)
    {
        IsHost = isHost;
        this.report = report;
        this.playerId = playerId ?? Guid.NewGuid();
        worker = isHost ? ListenAsync(endpoint, shutdown.Token) : JoinAsync(endpoint, shutdown.Token);
    }

    public void Publish(PlayerState? state, ExitDecision? exit = null, LobbyState? lobby = null,
        RerollRequest? reroll = null, ShopStock[]? shopStocks = null,
        DropFrame? dropFrame = null, DropReceipt? dropReceipt = null)
        => Volatile.Write(ref local, new PeerState(state, new PeerControls(exit, lobby, reroll, shopStocks, dropFrame, dropReceipt)));

    public (int Seed, Guid PlayerId, ReceivedState? Snapshot) ObservePeer()
    {
        var peer = Volatile.Read(ref connection);
        return peer is null ? default : (peer.Seed, peer.PlayerId, peer.Latest);
    }

    public bool TryReservePeerForRun(Guid peerId)
    {
        lock (admission)
        {
            if (connection is not { Latest: { IsFresh: true, State.Lobby.Ready: true } } peer || peer.PlayerId != peerId)
                return false;
            reservedPeer = peer.PlayerId;
            return true;
        }
    }

    public void ReleasePeerReservation()
    {
        lock (admission)
            reservedPeer = Guid.Empty;
    }

    public void PinHostForRun(int seed, Guid id) => Volatile.Write(ref pinnedHost, new HostIdentity(seed, id));

    private async Task ListenAsync(IPEndPoint endpoint, CancellationToken cancellation)
    {
        var listener = new TcpListener(endpoint);
        try
        {
            listener.Start();
            var seed = Random.Shared.Next(1, int.MaxValue);
            report($"LAN listening on {listener.LocalEndpoint}.");
            while (!cancellation.IsCancellationRequested)
            {
                using var peer = await listener.AcceptTcpClientAsync(cancellation).ConfigureAwait(false);
                await RunPeerAsync(peer, seed, cancellation).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (SocketException error)
        {
            Volatile.Write(ref connectionError, $"Could not open LAN port {endpoint.Port}");
            report($"LAN listener failed: {error.Message}");
        }
        finally { listener.Stop(); }
    }

    private async Task JoinAsync(IPEndPoint endpoint, CancellationToken cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                using var peer = new TcpClient(endpoint.AddressFamily);
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                    timeout.CancelAfter(TimeSpan.FromSeconds(3));
                    await peer.ConnectAsync(endpoint, timeout.Token).ConfigureAwait(false);
                    await RunPeerAsync(peer, 0, cancellation).ConfigureAwait(false);
                }
                catch (SocketException) { }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { }
                await Task.Delay(TimeSpan.FromSeconds(1), cancellation).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }

    private async Task RunPeerAsync(TcpClient peer, int seed, CancellationToken cancellation)
    {
        using var peerLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var token = peerLifetime.Token;
        try
        {
            peer.NoDelay = true;
            using var stream = peer.GetStream();
            Connection connected;
            using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                handshake.CancelAfter(TimeSpan.FromSeconds(5));
                Hello reply;
                if (IsHost)
                {
                    reply = await ReadAsync<Hello>(stream, handshake.Token).ConfigureAwait(false);
                    Guid reserved;
                    lock (admission)
                        reserved = reservedPeer;
                    if (reply.Version != ProtocolVersion)
                    {
                        await SendAsync(stream, new Hello(ProtocolVersion, 0, playerId), handshake.Token)
                            .ConfigureAwait(false);
                        return;
                    }
                    if (reply.PlayerId == Guid.Empty || reply.Seed != 0)
                        throw new InvalidDataException("Incompatible LAN handshake.");
                    var occupied = reserved != Guid.Empty && reserved != reply.PlayerId;
                    await SendAsync(stream, new Hello(ProtocolVersion, occupied ? 0 : seed, playerId), handshake.Token)
                        .ConfigureAwait(false);
                    if (occupied)
                        return;
                }
                else
                {
                    await SendAsync(stream, new Hello(ProtocolVersion, 0, playerId), handshake.Token).ConfigureAwait(false);
                    reply = await ReadAsync<Hello>(stream, handshake.Token).ConfigureAwait(false);
                    var expected = Volatile.Read(ref pinnedHost);
                    if (reply.Version != ProtocolVersion || reply.PlayerId == Guid.Empty ||
                        reply.Seed <= 0 || (expected is not null && (reply.Seed != expected.Seed || reply.PlayerId != expected.Id)))
                    {
                        Volatile.Write(ref connectionError, reply.Version == ProtocolVersion && reply.Seed == 0
                            ? "Host already has a co-op partner" : "Host session or mod version changed");
                        return;
                    }
                    seed = reply.Seed;
                }
                connected = new Connection(seed, reply.PlayerId);
            }
            Volatile.Write(ref connectionError, null);
            lock (admission)
                Volatile.Write(ref connection, connected);
            report($"LAN connected. Role={(IsHost ? "host" : "client")} Seed={seed}.");

            var sending = PublishChangesAsync(stream, token);
            var receiving = ReceiveChangesAsync(stream, connected, token);
            await Task.WhenAny(sending, receiving).ConfigureAwait(false);
            peerLifetime.Cancel();
            await Task.WhenAll(sending, receiving).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (OperationCanceledException) { report("LAN handshake timed out."); }
        catch (Exception error) when (error is IOException or InvalidDataException or SocketException or JsonException)
        {
            report($"LAN peer closed: {error.Message}");
        }
        finally
        {
            lock (admission)
                Volatile.Write(ref connection, null);
        }
    }

    private async Task PublishChangesAsync(NetworkStream stream, CancellationToken cancellation)
    {
        using var timer = new PeriodicTimer(TickInterval);
        PeerControls? sent = null;
        while (await timer.WaitForNextTickAsync(cancellation).ConfigureAwait(false))
        {
            var snapshot = Volatile.Read(ref local);
            var controls = snapshot?.Controls ?? PeerControls.Empty;
            // TCP keeps these ordered, so the receiver can retain unchanged world state.
            await SendAsync(stream, new WireState(snapshot?.Player, controls == sent ? null : controls), cancellation)
                .ConfigureAwait(false);
            sent = controls;
        }
    }

    private async Task ReceiveChangesAsync(NetworkStream stream, Connection connected, CancellationToken cancellation)
    {
        PeerControls? controls = null;
        while (!cancellation.IsCancellationRequested)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            WireState? wire;
            try
            {
                wire = await ReadAsync<WireState?>(stream, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                throw new IOException("No LAN snapshots received in eight seconds.");
            }
            if (wire is null || wire.Player is { IsValid: false })
                throw new InvalidDataException("Invalid peer snapshot.");
            if (wire.Controls is { } changes)
            {
                if (!changes.IsValid || (IsHost
                        ? changes.Exit is { Committed: true } || changes.Lobby is { Started: true } ||
                          changes.ShopStocks is not null || changes.DropFrame is not null
                        : changes.Reroll is not null || changes.DropReceipt is not null))
                    throw new InvalidDataException("Invalid peer snapshot.");
                controls = changes;
            }
            if (controls is null)
                throw new InvalidDataException("Initial peer snapshot has no world state.");
            connected.Latest = new ReceivedState(new PeerState(wire.Player, controls), Stopwatch.GetTimestamp());
        }
    }

    private static async Task SendAsync<T>(NetworkStream stream, T value, CancellationToken cancellation)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value);
        if (payload.Length > MaximumFrameBytes)
            throw new InvalidDataException("LAN frame exceeds limit.");
        var frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, payload.Length);
        payload.CopyTo(frame, 4);
        await stream.WriteAsync(frame, cancellation).ConfigureAwait(false);
    }

    private static async Task<T> ReadAsync<T>(NetworkStream stream, CancellationToken cancellation)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellation).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is < 1 or > MaximumFrameBytes)
            throw new InvalidDataException("LAN frame exceeds limit.");
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellation).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(payload)!;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        shutdown.Cancel();
        _ = worker.ContinueWith(_ => shutdown.Dispose(), TaskScheduler.Default);
    }

    private sealed class Connection(int seed, Guid playerId)
    {
        public int Seed { get; } = seed;
        public Guid PlayerId { get; } = playerId;
        public volatile ReceivedState? Latest;
    }

    private readonly record struct Hello(int Version, int Seed, Guid PlayerId);
    private sealed record HostIdentity(int Seed, Guid Id);
    private sealed record WireState(PlayerState? Player, PeerControls? Controls);
}

internal sealed record PlayerState(string Level, double X, double Y, int Direction, string Animation, int LevelSeed = 0)
{
    public ulong Layout { get; init; }
    public string? SpriteGroup { get; init; }
    public MovementSound[]? Sounds { get; init; }

    internal bool IsValid => Level?.Length is > 0 and <= 96 &&
                             Animation is "idle" or "run" or "jump" or "fall" &&
                             (SpriteGroup is null || SpriteGroup.Length is > 0 and <= 80) &&
                             (Sounds is null || Sounds.Length <= 4 &&
                              Array.TrueForAll(Sounds, sound => sound is { IsValid: true })) &&
                             double.IsFinite(X) && double.IsFinite(Y) &&
                             Math.Abs(X) <= 50000 && Math.Abs(Y) <= 50000 && Direction is -1 or 1;
}

internal sealed record ReceivedState(PeerState State, long At)
{
    internal bool IsFresh => Stopwatch.GetElapsedTime(At).TotalMilliseconds <= 800;
}

internal sealed record PeerState(PlayerState? Player, PeerControls Controls)
{
    internal ExitDecision? Exit => Controls.Exit;
    internal LobbyState? Lobby => Controls.Lobby;
    internal RerollRequest? Reroll => Controls.Reroll;
    internal ShopStock[]? ShopStocks => Controls.ShopStocks;
    internal DropFrame? DropFrame => Controls.DropFrame;
    internal DropReceipt? DropReceipt => Controls.DropReceipt;
}

internal sealed record PeerControls(ExitDecision? Exit, LobbyState? Lobby,
    RerollRequest? Reroll, ShopStock[]? ShopStocks, DropFrame? DropFrame, DropReceipt? DropReceipt)
{
    internal static readonly PeerControls Empty = new(null, null, null, null, null, null);

    internal bool IsValid => Exit is not { IsValid: false } &&
                             Reroll is not { IsValid: false } && DropFrame is not { IsValid: false } &&
                             DropReceipt is not { IsValid: false } &&
                             (ShopStocks is null || ShopStocks.Length <= ShopStock.MaxShops &&
                              Array.TrueForAll(ShopStocks, stock => stock is { IsValid: true }));
}

internal sealed record LobbyState(bool Ready, bool Started);

internal sealed record ExitDecision(DoorIdentity Door, bool Committed)
{
    internal bool IsValid => Door is { IsValid: true };
}

internal sealed record DoorIdentity(string Level, int LevelSeed, int X, int Y, string Destination)
{
    internal bool IsValid => Level?.Length is > 0 and <= 96 && Destination?.Length is > 0 and <= 96 &&
                             X is >= 0 and <= 4000 && Y is >= 0 and <= 4000;
}
