using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using dc;
using dc.en;
using dc.en.mob;
using dc.pr;
using dc.tool.atk;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using Math = System.Math;

namespace DeadCellsMultiplayerMod;

internal sealed class NativeEnemies
{
    private sealed class Enemy(EnemyKey key, Mob mob, int damageTier, int lifeTier)
    {
        internal EnemyKey Key { get; } = key;
        internal Mob Mob { get; } = mob;
        internal int DamageTier { get; } = damageTier;
        internal int LifeTier { get; } = lifeTier;
        internal EnemyState? Pose { get; set; }
        internal EnemyImpact? Impact { get; set; }
        internal int AppliedImpact { get; set; }
    }

    private sealed class Roster
    {
        internal Dictionary<EnemyKey, Enemy> ByKey { get; } = [];
        internal Dictionary<Mob, Enemy> ByMob { get; } = [];
        internal Dictionary<(string Kind, int X, int Y), int> NextOrder { get; } = [];

        internal void Add(Enemy enemy)
        {
            ByKey.Add(enemy.Key, enemy);
            ByMob.Add(enemy.Mob, enemy);
            var anchor = (enemy.Key.Kind, enemy.Key.CellX, enemy.Key.CellY);
            NextOrder[anchor] = Math.Max(NextOrder.GetValueOrDefault(anchor), enemy.Key.Order + 1);
        }
    }

    private readonly bool host;
    private readonly Func<bool> inRun;
    private readonly NativePlayers players;
    private readonly Dictionary<Level, Roster> rosters = [];
    private readonly Queue<EnemyHit> pending = new();
    private Level? level;
    private LootLevel? scope;
    private EnemyWorld? world;
    private EnemyWorld? lastWorld;
    private EnemyHits? sending;
    private long sampledAt;
    private int nextHit;
    private int acknowledged;
    private int directionOverride;
    private bool spawningReplica;

#if COOP_QA
    internal int ConfirmedImpacts { get; private set; }
    internal int MirroredDeaths { get; private set; }
    internal EnemyWorld? ObservedWorld => host ? world : lastWorld;
    internal Mob? FindForQa(EnemyKey key) => level is not null && GetRoster(level).ByKey.TryGetValue(key, out var enemy)
        ? enemy.Mob : null;
#endif

    internal NativeEnemies(bool host, Func<bool> inRun, NativePlayers players)
    {
        this.host = host;
        this.inRun = inRun;
        this.players = players;
        Hook__Mob.__constructor__ += (original, mob, source, x, y, kind, damageTier, lifeTier) =>
        {
            original(mob, source, x, y, kind, damageTier, lifeTier);
            if (inRun() && !spawningReplica && mob is not Boss && kind?.ToString() is { } name)
                Register(source, mob, name, x, y, damageTier, lifeTier);
        };
        Hook_Mob.canUpdate += (original, mob) => !IsReplica(mob) && original(mob);
        Hook_Entity.canUpdate += (original, entity) =>
            (entity is not Mob mob || !IsReplica(mob)) && original(entity);
        Hook_Entity.applyAttackResult += OnAttack;
    }

    private static bool IsEnemy(Mob mob) => mob is not Boss && mob._level is { } source &&
        mob._team is not null && ReferenceEquals(mob._team, source.teamMob);

    private bool IsReplica(Mob mob) => !host && inRun() && IsEnemy(mob);

    private Roster GetRoster(Level source)
    {
        if (!rosters.TryGetValue(source, out var roster))
            rosters.Add(source, roster = new Roster());
        return roster;
    }

    private Enemy? Register(Level source, Mob mob, string name, int x, int y, int damageTier, int lifeTier)
    {
        var roster = GetRoster(source);
        if (roster.ByMob.TryGetValue(mob, out var tracked))
            return tracked;
        if (roster.ByKey.Count >= EnemyWorld.MaxEnemies || !EnemyState.ValidName(name))
            return null;
        var key = new EnemyKey(name, x, y, roster.NextOrder.GetValueOrDefault((name, x, y)));
        if (!key.IsValid)
            return null;
        var entry = new Enemy(key, mob, Math.Clamp(damageTier, 0, 256), Math.Clamp(lifeTier, 0, 256));
        roster.Add(entry);
        return entry;
    }

