using System;
using System.Diagnostics;
using dc.en;
using dc.hl.types;
using dc.level;
using dc.pr;
using ModCore.Utilities;

namespace DeadCellsMultiplayerMod;

internal sealed class NativePlayers
{
    private readonly Action<string> log;
    private KingSkin? replica;
    private Level? replicaLevel;
    private long lastUpdate;
    private string? previousAnimation;
    private double x;
    private double y;
    private LevelMap? lastMap;
    private ulong layout;

    internal NativePlayers(Action<string> log)
    {
        this.log = log;
        Hook_KingSkin.fixedUpdate += SkipReplicaSimulation;
        Hook_Level.fillCircularCollisionEntitiesBuffer += ExcludeReplicaFromCollision;
    }

    internal static PlayerState? Capture(Hero? hero)
    {
        if (hero is null || hero.destroyed || hero.spr is not { } sprite ||
            hero._level?.map is not { } map || map.id?.ToString() is not { Length: > 0 } level)
            return null;

        var animation = hero.dy < -0.05 ? "jump" : hero.dy > 0.05 ? "fall" :
                        Math.Abs(hero.dx) > 0.05 ? "run" : "idle";
        return new PlayerState(level, sprite.x, sprite.y, hero.dir < 0 ? -1 : 1, animation, map.seed);
    }

    internal PlayerState? CaptureForLan(Hero? hero)
    {
        if (Capture(hero) is not { } state)
            return null;
        var map = hero!._level.map;
        if (!ReferenceEquals(map, lastMap))
        {
            lastMap = map;
            layout = 14695981039346656037UL;
            AddLayoutValue(map.wid);
            AddLayoutValue(map.hei);
            var rooms = map.rooms;
            for (var i = 0; i < rooms.length; i++)
            {
                if (rooms.getDyn(i) is not Room room)
                    continue;
                AddLayoutValue(room.id);
                AddLayoutValue(room.x);
                AddLayoutValue(room.y);
                AddLayoutValue(room.wid);
                AddLayoutValue(room.hei);
            }
        }
        return state with { Layout = layout };
    }

    private void AddLayoutValue(int value) => layout = unchecked((layout ^ (uint)value) * 1099511628211UL);

    internal void Update(Hero? hero, PlayerState? state)
    {
        var level = hero?._level;
        if (state is null || level?.map?.id?.ToString() != state.Level || level.map.seed != state.LevelSeed)
        {
            RemoveReplica();
            return;
        }

        var now = Stopwatch.GetTimestamp();
        if (replica is null || !ReferenceEquals(replicaLevel, level) || replica.destroyed)
        {
            RemoveReplica();
            replicaLevel = level;
            replica = new KingSkin(level!, hero!.cx, hero.cy);
            replica.init();
            replica._targetable = false;
            replica.hasGravity = false;
            replica.hasEntityTouchChecks = false;
            replica.colLayer = 0;
            replica.isAlwaysUpdated = true;
            x = state.X;
            y = state.Y;
            previousAnimation = null;
            lastUpdate = now;
            log($"Remote player spawned in {state.Level}.");
        }

        var dt = Stopwatch.GetElapsedTime(lastUpdate, now).TotalSeconds;
        lastUpdate = now;
        var distanceSquared = Math.Pow(state.X - x, 2) + Math.Pow(state.Y - y, 2);
        var blend = distanceSquared > 192 * 192 ? 1.0 : 1.0 - Math.Exp(-18.0 * dt);
        x += (state.X - x) * blend;
        y += (state.Y - y) * blend;
        replica.setPosPixel(x, y);
        replica.dir = state.Direction;
        replica.visible = true;

        if (replica.spr is { _animManager: { } animations } sprite && previousAnimation != state.Animation)
        {
            var animation = state.Animation is "run" or "jump" or "fall" ? state.Animation : "idle";
            if (sprite.lib?.groups?.exists(animation.AsHaxeString()) == true)
                animations.play(animation.AsHaxeString(), null, null)?.loop(null);
            previousAnimation = state.Animation;
        }
    }

#if COOP_QA
    internal bool ReplicaAt(PlayerState state) =>
        replica is { destroyed: false, visible: true, spr: { } sprite } &&
        Math.Abs(sprite.x - state.X) <= 4 && Math.Abs(sprite.y - state.Y) <= 4;
#endif

    private void RemoveReplica()
    {
        if (replica is { destroyed: false } old && replicaLevel is { destroyed: false })
            old.destroy();
        replica = null;
        replicaLevel = null;
    }

    private void SkipReplicaSimulation(Hook_KingSkin.orig_fixedUpdate original, KingSkin entity)
    {
        if (!ReferenceEquals(entity, replica))
            original(entity);
    }

    private void ExcludeReplicaFromCollision(Hook_Level.orig_fillCircularCollisionEntitiesBuffer original,
        Level level, ArrayObj collisions)
    {
        original(level, collisions);
        if (replica is not null)
            collisions.remove(replica);
    }
}
