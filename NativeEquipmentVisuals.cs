using System;
using System.Collections.Generic;
using System.Diagnostics;
using dc;
using dc.en;
using dc.en.mob;
using dc.en.pet;
using dc.libs.heaps.slib;
using dc.pr;
using Hashlink.Virtuals;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using Math = System.Math;

namespace DeadCellsMultiplayerMod;

internal sealed class NativeEquipmentVisuals
{
    private sealed record Owned(int Id, EquipmentKind Kind, long SpawnedAt);
    private sealed record Replica(HSprite Sprite, EquipmentVisual Pose);
    private readonly Dictionary<Entity, Owned> owned = new();
    private readonly Dictionary<int, Replica> replicas = new();
    private readonly List<(Level Level, EquipmentVisual Visual, long CapturedAt)> brief = [];
    private readonly Dictionary<SpriteLib, dc.String> cachedAtlases = new();
    private Level? source;
    private long sampledAt;
    private long scannedAtlasesAt;
    private long renderedAt;
    private int nextId;
    private EquipmentVisual[]? outgoing;
    private EquipmentVisual[]? lastRemote;
    private Level? destination;
#if COOP_QA
    internal HashSet<EquipmentKind> Captured { get; } = [];
    internal HashSet<EquipmentKind> Rendered { get; } = [];
#endif

    internal NativeEquipmentVisuals()
    {
        Hook__Bullet.__constructor__ += (original, bullet, owner, attack, speed, distance, group) =>
        {
            original(bullet, owner, attack, speed, distance, group);
            Track(bullet, attack?.source ?? owner, EquipmentKind.Projectile);
        };
        Hook__Grenade.__constructor__ += (original, grenade, owner, speed) =>
        {
            original(grenade, owner, speed);
            Track(grenade, owner, EquipmentKind.Grenade);
        };
        Hook__Active.__constructor__ += (original, active, hero, x, y, item) =>
        {
            original(active, hero, x, y, item);
            Track(active, hero, EquipmentKind.Deployable);
        };
        Hook__Pet.__constructor__ += (original, pet, level, x, y, owner, item) =>
        {
            original(pet, level, x, y, owner, item);
            Track(pet, owner, EquipmentKind.Companion);
        };
        Hook__PetMob.__constructor__ += (original, pet, level, x, y, kind, difficulty, tier, owner, item, color) =>
        {
            original(pet, level, x, y, kind, difficulty, tier, owner, item, color);
            Track(pet, owner, EquipmentKind.Companion);
        };
        Hook__Boomerang.__constructor__ += (original, projectile, owner, item, angle) =>
        {
            original(projectile, owner, item, angle);
            Track(projectile, owner, EquipmentKind.Projectile);
        };
        Hook__Decoy.__constructor__ += (original, decoy, owner, grenade, item) =>
        {
            original(decoy, owner, grenade, item);
            Track(decoy, owner, EquipmentKind.Deployable);
        };
        Hook__Homunculus.__constructor__ += (original, head, level, x, y, visible, auto, skill) =>
        {
            original(head, level, x, y, visible, auto, skill);
            Track(head, skill?.hero, EquipmentKind.Companion);
        };
        Hook_Entity.destroy += (original, entity) =>
        {
            if (owned.TryGetValue(entity, out var identity) && identity.Kind == EquipmentKind.Projectile &&
                Stopwatch.GetElapsedTime(identity.SpawnedAt).TotalMilliseconds < 120 &&
                entity._level is { } level && ReadSprite(entity, identity) is { } pose)
            {
                // A close-range hit can remove the projectile before the first LAN tick.
                if (brief.Count == EquipmentVisual.MaxObjects)
                    brief.RemoveAt(0);
                brief.Add((level, pose, Stopwatch.GetTimestamp()));
            }
            original(entity);
        };
    }

    private void Track(Entity entity, Entity? owner, EquipmentKind kind)
    {
        var localHero = Game.Class.ME?.hero;
        if (localHero is not null && (ReferenceEquals(owner, localHero) || Owns(owner)) && !owned.ContainsKey(entity))
            owned.Add(entity, new Owned(++nextId, kind, Stopwatch.GetTimestamp()));
    }

    internal bool Owns(Entity? entity) => entity is not null && owned.ContainsKey(entity);

    internal EquipmentVisual[]? Capture(Level level)
    {
        var now = Stopwatch.GetTimestamp();
        if (ReferenceEquals(source, level) && Stopwatch.GetElapsedTime(sampledAt, now).TotalMilliseconds < 30)
            return outgoing;

        source = level;
        sampledAt = now;
        List<EquipmentVisual> objects = [];
        foreach (var (entity, identity) in owned)
        {
            if (entity.destroyed || !ReferenceEquals(entity._level, level))
            {
                owned.Remove(entity);
                continue;
            }
            if (objects.Count >= EquipmentVisual.MaxObjects || ReadSprite(entity, identity) is not { } pose)
                continue;
            objects.Add(pose);
        }
        brief.RemoveAll(entry => !ReferenceEquals(entry.Level, level) ||
            Stopwatch.GetElapsedTime(entry.CapturedAt, now).TotalMilliseconds > 110);
        foreach (var entry in brief)
        {
            if (objects.Count >= EquipmentVisual.MaxObjects || objects.Exists(pose => pose.Id == entry.Visual.Id))
                continue;
            objects.Add(entry.Visual);
        }
#if COOP_QA
        foreach (var pose in objects)
            Captured.Add(pose.Kind);
#endif
        outgoing = objects.Count > 0 ? objects.ToArray() : null;
        return outgoing;
    }

