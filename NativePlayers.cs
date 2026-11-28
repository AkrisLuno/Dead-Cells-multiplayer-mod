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
    private readonly RemoteMotion motion = new();
    private readonly NativeEquipmentVisuals equipment = new();
    private readonly NativeMovementAudio audio;
    private readonly NativePlayerEffects effects;
    private KingSkin? replica;
    private Level? replicaLevel;
    private string? previousAnimation;
    private (string? Group, string? Fallback) previousPose;
    private const int PartnerOutline = 0x62D9FF;
    private LevelMap? lastMap;
    private ulong layout;
    private Guid remoteId;

    internal NativePlayers(Action<string> log)
    {
        this.log = log;
        audio = new NativeMovementAudio(equipment);
        effects = new NativePlayerEffects(equipment);
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
        var group = sprite.groupName?.ToString();
        return new PlayerState(level, sprite.x, sprite.y, hero.dir < 0 ? -1 : 1, animation, map.seed)
        {
            SpriteGroup = group is { Length: > 0 and <= 80 } ? group : null,
            Opacity = sprite.visible ? Math.Clamp(sprite.alpha, 0, 1) : 0
        };
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
        return state with
        {
            Layout = layout, Sounds = audio.Outgoing(hero._level), Effects = effects.Outgoing(hero._level),
            Equipment = equipment.Capture(hero._level)
        };
    }

    private void AddLayoutValue(int value) => layout = unchecked((layout ^ (uint)value) * 1099511628211UL);

    internal void Update(Hero? hero, PlayerState? state, long receivedAt, Guid peerId)
    {
        if (peerId != Guid.Empty && peerId != remoteId)
        {
            remoteId = peerId;
            RemoveReplica();
            effects.ResetPlayback();
            audio.ResetPlayback();
        }
        var level = hero?._level;
        var now = Stopwatch.GetTimestamp();
        if (state is null || level?.map?.id?.ToString() != state.Level || level.map.seed != state.LevelSeed ||
            Stopwatch.GetElapsedTime(receivedAt, now).TotalMilliseconds > 1500)
        {
            RemoveReplica();
            return;
        }

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
            replica.setOutlineColor(PartnerOutline);
            previousAnimation = null;
            previousPose = default;
            log($"Remote player spawned in {state.Level}.");
        }

        motion.Add(state, receivedAt);
        var pose = motion.Render(now);
        replica.setPosPixel(pose.X, pose.Y);
        replica.dir = pose.Direction;
        replica.visible = true;
        if (previousPose != (pose.SpriteGroup, pose.Animation) &&
            replica.spr is { _animManager: { } animations, lib.groups: { } groups })
        {
            previousPose = (pose.SpriteGroup, pose.Animation);
            var animation = pose.SpriteGroup is { } group && groups.exists(group.AsHaxeString()) ? group : pose.Animation;
            if (previousAnimation != animation && groups.exists(animation.AsHaxeString()))
            {
                animations.play(animation.AsHaxeString(), null, null)?.loop(null);
                previousAnimation = animation;
            }
        }
        replica.spriteUpdate();
        if (replica.spr is { } sprite)
            sprite.alpha = pose.Opacity;
        equipment.Render(level!, pose.Equipment);
        if (Stopwatch.GetElapsedTime(receivedAt, now).TotalMilliseconds <= 350)
        {
            effects.Play(level!, replica, pose);
            if (hero?.spr is { } listener)
                audio.Play(level!, pose, listener.x, listener.y);
        }
    }

#if COOP_QA
    internal bool ReplicaAt(PlayerState state) =>
        replica is { destroyed: false, visible: true, spr: { } sprite } &&
        Math.Abs(sprite.x - state.X) <= 4 && Math.Abs(sprite.y - state.Y) <= 4;

    internal bool ReplicaShowsPartner(PlayerState state) =>
        replica is { destroyed: false, outlineColor: PartnerOutline } && replica.dir == state.Direction &&
        previousAnimation is not null;
    internal string? ReplicaAnimation => replica?.spr?.groupName?.ToString();
    internal int RemoteSounds => audio.Played;
    internal string? LastRemoteSound => audio.LastAsset;
    internal bool PlayedEquipmentSound => audio.PlayedEquipment;
    internal NativePlayerEffects Effects => effects;
    internal NativeEquipmentVisuals Equipment => equipment;
    internal (double Near, double Middle, bool FarSilent) ProbeSoundDistance(Hero hero, PlayerState state)
        => audio.ProbeDistance(hero._level, state);
#endif

    private void RemoveReplica()
    {
        if (replica is { destroyed: false } old && replicaLevel is { destroyed: false })
            old.destroy();
        replica = null;
        replicaLevel = null;
        motion.Clear();
        equipment.Clear();
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
