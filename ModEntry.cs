using System;
using System.IO;
using System.Net;
using dc;
using dc.pr;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces.Game;
using ModCore.Mods;
using ModCore.Utilities;
using Hook__Save = dc.tool.Hook__Save;
using IOFile = dc.tool.File;
using Save = dc.tool.Save;

namespace DeadCellsMultiplayerMod;

public sealed class ModEntry(ModInfo info) : ModBase(info), IOnGameEndInit, IOnFrameUpdate, IOnGameExit
{
    private LanSession? session;
    private NativePlayers? players;
    private NativeExits? exits;
    private NativeShops? shops;
    private NativeLoot? loot;
    private NativeEnemies? enemies;
    private NativeRevival? revival;
    private IPEndPoint? endpoint;
    private bool host;
    private int runSeed;
    private bool launchRequested;
    private bool ready;
    private int readyForSeed;
    private Guid readyForHost;
    private string? lobbyError;
    private TitleScreen? lobbyMenu;
    private bool reportedLayoutMismatch;
    private (int Seed, bool PeerReady, bool Ready, string? Error, string? NetworkError) lastMenuState;
#if COOP_QA
    private CoopQa? qa;
#endif

    public override void Initialize()
    {
        Logger.Information("Multiplayer mod initialized.");
        var mode = Environment.GetEnvironmentVariable("DEAD_CELLS_COOP");
        if (string.IsNullOrWhiteSpace(mode))
            return;

        if (mode.StartsWith("host:", StringComparison.Ordinal) &&
            int.TryParse(mode.AsSpan(5), out var port) && port is > 0 and <= 65535)
        {
            host = true;
            endpoint = new IPEndPoint(IPAddress.Any, port);
        }
        else if (mode.StartsWith("join:", StringComparison.Ordinal) &&
                 IPEndPoint.TryParse(mode.AsSpan(5), out var remote) && remote.Port > 0)
        {
            endpoint = remote;
        }
        else
        {
            Logger.Warning("Use host:<port> or join:<IP>:<port> for DEAD_CELLS_COOP.");
            return;
        }

        Directory.CreateDirectory(Path.Combine("save", "MSave"));
        Hook__Save.fileName += ResolveCoopSavePath;
        Hook_User.newGame += ApplyHostSeed;
        Hook_TitleScreen.mainMenu += BuildLobbyMenu;
    }

    public void OnGameEndInit()
    {
        Logger.Information("Multiplayer mod reached game initialization.");
        var options = Main.Class.ME.options;
        options.mute = true;
        options.applyVolumes();
        Logger.Information("Game audio muted at startup.");
        if (endpoint is null)
            return;

        session = new LanSession(host, endpoint, message => Logger.Information("{LanState}", message));
        players = new NativePlayers(message => Logger.Information("{PlayerState}", message));
        exits = new NativeExits(host, () => runSeed != 0, message => Logger.Information("{ExitState}", message));
        shops = new NativeShops(host, () => session?.RunSeed == runSeed && runSeed != 0 && !reportedLayoutMismatch,
            message => Logger.Information("{ShopState}", message));
        shops.Install();
        loot = new NativeLoot(host, () => runSeed != 0, message => Logger.Warning("{DropState}", message));
        loot.Install();
        enemies = new NativeEnemies(host, () => runSeed != 0, players);
        revival = new NativeRevival(host, message => Logger.Information("{Revival}", message));
#if COOP_QA
        var qaMode = Environment.GetEnvironmentVariable("DEAD_CELLS_COOP_QA");
        if (qaMode == "boss")
            Hook_Game.loadMainLevel += (original, game, transition, level, keepHero, seed) =>
                original(game, transition, level?.ToString() == "PrisonStart" ? "Bridge".AsHaxeString() : level, keepHero, seed);
        if (qaMode is "1" or "exit" or "shop" or "loot" or "presentation" or "normal" or "effects" or "equipment" or "summons" or "combat" or "revive" or "enemy-target" or "boss")
        {
            options.masterVolume = 0;
            options.applyVolumes();
            var stopSignal = Environment.GetEnvironmentVariable("DEAD_CELLS_COOP_QA_STOP");
            qa = new CoopQa(host, qaMode, stopSignal,
                message => Logger.Information("{CoopQa}", message));
        }
#endif
    }