    private void OnAttack(Hook_Entity.orig_applyAttackResult original, Entity entity, AttackData attack)
    {
        if (!inRun() || entity is not Mob { destroyed: false } mob || !IsEnemy(mob))
        {
            original(entity, attack);
            return;
        }

        var source = mob._level;
        var entry = Register(source, mob, mob.type?.ToString() ?? "", mob.cx, mob.cy, 0, mob.trueLifeTier);
        if (entry is null)
        {
            if (host)
                original(entity, attack);
            return;
        }
        var direction = directionOverride != 0 ? directionOverride : attack.source is { } attacker &&
            (attacker.cx < mob.cx || attacker.cx == mob.cx && attacker.dir > 0) ? 1 : -1;
        var critical = attack.hitResult is HitResult.Critical;
        if (!host)
        {
            if (scope is not null && ReferenceEquals(source, level) && attack.finalDmg > 0 &&
                (ReferenceEquals(attack.source, Game.Class.ME?.hero) || players.OwnsEquipment(attack.source) ||
                 players.OwnsEquipment(attack.carrier)) && pending.Count < 256)
            {
                var hit = new EnemyHit(++nextHit, entry.Key, Math.Min(attack.finalDmg, EnemyImpact.MaxDamage), direction, critical);
                pending.Enqueue(hit);
                sending = null;
            }
            return;
        }

        var before = mob.life;
        original(entity, attack);
        var damage = before - Math.Max(0, mob.life);
        if (damage <= 0)
            return;
        entry.Impact = new EnemyImpact((entry.Impact?.Revision ?? 0) + 1,
            Math.Min(damage, EnemyImpact.MaxDamage), direction, critical);
        if (ReadPose(entry) is { } pose)
        {
            entry.Pose = pose;
            PlayImpact(source, pose, entry.Impact);
        }
    }

    internal EnemySync? Update(Hero? hero, PlayerState? player, PeerState? peer)
    {
        if (!inRun() || hero?._level is not { } current || player is null)
            return null;
        if (!ReferenceEquals(level, current))
        {
            level = current;
            scope = null;
            world = null;
            lastWorld = null;
            pending.Clear();
            sending = null;
            nextHit = acknowledged = 0;
            sampledAt = 0;
            foreach (var old in rosters.Keys.Where(old => old.destroyed).ToArray())
                rosters.Remove(old);
        }
        scope = new LootLevel(player.Level, player.LevelSeed, player.Layout);
        var roster = GetRoster(current);
        if (host)
        {
            if (peer?.Enemies.Hits is { } hits && hits.Level == scope && scope.Matches(peer.Player))
                ApplyHits(hero, roster, hits, peer.Player!);
            PublishWorld(current, roster);
            return new EnemySync(World: world);
        }

        if (peer?.Enemies.World is { } incoming && incoming.Level == scope && scope.Matches(peer.Player))
        {
            while (pending.TryPeek(out var hit) && hit.Sequence <= incoming.Acknowledged)
            {
                pending.Dequeue();
                sending = null;
            }
            Discover(current, roster);
            ApplyWorld(current, roster, incoming);
            lastWorld = incoming;
        }
        if (pending.Count == 0)
            return null;
        sending ??= new EnemyHits(scope, pending.Take(EnemyHits.MaxHits).ToArray());
        return new EnemySync(Hits: sending);
    }

    private void ApplyHits(Hero hero, Roster roster, EnemyHits batch, PlayerState peer)
    {
        foreach (var hit in batch.Hits)
        {
            if (hit.Sequence <= acknowledged)
                continue;
            acknowledged = hit.Sequence;
            if (!roster.ByKey.TryGetValue(hit.Target, out var entry) ||
                entry.Mob is not { destroyed: false, life: > 0 } mob || !IsEnemy(mob))
                continue;
            var dx = (mob.cx + mob.xr) * 24 - peer.X;
            var dy = (mob.cy + mob.yr) * 24 - peer.Y;
            if (dx * dx + dy * dy > 1152 * 1152)
                continue;

            var attack = new AttackData().init(players.Partner ?? (Entity)hero, (double)hit.Damage);
            attack.lastHitTarget = mob;
            attack.finalDmg = hit.Damage;
            attack.hitResult = hit.Critical ? new HitResult.Critical() : new HitResult.Normal();
            directionOverride = hit.Direction;
            try { mob.applyAttackResult(attack); }
            finally { directionOverride = 0; }
        }
    }

    private void Discover(Level current, Roster roster)
    {
        var entities = current.entities;
        for (var i = 0; i < entities.length; i++)
        {
            if (entities.getDyn(i) is Mob { destroyed: false } mob && !roster.ByMob.ContainsKey(mob) && IsEnemy(mob))
                Register(current, mob, mob.type?.ToString() ?? "", mob.cx, mob.cy, 0, mob.trueLifeTier);
        }
    }

