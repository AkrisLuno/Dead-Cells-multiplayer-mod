using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using dc;
using dc.en;
using dc.libs.heaps.slib;
using dc.pr;
using dc.tool.mainSkills;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using Math = System.Math;

namespace DeadCellsMultiplayerMod;

internal sealed class NativePlayerEffects
{
    private readonly Queue<(long At, PlayerEffect Cue)> pending = new(PlayerEffect.MaxCues);
    private PlayerEffect[]? outgoing;
    private Level? level;
    private Hero? capturing;
    private Hero? equipmentOwner;
    private long lastShieldGuard;
    private int nextSequence;
    private int lastPlayed;
#if COOP_QA
    internal bool[] Captured { get; } = new bool[Enum.GetValues<EffectKind>().Length];
    internal bool[] Played { get; } = new bool[Enum.GetValues<EffectKind>().Length];
    internal string? LastTrailGroup { get; private set; }
#endif

    internal NativePlayerEffects(NativeEquipmentVisuals equipment)
    {
        Hook_Hero.onLand += (original, hero, speed) => Capture(hero, () => original(hero, speed));
        Hook_Dodge.onStart += (original, skill) => Capture(skill.hero, () => original(skill));
        Hook_DodgeRoll.onStart += OnRoll;
        Hook_DiveAttack.onOwnerLand += (original, skill, speed) => Capture(skill.hero, () => original(skill, speed));
        Hook_Grenade.fixedUpdate += (original, grenade) =>
        {
            if (equipment.Owns(grenade))
                CaptureEquipment(Game.Class.ME?.hero, () => original(grenade));
            else
                original(grenade);
        };
        Hook_Bullet.fixedUpdate += (original, bullet) =>
        {
            if (equipment.Owns(bullet))
                CaptureEquipment(Game.Class.ME?.hero, () => original(bullet));
            else
                original(bullet);
        };
        Hook_Active.fixedUpdate += (original, active) =>
        {
            if (equipment.Owns(active))
                CaptureEquipment(Game.Class.ME?.hero, () => original(active));
            else
                original(active);
        };
        dc.pow.Hook_SmokeBomb.finishCast += (original, skill) =>
            CaptureEquipment(skill.owner as Hero, () => original(skill));

        Hook_Fx.landSmoke += (original, fx, x, y, count) =>
        {
            original(fx, x, y, count);
            Record(capturing, fx, new(0, EffectKind.LandSmoke, x, y, count ?? -1));
        };
        Hook_Fx.landDirt += (original, fx, x, y, large) =>
        {
            var choice = large.IsNull ? -1 : large.value ? 1 : 0;
            original(fx, x, y, large);
            Record(capturing, fx, new(0, EffectKind.LandDirt, x, y, choice));
        };
        Hook_Fx.landHeavy += (original, fx, x, y, count, color) =>
        {
            original(fx, x, y, count, color);
            Record(capturing, fx, new(0, EffectKind.LandHeavy, x, y, count ?? -1, color));
        };
        Hook_Fx.diveAttack += (original, fx, x, y, color, scale) =>
        {
            var size = scale.IsNull ? 1 : scale.value;
            original(fx, x, y, color, scale);
            Record(capturing, fx, new(0, EffectKind.Dive, x, y, Color: color, Scale: size));
        };
        Hook_Fx.dodgeStart += (original, fx, x, y, dir, color) =>
        {
            original(fx, x, y, dir, color);
            Record(capturing, fx, new(0, EffectKind.Dodge, x, y, dir, color));
        };
        Hook_Fx.dash += (original, fx, owner, dir, color, offset, scale) =>
        {
            var dx = offset.IsNull ? 0 : offset.value;
            var size = scale.IsNull ? 1 : scale.value;
            original(fx, owner, dir, color, offset, scale);
            Record(owner as Hero, fx, new(0, EffectKind.Dash, dx, 0, dir, color, Scale: size));
        };
        Hook_Fx.playWeaponAnimFromObject += (original, fx, attach, strike, speed, x, y, custom, inner, outer) =>
        {
            var sprite = original(fx, attach, strike, speed, x, y, custom, inner, outer);
            if (Game.Class.ME?.hero is { spr: { } root } hero && ReferenceEquals(attach, root))
            {
                var props = strike.fxProps;
                RecordTrail(hero, fx, sprite, speed * Math.Abs(strike.animSpd ?? 1),
                    inner ?? props?.fxInnerColor, outer ?? props?.fxOuterColor);
            }
            return sprite;
        };
        Hook_Fx.playWeaponFx += (original, fx, owner, group, speed, offset, inner, outer) =>
        {
            var rate = speed.IsNull ? 1 : speed.value;
            var sprite = original(fx, owner, group, speed, offset, inner, outer);
            RecordTrail(owner as Hero, fx, sprite, rate, inner, outer);
            return sprite;
        };
        Hook_Fx.frontShield += (original, fx, owner, power, color) =>
        {
            original(fx, owner, power, color);
            if (!ReferenceEquals(owner, Game.Class.ME?.hero))
                return;
            var now = Stopwatch.GetTimestamp();
            if (Stopwatch.GetElapsedTime(lastShieldGuard, now).TotalMilliseconds < 140)
                return;
            lastShieldGuard = now;
            Record(owner as Hero, fx, new(0, EffectKind.ShieldGuard, 0, 0, Color: color, Scale: power));
        };
        Hook_Fx.shieldParryStart += (original, fx, owner, color, variant) =>
        {
            original(fx, owner, color, variant);
            Record(owner as Hero, fx, new(0, EffectKind.ShieldParry, 0, 0, variant ?? -1, color));
        };
        Hook_Fx.frontShieldHit += (original, fx, owner, color) =>
        {
            original(fx, owner, color);
            Record(owner as Hero, fx, new(0, EffectKind.ShieldHit, 0, 0, Color: color));
        };
        Hook_Fx.healing += (original, fx, owner, color) =>
        {
            original(fx, owner, color);
            Record(owner as Hero, fx, new(0, EffectKind.Heal, 0, 0, Color: color));
        };
        Hook_Fx.flaskHeal += (original, fx, hero, color) =>
        {
            original(fx, hero, color);
            Record(hero, fx, new(0, EffectKind.Heal, 0, 0, Color: color));
        };
        Hook_Fx.radiusExplosion += (original, fx, x, y, radius, color, life) =>
        {
            var duration = life.IsNull ? 0 : life.value;
            original(fx, x, y, radius, color, life);
            Record(equipmentOwner, fx, new(0, EffectKind.Explosion, x, y, Color: color, Scale: radius, Extra: duration));
        };
        Hook_Fx.deflagration += (original, fx, x, y, radius, core, outer, skipDots, intensity) =>
        {
            var withoutDots = !skipDots.IsNull && skipDots.value;
            var force = intensity.IsNull ? 1 : intensity.value;
            original(fx, x, y, radius, core, outer, skipDots, intensity);
            Record(equipmentOwner, fx, new(0, EffectKind.Deflagration, x, y, withoutDots ? 1 : 0, core, outer, radius, Extra: force));
        };
        Hook_Fx.oilDeflagration += (original, fx, x, y, radius) =>
        {
            original(fx, x, y, radius);
            Record(equipmentOwner, fx, new(0, EffectKind.OilBurst, x, y, Scale: radius));
        };
        Hook_Fx.frostExplosion += (original, fx, x, y, direction, radius, color) =>
        {
            original(fx, x, y, direction, radius, color);
            Record(equipmentOwner, fx, new(0, EffectKind.Frost, x, y, direction, color, Scale: radius));
        };
        Hook_Fx.smokeBombGrenade += (original, fx, x, y, radius, color) =>
        {
            original(fx, x, y, radius, color);
            Record(equipmentOwner, fx, new(0, EffectKind.Smoke, x, y, Color: color, Scale: radius));
        };
        Hook_Fx.damageAura += (original, fx, owner, radius, color, alpha) =>
        {
            original(fx, owner, radius, color, alpha);
            Record(owner as Hero, fx, new(0, EffectKind.Aura, 0, 0, Color: color, Scale: radius, Extra: alpha));
        };
        Hook_Fx.entityHorizontalTeleport += (original, fx, owner, fromX, fromY, toX, toY, color) =>
        {
            original(fx, owner, fromX, fromY, toX, toY, color);
            if (owner.spr is { } sprite)
                Record(owner as Hero, fx, new(0, EffectKind.Teleport, fromX - sprite.x, fromY - sprite.y,
                    Color: color, Scale: toX - sprite.x, Extra: toY - sprite.y));
        };
    }