    private EquipmentVisual? ReadSprite(Entity entity, Owned identity)
    {
        if (!entity.visible || entity.spr is not
            { destroyed: false, visible: true, lib: { } library, groupName: { } group } sprite ||
            IdentifyAtlas(library) is not { } atlas)
            return null;
        var pose = new EquipmentVisual(identity.Id, identity.Kind, atlas.Atlas, group.ToString(),
            sprite.x, sprite.y, sprite.scaleX, sprite.scaleY, sprite.rotation, sprite.alpha, sprite.frame, atlas.Key);
        return pose.IsValid ? pose : null;
    }

    internal void Render(Level level, EquipmentVisual[]? objects)
    {
        if (!ReferenceEquals(destination, level))
        {
            Clear();
            destination = level;
        }
        if (!ReferenceEquals(lastRemote, objects))
        {
            lastRemote = objects;
            foreach (var (id, replica) in replicas)
            {
                if (objects is not null && Array.Exists(objects, pose => pose.Id == id))
                    continue;
                replica.Sprite.remove();
                replicas.Remove(id);
            }
        }
        if (objects is null)
            return;
        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(renderedAt, now).TotalMilliseconds;
        renderedAt = now;
        var blend = 1 - Math.Exp(-elapsed / 28);
        foreach (var pose in objects)
            Apply(level, pose, blend);
    }

    private void Apply(Level level, EquipmentVisual pose, double blend)
    {
        if (!replicas.TryGetValue(pose.Id, out var replica) || replica.Sprite.destroyed ||
            replica.Pose.Group != pose.Group || replica.Pose.Atlas != pose.Atlas || replica.Pose.AtlasKey != pose.AtlasKey)
        {
            if (replicas.Remove(pose.Id, out var previous) && !previous.Sprite.destroyed)
                previous.Sprite.remove();
            var library = ResolveAtlas(pose.Atlas, pose.AtlasKey);
            var name = pose.Group.AsHaxeString();
            if (library?.groups?.exists(name) != true)
                return;
            var sprite = new HSprite(library, name, Ref<int>.Null, null);
            level.scroller.over(sprite);
            replica = new Replica(sprite, pose);
            replicas[pose.Id] = replica;
            sprite.x = pose.X;
            sprite.y = pose.Y;
        }
        else if (replica.Pose != pose)
        {
            replica = replica with { Pose = pose };
            replicas[pose.Id] = replica;
        }
        var visual = replica.Sprite;
        var dx = pose.X - visual.x;
        var dy = pose.Y - visual.y;
        var movementBlend = dx * dx + dy * dy > 192 * 192 ? 1 : blend;
        visual.x += dx * movementBlend;
        visual.y += dy * movementBlend;
        visual.scaleX = pose.ScaleX;
        visual.scaleY = pose.ScaleY;
        visual.rotation = pose.Rotation;
        visual.alpha = pose.Alpha;
        if (visual.frame != pose.Frame && pose.Frame < visual.totalFrames())
            visual.setFrame(pose.Frame);
#if COOP_QA
        Rendered.Add(pose.Kind);
#endif
    }

    internal void Clear()
    {
        foreach (var replica in replicas.Values)
            if (!replica.Sprite.destroyed)
                replica.Sprite.remove();
        replicas.Clear();
        lastRemote = null;
        destination = null;
        renderedAt = 0;
    }

    private (SpriteAtlas Atlas, string? Key)? IdentifyAtlas(SpriteLib library)
    {
        for (var id = SpriteAtlas.Effects; id < SpriteAtlas.Cached; id++)
            if (ReferenceEquals(ResolveAtlas(id, null), library))
                return (id, null);

        if (cachedAtlases.TryGetValue(library, out var name))
            return (SpriteAtlas.Cached, name.ToString());
        var now = Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(scannedAtlasesAt, now).TotalMilliseconds < 150 ||
            Assets.Class.lib?.cache is not { } cache)
            return null;
        scannedAtlasesAt = now;
        cachedAtlases.Clear();
        var keys = cache.keys();
        while (keys.hasNext.Invoke())
        {
            var key = keys.next.Invoke();
            if (cache.get(key) is virtual_lib_used_ { lib: { } resolved })
                cachedAtlases.TryAdd(resolved, key);
        }
        return cachedAtlases.TryGetValue(library, out name) ? (SpriteAtlas.Cached, name.ToString()) : null;
    }

    private static SpriteLib? ResolveAtlas(SpriteAtlas atlas, string? key) => atlas switch
    {
        SpriteAtlas.Effects => Assets.Class.fx,
        SpriteAtlas.Weapon => Assets.Class.fxWeapon,
        SpriteAtlas.Enemy => Assets.Class.fxEnemy,
        SpriteAtlas.Level => Assets.Class.gameElements,
        SpriteAtlas.Common => Assets.Class.common,
        SpriteAtlas.Cached when key is not null =>
            (Assets.Class.lib?.cache?.get(key.AsHaxeString()) as virtual_lib_used_)?.lib,
        _ => null
    };
}