    public void OnFrameUpdate(double dt)
    {
#if COOP_QA
        if (qa?.CheckForShutdown() == true)
            return;
#endif
        if (session is null || players is null || exits is null || shops is null || loot is null || enemies is null || revival is null)
            return;

        var (seed, peerId, snapshot) = session.ObservePeer();
        var incoming = snapshot?.State;
        if (!launchRequested && TitleScreen.Class.ME is { isMainMenu: true } title)
        {
            if (!host && seed != 0 && (readyForSeed != seed || readyForHost != peerId))
                ready = false;
            var peerReady = snapshot is { IsFresh: true } && incoming?.Lobby?.Ready == true;
            var menuState = (seed, peerReady, ready, lobbyError, session.ConnectionError);
            if (!ReferenceEquals(lobbyMenu, title) || lastMenuState != menuState)
                title.mainMenu();
#if COOP_QA
            qa?.DriveLobby(seed != 0 && (host ? peerReady : !ready), () => StartOrReady(title));
#endif
            if (!host && ready && seed != 0 && snapshot is { IsFresh: true } && incoming?.Lobby?.Started == true)
                StartRun(title, seed, peerId);
            if (!launchRequested)
                session.Publish(null, lobby: new LobbyState(ready, false));
            return;
        }

        var hero = Game.Class.ME?.hero;
        var remote = seed == runSeed ? incoming : null;
        var player = players.CaptureForLan(hero);
        var layoutMismatch = player is not null && remote?.Player is { } other &&
            other.Level == player.Level && other.LevelSeed == player.LevelSeed && other.Layout != player.Layout;
        if (layoutMismatch)
        {
            if (!reportedLayoutMismatch)
            {
                hero!.popError("Co-op maps differ. Check difficulty, DLC and mods.".AsHaxeString(), null);
                Logger.Warning("LAN layout differs from the host or client in {Level}.", player!.Level);
            }
            remote = null;
        }
        reportedLayoutMismatch = layoutMismatch;
        players.Update(hero, remote?.Player, snapshot?.At ?? 0, peerId);
        if (snapshot is not { IsFresh: true })
            remote = null;
        player = revival.Update(hero, player, remote?.Player);
        exits.Update(hero, remote, player is { Downed: > 0 } || remote?.Player is { Downed: > 0 });
        shops.Update(hero, remote);
        loot.Update(hero, player, remote);
        var combat = enemies.Update(hero, player, remote);
        var receipt = loot.Receipt;
#if COOP_QA
        if (qa is not null)
            receipt = qa.OutgoingReceipt(receipt);
#endif
        session.Publish(player, exits.Decision, new LobbyState(ready, host && launchRequested),
            shops.Request, shops.Stocks, loot.Frame, receipt, combat);
#if COOP_QA
        qa?.Tick(hero, remote?.Player, players, loot, enemies, revival);
#endif
    }

    public void OnGameExit()
    {
        session?.Dispose();
        session = null;
    }

    private static dc.String ResolveCoopSavePath(Hook__Save.orig_fileName original, int? slot)
        => ("MSave/" + Path.GetFileName(original(slot).ToString())).AsHaxeString();

    private void BuildLobbyMenu(Hook_TitleScreen.orig_mainMenu original, TitleScreen title)
    {
        original(title);
        lobbyMenu = title;
        var (seed, _, frame) = session?.ObservePeer() ?? default;
        var peerReady = frame is { IsFresh: true, State.Lobby.Ready: true };
        lastMenuState = (seed, peerReady, ready, lobbyError, session?.ConnectionError);
        title.clearMenu();
        if (lobbyError is { } error)
            AddMenu(title, error, null, false);
        if (session?.ConnectionError is { } networkError)
            AddMenu(title, networkError, null, false);
        if (launchRequested)
        {
            AddMenu(title, "Reopen co-op to start another run", null, false);
        }
        else if (host)
        {
            AddMenu(title, seed == 0 ? "Waiting for a LAN player" :
                !peerReady ? "Waiting for your partner to ready up" : "Start new co-op run",
                () => StartOrReady(title), seed != 0 && peerReady);
        }
        else
        {
            AddMenu(title, seed == 0 ? "Connecting to host" : ready ? "Waiting for host to begin" : "Ready",
                () => StartOrReady(title), seed != 0 && !ready);
        }
        AddMenu(title, "Quit", () =>
        {
            if (Main.Class.ME.onExit())
                dc.hxd.System.Class.exit.Invoke();
        }, true);
    }

    private static void AddMenu(TitleScreen title, string label, Action? action, bool enabled)
    {
        title.addMenu(label.AsHaxeString(), new HlAction(action ?? (() => { })), null, enabled, Ref<int>.Null);
    }

    private void StartOrReady(TitleScreen title)
    {
        if (session is null)
            return;
        var (seed, peerId, snapshot) = session.ObservePeer();
        if (seed == 0 || launchRequested || snapshot is not { IsFresh: true } ||
            (host ? snapshot.State.Lobby?.Ready != true : ready))
            return;

        if (host && !session.TryReservePeerForRun(peerId))
            return;

        try
        {
            var savePath = Save.Class.fileName.Invoke(null).ToString();
            var backup = CoopSaves.BackUpSlot(Path.Combine(IOFile.Class.PATH.ToString(), savePath));
            if (backup is not null)
                Logger.Information("Existing co-op slot backed up to {Backup}.", backup);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (host)
                session!.ReleasePeerReservation();
            lobbyError = "Could not back up the co-op save";
            Logger.Warning(error, "Co-op launch blocked while backing up the current slot.");
            return;
        }

        lobbyError = null;
        ready = true;
        readyForSeed = seed;
        readyForHost = peerId;
        if (host)
            StartRun(title, seed, peerId);
    }

    private void StartRun(TitleScreen title, int seed, Guid peerId)
    {
        if (!host)
            session!.PinHostForRun(seed, peerId);
        launchRequested = true;
        runSeed = seed;
        session!.Publish(null, lobby: new LobbyState(ready, host));
        Logger.Information("Starting LAN run with seed {RunSeed}.", seed);
        title.startNewGame(false);
    }

    private void ApplyHostSeed(Hook_User.orig_newGame original, User user, int seed,
        Hashlink.Virtuals.virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_ level,
        bool custom, bool streaming, LaunchMode launchMode)
    {
        original(user, runSeed == 0 ? seed : runSeed, level, custom, streaming, launchMode);
    }
}