    private void CaptureEquipment(Hero? hero, Action original)
    {
        var previous = equipmentOwner;
        if (hero is not null && ReferenceEquals(hero, Game.Class.ME?.hero))
            equipmentOwner = hero;
        try { original(); }
        finally { equipmentOwner = previous; }
    }

    private void Capture(Hero hero, Action original)
    {
        var previous = capturing;
        if (ReferenceEquals(hero, Game.Class.ME?.hero))
            capturing = hero;
        try { original(); }
        finally { capturing = previous; }
    }

    private void OnRoll(Hook_DodgeRoll.orig_onStart original, DodgeRoll skill)
    {
        var hero = skill.hero;
        var before = nextSequence;
        Capture(hero, () => original(skill));
        if (hero._level?.fx is not { } fx ||
            pending.Any(entry => entry.Cue.Sequence > before && entry.Cue.Kind == EffectKind.Dodge))
            return;
        // Native dodgeStart is a water splash. Give dry rolls a short dust trail for the remote partner.
        Record(hero, fx, new(0, EffectKind.Dodge, (hero.cx + hero.xr) * 24, (hero.cy + hero.yr) * 24, -hero.dir));
    }

    private void RecordTrail(Hero? hero, Fx fx, HSprite? sprite, double speed, int? inner, int? outer)
    {
        if (hero is null || sprite?.groupName?.ToString() is not { } name)
            return;
        Record(hero, fx, new(0, EffectKind.Slash, sprite.x, sprite.y, Color: inner ?? outer, OuterColor: outer,
            Scale: speed, Trail: new WeaponTrail(name, sprite.scaleX, sprite.scaleY)));
    }