    private void PublishWorld(Level current, Roster roster)
    {
        var now = Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(sampledAt, now).TotalMilliseconds < 75)
            return;
        sampledAt = now;
        Discover(current, roster);
        var poses = new List<EnemyState>(roster.ByKey.Count);
        foreach (var enemy in roster.ByKey.Values)
        {
            if (!enemy.Mob.destroyed && !IsEnemy(enemy.Mob))
                continue;
            if (ReadPose(enemy) is { IsValid: true } pose)
            {
                enemy.Pose = pose;
                poses.Add(pose);
            }
        }
        if (world is null || world.Acknowledged != acknowledged || !world.Enemies.SequenceEqual(poses))
            world = new EnemyWorld(scope!, acknowledged, poses.ToArray());
    }

    private static EnemyState? ReadPose(Enemy enemy)
    {
        var mob = enemy.Mob;
        if (mob.destroyed && enemy.Pose is { } previous)
            return previous with { Life = 0, Removed = mob.life > 0, Impact = enemy.Impact };
        if (mob.maxLife <= 0)
            return null;
        var sprite = mob.spr;
        var animation = sprite?.groupName?.ToString();
        return new EnemyState(enemy.Key, sprite?.x ?? (mob.cx + mob.xr) * 24, sprite?.y ?? (mob.cy + mob.yr) * 24,
            mob.dir < 0 ? -1 : 1, mob.destroyed ? 0 : Math.Clamp(mob.life, 0, mob.maxLife), mob.maxLife,
            EnemyState.ValidName(animation) ? animation : null, sprite?.frame ?? 0,
            enemy.DamageTier, enemy.LifeTier, mob.destroyed && mob.life > 0, enemy.Impact);
    }

    private Enemy? CreateReplica(Level current, Roster roster, EnemyState pose)
    {
        var key = pose.Key;
        var name = key.Kind.AsHaxeString();
        if (key.CellX >= current.map.wid || key.CellY >= current.map.hei || dc.Data.Class.mob.byId.get(name) is null)
            return null;
        var lifeTier = pose.LifeTier;
        spawningReplica = true;
        try
        {
            var mob = Mob.Class.create.Invoke(name, current, key.CellX, key.CellY, pose.DamageTier, Ref<int>.From(ref lifeTier));
            if (mob is null)
                return null;
            if (!mob.initDone)
                mob.init();
            var entry = new Enemy(key, mob, pose.DamageTier, pose.LifeTier);
            roster.Add(entry);
            return entry;
        }
        finally { spawningReplica = false; }
    }

    private void ApplyWorld(Level current, Roster roster, EnemyWorld incoming)
    {
        foreach (var pose in incoming.Enemies)
        {
            if (!roster.ByKey.TryGetValue(pose.Key, out var entry))
            {
                if (pose.Life == 0 || CreateReplica(current, roster, pose) is not { } created)
                    continue;
                entry = created;
            }
            if (entry.Mob is not { destroyed: false } mob)
                continue;

            var dx = pose.X - (mob.cx + mob.xr) * 24;
            var dy = pose.Y - (mob.cy + mob.yr) * 24;
            var blend = pose.Life == 0 || dx * dx + dy * dy > 192 * 192 ? 1 : 0.55;
            mob.setPosPixel(pose.X - dx * (1 - blend), pose.Y - dy * (1 - blend));
            mob.updatePositionXY();
            mob.dir = pose.Direction;
            mob.maxLife = pose.MaxLife;
            mob.life = pose.Life;
            mob.spriteUpdate();

            if (pose.Impact is { } impact && impact.Revision > entry.AppliedImpact)
            {
                entry.AppliedImpact = impact.Revision;
                PlayImpact(current, pose, impact);
#if COOP_QA
                ConfirmedImpacts++;
#endif
            }
            if (pose.Life == 0)
            {
                if (!pose.Removed && mob.spr is not null)
                    current.fx.mobDeath(mob);
                mob.destroy();
#if COOP_QA
                MirroredDeaths++;
#endif
                continue;
            }

            if (mob.spr is { } sprite)
            {
                if (pose.Animation is { } name && sprite.groupName?.ToString() != name)
                {
                    var group = name.AsHaxeString();
                    if (sprite.lib.groups.exists(group))
                        sprite._animManager?.play(group, null, null)?.loop(null);
                }
                if (sprite.frame != pose.Frame && pose.Frame < sprite.totalFrames())
                    sprite.setFrame(pose.Frame);
            }
        }
    }

    private static void PlayImpact(Level level, EnemyState pose, EnemyImpact impact) =>
        level.fx.hitLines(pose.X, pose.Y - 10, impact.Direction, impact.Critical ? 0xFFD76A : 0xFFF4DB,
            null, Ref<int>.Null, Ref<int>.Null);
}
