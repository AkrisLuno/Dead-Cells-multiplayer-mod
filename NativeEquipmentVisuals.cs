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
    private sealed class Owned(int id, EquipmentKind kind, long spawnedAt, Mob? hostileSource)
    {
        internal int Id { get; } = id;
        internal EquipmentKind Kind { get; } = kind;
        internal long SpawnedAt { get; } = spawnedAt;
        internal Mob? HostileSource { get; } = hostileSource;
        internal CompanionTrail? Trail { get; set; }
        internal long TrailAt { get; set; }
    }
    private sealed record Replica(HSprite Sprite, EquipmentVisual Pose, bool Appeared);
    private readonly bool host;
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
    internal int CompanionArrivals { get; private set; }
    internal int CompanionRemovals { get; private set; }
    internal int CompanionTrails { get; private set; }
    internal bool CapturedHostileProjectile { get; private set; }
#endif

    internal NativeEquipmentVisuals(bool host)
    {
        this.host = host;
        Hook__Bullet.__constructor__ += (original, bullet, owner, attack, speed, distance, group) =>
        {
            original(bullet, owner, attack, speed, distance, group);
            Track(bullet, owner, EquipmentKind.Projectile, attack?.source);
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
        Hook_Fx.tailLine += (original, fx, entity, color, alpha, thickness, duration, offX, offY) =>
        {
            if (owned.TryGetValue(entity, out var companion) && companion.Kind == EquipmentKind.Companion)
            {
                var x = offX.IsNull ? 0 : offX.value;
                var y = offY.IsNull ? 0 : offY.value;
                var life = duration ?? 1;
                if (companion.Trail is not { } previous || previous.Color != color || previous.Alpha != alpha ||
                    previous.Thickness != thickness || previous.Duration != life || previous.OffsetX != x || previous.OffsetY != y)
                {
                    var trail = new CompanionTrail(color, alpha, thickness, life, x, y);
                    companion.Trail = trail.IsValid ? trail : null;
                }
                companion.TrailAt = Stopwatch.GetTimestamp();
            }
            original(fx, entity, color, alpha, thickness, duration, offX, offY);
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

    private void Track(Entity entity, Entity? owner, EquipmentKind kind, Entity? attackSource = null)
    {
        if (owned.ContainsKey(entity))
            return;
        var localHero = Game.Class.ME?.hero;
        var hostile = host && kind is EquipmentKind.Projectile or EquipmentKind.Grenade
            ? FindHostileSource(owner) ?? FindHostileSource(attackSource) : null;
        if (hostile is not null || localHero is not null &&
            (ReferenceEquals(owner, localHero) || Owns(owner) || ReferenceEquals(attackSource, localHero) || Owns(attackSource)))
            owned.Add(entity, new Owned(++nextId, kind, Stopwatch.GetTimestamp(), hostile));
    }

    internal bool Owns(Entity? entity) => entity is not null &&
        owned.TryGetValue(entity, out var identity) && identity.HostileSource is null;

    internal Mob? FindHostileSource(Entity? entity)
    {
        if (entity is Mob mob && NativeEnemies.IsEnemy(mob))
            return mob;
        return entity is not null && owned.TryGetValue(entity, out var identity) ? identity.HostileSource : null;
    }

    internal bool OwnsCompanion(Entity? entity) => entity is not null && owned.TryGetValue(entity, out var identity) &&
        identity.Kind == EquipmentKind.Companion;

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
#if COOP_QA
            if (identity is { Kind: EquipmentKind.Projectile, HostileSource: not null })
                CapturedHostileProjectile = true;
#endif
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
        if (entity.spr is not { destroyed: false, lib: { } library, groupName: { } group } sprite ||
            identity.Kind != EquipmentKind.Companion && (!entity.visible || !sprite.visible) ||
            IdentifyAtlas(library) is not { } atlas)
            return null;
        var trail = identity.Trail is { } style && Stopwatch.GetElapsedTime(identity.TrailAt).TotalMilliseconds < 95 ? style : null;
        var pose = new EquipmentVisual(identity.Id, identity.Kind, atlas.Atlas, group.ToString(),
            sprite.x, sprite.y, sprite.scaleX, sprite.scaleY, sprite.rotation,
            entity.visible && sprite.visible ? sprite.alpha : 0, sprite.frame, atlas.Key, trail);
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
                if (!replica.Sprite.destroyed)
                    replica.Sprite.remove();
                replicas.Remove(id);
#if COOP_QA
                if (replica.Pose.Kind == EquipmentKind.Companion)
                    CompanionRemovals++;
#endif
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
            var x = pose.X;
            var y = pose.Y;
            if (replica is { Sprite.destroyed: false } previous)
            {
                x = previous.Sprite.x;
                y = previous.Sprite.y;
                previous.Sprite.remove();
            }
            replicas.Remove(pose.Id);
            var library = ResolveAtlas(pose.Atlas, pose.AtlasKey);
            var name = pose.Group.AsHaxeString();
            if (library?.groups?.exists(name) != true)
                return;
            var sprite = new HSprite(library, name, Ref<int>.Null, null);
            level.scroller.over(sprite);
            replica = new Replica(sprite, pose, replica?.Appeared == true);
            replicas[pose.Id] = replica;
            sprite.x = x;
            sprite.y = y;
        }
        if (!replica.Appeared && pose.Kind == EquipmentKind.Companion && pose.Alpha > 0)
        {
            level.fx.smallIceExplosion(pose.X, pose.Y, pose.Trail?.Color ?? 0xB5D7FF);
            replica = replica with { Appeared = true };
#if COOP_QA
            CompanionArrivals++;
#endif
        }
        if (replica.Pose != pose)
        {
            replica = replica with { Pose = pose };
        }
        replicas[pose.Id] = replica;
        var visual = replica.Sprite;
        var previousX = visual.x;
        var previousY = visual.y;
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
        if (pose.Alpha > 0 && pose.Trail is { } trail && movementBlend < 1 &&
            Math.Abs(visual.x - previousX) + Math.Abs(visual.y - previousY) >= 0.75)
        {
            level.fx.tailLineFree(previousX + trail.OffsetX, previousY + trail.OffsetY,
                visual.x + trail.OffsetX, visual.y + trail.OffsetY,
                trail.Color, trail.Alpha, trail.Thickness, trail.Duration);
#if COOP_QA
            CompanionTrails++;
#endif
        }
#if COOP_QA
        Rendered.Add(pose.Kind);
#endif
    }

#if COOP_QA
    internal T? FindCompanion<T>() where T : Entity
    {
        foreach (var (entity, identity) in owned)
            if (identity.Kind == EquipmentKind.Companion && entity is T { destroyed: false } companion)
                return companion;
        return null;
    }
#endif

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
