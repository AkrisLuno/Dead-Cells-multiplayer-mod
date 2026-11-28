using System;
using dc.en;
using dc.en.inter;
using dc.pr;

namespace DeadCellsMultiplayerMod;

internal sealed class NativeExits
{
    private readonly bool host;
    private readonly Func<bool> inCoop;
    private readonly Action<string> log;
    private Exit? selected;
    private DoorIdentity? applied;
    private bool activating;

    internal ExitDecision? Decision { get; private set; }

    internal NativeExits(bool host, Func<bool> inCoop, Action<string> log)
    {
        this.host = host;
        this.inCoop = inCoop;
        this.log = log;
        Hook_Exit.onActivate += OnExit;
    }

    private void OnExit(Hook_Exit.orig_onActivate original, Exit exit, Hero hero, bool local)
    {
        if (!inCoop() || activating || !ReferenceEquals(hero, Game.Class.ME?.hero))
        {
            original(exit, hero, local);
            return;
        }

        var map = exit._level?.map;
        var destination = exit.destLevel?.ToString() ?? exit.getDestBasedOnNextLevels()?.ToString();
        if (map?.id?.ToString() is not { Length: > 0 } level || string.IsNullOrWhiteSpace(destination))
        {
            original(exit, hero, local);
            return;
        }

        if (Decision is { Committed: true })
            return;
        selected = exit;
        var door = new DoorIdentity(level, map.seed, exit.cx, exit.cy, destination);
        if (Decision?.Door == door)
            return;
        Decision = new ExitDecision(door, false);
        log($"Exit ready: {level} ({door.X},{door.Y}) to {door.Destination}.");
    }

    internal void Update(Hero? hero, PeerState? peer)
    {
        if (Decision is null)
            return;

        var door = Decision.Door;
        var map = hero?._level?.map;
        var sameLevel = map?.id?.ToString() == door.Level && map.seed == door.LevelSeed;
        if (Decision.Committed)
        {
            if (!sameLevel && peer?.Player is { } other &&
                (other.Level != door.Level || other.LevelSeed != door.LevelSeed))
            {
                Decision = null;
                applied = null;
            }
            return;
        }

        if (!sameLevel || selected is null || selected.destroyed || hero is null ||
            Math.Abs(hero.cx - door.X) > 3 || Math.Abs(hero.cy - door.Y) > 3)
        {
            Decision = null;
            selected = null;
            return;
        }

        if (peer?.Exit is not { } otherExit || otherExit.Door != door || applied == door ||
            (!host && !otherExit.Committed))
            return;

        if (host)
        {
            if (peer.Player is not { } other || other.Level != door.Level || other.LevelSeed != door.LevelSeed ||
                Math.Abs(other.X / 24 - door.X) > 4 || Math.Abs(other.Y / 24 - door.Y) > 4)
                return;
            Decision = new ExitDecision(door, true);
        }
        applied = door;
        activating = true;
        try
        {
            selected.onActivate(hero, true);
            log($"Co-op exit activated: {door.Level} to {door.Destination}.");
        }
        finally
        {
            activating = false;
            if (!host)
            {
                Decision = null;
                selected = null;
            }
        }
    }
}
