using System;
using System.Diagnostics;
using dc;
using dc.en;
using dc.pr;
using ModCore.Utilities;
using Math = System.Math;

namespace DeadCellsMultiplayerMod;

internal sealed class NativeRevival
{
    private const int InteractAction = 6;
    private const int ReviveDistance = 96;
    private const int ReviveHealthPercent = 30;
    private readonly bool host;
    private readonly Action<string> log;
    private Hero? downed;
    private Hero? protectedHero;
    private PlayerState? nearbyPartner;
    private int deathSequence;
    private int reviveFor;
    private int promptedFor;
    private long bothDownAt;
    private long protectedUntil;
    private bool hasPartner;
    private bool interactWasPressed;

    internal NativeRevival(bool host, Action<string> log)
    {
        this.host = host;
        this.log = log;
        Hook_Hero.onDie += (original, hero) =>
        {
            if (!TryDown(hero))
                original(hero);
        };
        Hook_Entity.kill += (original, entity) =>
        {
            if (entity is not Hero hero || !TryDown(hero))
                original(entity);
        };
        Hook_Hero.controlsLocked += (original, hero, fromInput) =>
            ReferenceEquals(hero, downed) || original(hero, fromInput);
        Hook_Hero.canReceiveAttack += (original, hero, attack) =>
            !IsProtected(hero) && original(hero, attack);
        Hook_Hero.applyAttackResult += (original, hero, attack) =>
        {
            if (!IsProtected(hero))
                original(hero, attack);
        };
        Hook_Hero.updateInteraction += (original, hero) =>
        {
            if (ReferenceEquals(hero, downed))
                return;
            var presses = hero.controller?.parent?.framePresses;
            var pressed = presses is { length: > InteractAction } && presses.getDyn(InteractAction) is 1;
            var newlyPressed = pressed && !interactWasPressed;
            interactWasPressed = pressed;
            if (newlyPressed && RequestRevival(hero))
                return;
            original(hero);
        };
    }

    internal bool IsDown(Hero? hero) => downed is not null && ReferenceEquals(downed, hero);

    private bool IsProtected(Hero hero) => IsDown(hero) ||
        ReferenceEquals(hero, protectedHero) && Stopwatch.GetTimestamp() < protectedUntil;

    private bool TryDown(Hero hero)
    {
        if (!hasPartner || hero._level is null || !ReferenceEquals(hero, Game.Class.ME?.hero))
            return false;
        if (IsDown(hero))
        {
            hero.setLifeAndRally(1, 0);
            return true;
        }
        downed = hero;
        deathSequence++;
        reviveFor = 0;
        bothDownAt = 0;
        hero.setLifeAndRally(1, 0);
        hero.cleanDOT();
        hero.interruptHeroSkills();
        hero.cancelVelocities();
        hero.lockControlsS(1);
        hero.popText("Downed. Your partner can revive you.".AsHaxeString(), 0xFFC477);
        log($"Co-op player downed #{deathSequence}.");
        return true;
    }

    private bool RequestRevival(Hero hero)
    {
        if (nearbyPartner is not { Downed: > 0 } partner || IsDown(hero) || hero.destroyed ||
            !ReferenceEquals(hero, Game.Class.ME?.hero) || hero.controlsLocked(HaxeProxy.Runtime.Ref<bool>.Null) ||
            !WithinRange((hero.cx + hero.xr) * 24, (hero.cy + hero.yr) * 24, partner.X, partner.Y))
            return false;
        reviveFor = partner.Downed;
        return true;
    }

    internal PlayerState? Update(Hero? hero, PlayerState? local, PlayerState? remote)
    {
        if (local is null || hero is null)
            return local;
        if (downed is not null && !ReferenceEquals(hero, downed))
            downed = null;
        if (IsDown(hero))
        {
            hero.lockControlsS(1);
            if (hero.life != 1)
                hero.setLifeAndRally(1, 0);
        }

        var matching = remote is not null && local.Level == remote.Level &&
                       local.LevelSeed == remote.LevelSeed && local.Layout == remote.Layout;
        nearbyPartner = matching && WithinRange(local.X, local.Y, remote!.X, remote.Y) ? remote : null;
        if (matching)
            hasPartner = true;
        if (matching && remote!.Downed != reviveFor)
            reviveFor = 0;

        var now = Stopwatch.GetTimestamp();
        if (host && IsDown(hero) && matching && remote!.Downed > 0)
        {
            if (bothDownAt == 0)
                bothDownAt = now;
            if (Stopwatch.GetElapsedTime(bothDownAt, now).TotalSeconds >= 4)
            {
                reviveFor = remote.Downed;
                Restore(hero);
            }
        }
        else
            bothDownAt = 0;

        if (IsDown(hero) && matching && remote!.ReviveFor == deathSequence &&
            (!host || nearbyPartner is { Downed: 0 }))
            Restore(hero);

        if (!IsDown(hero) && nearbyPartner is { Downed: > 0 } partner && promptedFor != partner.Downed)
        {
            promptedFor = partner.Downed;
            hero.popText("Interact to revive your partner".AsHaxeString(), 0xFFC477);
        }
        if (nearbyPartner is null)
            promptedFor = 0;

        return local with { Downed = IsDown(hero) ? deathSequence : 0, ReviveFor = reviveFor };
    }

    private void Restore(Hero hero)
    {
        downed = null;
        protectedHero = hero;
        protectedUntil = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 2;
        hero.setLifeAndRally(Math.Max(1, (int)((long)hero.maxLife * ReviveHealthPercent / 100)), 0);
        hero.unlockControls();
        hero._level.fx.healing(hero, 0x73FFC2);
        hero.popText("Revived!".AsHaxeString(), 0x73FFC2);
        log($"Co-op player revived #{deathSequence}.");
    }

    private static bool WithinRange(double x, double y, double otherX, double otherY)
    {
        var dx = x - otherX;
        var dy = y - otherY;
        return dx * dx + dy * dy <= ReviveDistance * ReviveDistance;
    }
}