    private void Record(Hero? hero, Fx fx, PlayerEffect effect)
    {
        if (hero?._level is not { } current || !ReferenceEquals(hero, Game.Class.ME?.hero) ||
            !ReferenceEquals(current.fx, fx))
            return;
        SetLevel(current);
        var cue = effect with { Sequence = nextSequence + 1 };
        if (!cue.IsValid)
            return;
        nextSequence = cue.Sequence;
        if (pending.Count == PlayerEffect.MaxCues)
            pending.Dequeue();
        pending.Enqueue((Stopwatch.GetTimestamp(), cue));
        outgoing = pending.Select(entry => entry.Cue).ToArray();
#if COOP_QA
        Captured[(int)cue.Kind] = true;
#endif
    }

    internal PlayerEffect[]? Outgoing(Level current)
    {
        SetLevel(current);
        var changed = false;
        while (pending.TryPeek(out var entry) && Stopwatch.GetElapsedTime(entry.At).TotalMilliseconds > 480)
        {
            pending.Dequeue();
            changed = true;
        }
        if (changed)
            outgoing = pending.Count == 0 ? null : pending.Select(entry => entry.Cue).ToArray();
        return outgoing;
    }

    internal void ResetPlayback() => lastPlayed = 0;

    internal void Play(Level current, KingSkin replica, PlayerState pose)
    {
        if (pose.Effects is not { } cues || current.fx is not { } fx)
            return;
        foreach (var cue in cues)
        {
            if (cue.Sequence <= lastPlayed)
                continue;
            lastPlayed = cue.Sequence;
            var amount = cue.Variant == -1 ? (int?)null : cue.Variant;
            switch (cue.Kind)
            {
                case EffectKind.LandSmoke:
                    fx.landSmoke(cue.X, cue.Y, amount);
                    break;
                case EffectKind.LandDirt:
                    var large = cue.Variant == 1;
                    fx.landDirt(cue.X, cue.Y, amount is null ? Ref<bool>.Null : Ref<bool>.From(ref large));
                    break;
                case EffectKind.LandHeavy:
                    fx.landHeavy(cue.X, cue.Y, amount, cue.Color);
                    break;
                case EffectKind.Dive:
                    var diveSize = cue.Scale;
                    fx.diveAttack(cue.X, cue.Y, cue.Color ?? 0xFFFFFF, Ref<double>.From(ref diveSize));
                    break;
                case EffectKind.Dodge:
                    if (cue.Color is { } splash)
                        fx.dodgeStart(cue.X, cue.Y, cue.Variant, splash);
                    else
                        fx.driftDirtAndSmoke(cue.X, cue.Y, cue.Variant);
                    break;
                case EffectKind.Dash:
                    var offset = (int)cue.X;
                    var dashSize = cue.Scale;
                    fx.dash(replica, cue.Variant, cue.Color ?? 0x80FF, Ref<int>.From(ref offset), Ref<double>.From(ref dashSize));
                    break;
                case EffectKind.Slash:
                    if (cue.Trail is not { } trail || replica.spr is not { } owner)
                        continue;
                    var group = trail.Group.AsHaxeString();
                    if (Assets.Class.fxWeapon?.groups?.exists(group) != true)
                        continue;
                    var speed = cue.Scale;
                    if (fx.playWeaponFx(replica, group, Ref<double>.From(ref speed), null, cue.Color, cue.OuterColor) is not { } sprite ||
                        !ReferenceEquals(sprite.parent, owner))
                        continue;
                    sprite.x = cue.X;
                    sprite.y = cue.Y;
                    sprite.scaleX = trail.ScaleX;
                    sprite.scaleY = trail.ScaleY;
#if COOP_QA
                    LastTrailGroup = sprite.groupName?.ToString();
#endif
                    break;
                case EffectKind.ShieldGuard:
                    fx.frontShield(replica, cue.Scale, cue.Color ?? 0xFFFFFF);
                    break;
                case EffectKind.ShieldParry:
                    fx.shieldParryStart(replica, cue.Color ?? 0xFFFFFF, amount);
                    break;
                case EffectKind.ShieldHit:
                    fx.frontShieldHit(replica, cue.Color ?? 0xFFFFFF);
                    break;
                case EffectKind.Heal:
                    fx.healing(replica, cue.Color ?? 0xFFFFFF);
                    break;
                case EffectKind.Explosion:
                    var life = cue.Extra;
                    fx.radiusExplosion(cue.X, cue.Y, cue.Scale, cue.Color ?? 0xFFFFFF,
                        life == 0 ? Ref<double>.Null : Ref<double>.From(ref life));
                    break;
                case EffectKind.Deflagration:
                    var noDots = cue.Variant == 1;
                    var intensity = cue.Extra;
                    fx.deflagration(cue.X, cue.Y, cue.Scale, cue.Color ?? 0xFFFFFF, cue.OuterColor ?? 0xFFFFFF,
                        Ref<bool>.From(ref noDots), Ref<double>.From(ref intensity));
                    break;
                case EffectKind.OilBurst:
                    fx.oilDeflagration(cue.X, cue.Y, cue.Scale);
                    break;
                case EffectKind.Frost:
                    fx.frostExplosion(cue.X, cue.Y, cue.Variant, cue.Scale, cue.Color ?? 0xFFFFFF);
                    break;
                case EffectKind.Smoke:
                    fx.smokeBombGrenade(cue.X, cue.Y, cue.Scale, cue.Color ?? 0xFFFFFF);
                    break;
                case EffectKind.Aura:
                    fx.damageAura(replica, cue.Scale, cue.Color ?? 0xFFFFFF, cue.Extra);
                    break;
                case EffectKind.Teleport:
                    if (replica.spr is not { } position)
                        continue;
                    fx.entityHorizontalTeleport(replica, position.x + cue.X, position.y + cue.Y,
                        position.x + cue.Scale, position.y + cue.Extra, cue.Color ?? 0xFFFFFF);
                    break;
            }
#if COOP_QA
            Played[(int)cue.Kind] = true;
#endif
        }
    }

    private void SetLevel(Level current)
    {
        if (ReferenceEquals(level, current))
            return;
        level = current;
        pending.Clear();
        outgoing = null;
    }
}
