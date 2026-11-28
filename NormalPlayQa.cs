#if COOP_QA
using System;
using System.Diagnostics;
using Math = System.Math;
using dc.en;
using dc.hxd;
using dc.pr;
using dc.tool;
using HaxeProxy.Runtime;
using ModCore.Utilities;

namespace DeadCellsMultiplayerMod;

internal sealed class NormalPlayQa(Action<string> report, string mode)
{
    private const int Confirm = 13;
    private const int Left = 37;
    private const int Right = 39;
    private const int Jump = 32;
    private const int Dodge = 16;
    private const int Pause = 27;
    private const int Attack = 49;
    private const int LeftSkill = 81;
    private const int RightSkill = 69;
    private readonly bool verifyEffects = mode is "effects" or "equipment";
    private int held;
    private long releaseAt;
    private long lastConfirm;
    private long stageStarted;
    private double startX;
    private double startY;
    private int stage;
    private int soundCount;
    private bool reversed;
    private bool peerJumped;
    private bool peerRollRendered;
    private bool peerJumpRendered;
    private bool fixtureEquipped;
    private bool actionSent;
    private bool smokeTested;
    private int projectileShots;
    private static readonly (string Item, EquipmentKind Kind)[] EquipmentFixtures =
        [("StartBow", EquipmentKind.Projectile), ("ExplosiveGrenade", EquipmentKind.Grenade),
         ("HorizontalTurret", EquipmentKind.Deployable), ("Owl", EquipmentKind.Companion)];

    internal void UpdateInput()
    {
        if (held != 0 && Stopwatch.GetTimestamp() >= releaseAt)
            Release();
    }

    internal void Lobby(bool canConfirm)
    {
        if (canConfirm && held == 0 && Stopwatch.GetElapsedTime(lastConfirm).TotalMilliseconds >= 750)
        {
            lastConfirm = Stopwatch.GetTimestamp();
            Press(Confirm, 100);
        }
    }

    internal bool Update(Hero hero, PlayerState local, PlayerState remote, NativePlayers players)
    {
        if (stage < 0)
            return false;
        var age = Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds;
        peerJumped |= remote.Animation == "jump";
        var replicaGroup = players.ReplicaAnimation;
        peerJumpRendered |= replicaGroup?.Contains("jump", StringComparison.OrdinalIgnoreCase) == true;
        peerRollRendered |= replicaGroup?.Contains("roll", StringComparison.OrdinalIgnoreCase) == true;
        switch (stage)
        {
            case 0:
                if (!players.ReplicaAt(remote) || hero.controlsLocked(Ref<bool>.Null) ||
                    local.SpriteGroup?.Contains("rebirth", StringComparison.OrdinalIgnoreCase) == true)
                    break;
                startX = local.X;
                soundCount = players.RemoteSounds;
                Advance();
                Press(Right, 1500);
                report($"COOP_QA NORMAL controls ready at ({hero.cx},{hero.cy}).");
                break;
            case 1:
                if (Math.Abs(local.X - startX) >= 48)
                {
                    report($"COOP_QA NORMAL ran {Math.Abs(local.X - startX):F0} px.");
                    startY = local.Y;
                    Advance();
                    Press(Jump, 180);
                }
                else if (age >= 1600 && !reversed)
                {
                    reversed = true;
                    stageStarted = Stopwatch.GetTimestamp();
                    Press(Left, 1500);
                }
                else if (age >= 1600)
                    Fail($"walk did not move the hero from {startX:F0}");
                break;
            case 2:
                if (local.Y < startY - 15 && local.Animation == "jump")
                {
                    report("COOP_QA NORMAL jumped.");
                    Advance();
                }
                else if (age >= 2000)
                    Fail("jump did not leave the ground");
                break;
            case 3:
                if (age < 850 || local.Animation is "jump" or "fall")
                    break;
                Advance();
                Press(Dodge, 150);
                break;
            case 4:
                if (local.SpriteGroup?.Contains("roll", StringComparison.OrdinalIgnoreCase) == true)
                {
                    report($"COOP_QA NORMAL dodged with {local.SpriteGroup}.");
                    Advance();
                }
                else if (age > 1800)
                    Fail($"dodge did not start, sprite group {local.SpriteGroup}");
                break;
            case 5:
                if (age < 500)
                    break;
                if (verifyEffects && !Equip(hero, "StartSword"))
                    break;
                Press(EquippedKey(hero, "StartSword", false), 100);
                Advance();
                break;
            case 6:
                if (age < 300)
                    break;
                if (verifyEffects)
                {
                    if (!HasLand(players.Effects.Captured))
                        hero.onLand(0.5);
                    hero._level.fx.dash(hero, hero.dir, 0x80FF, Ref<int>.Null, Ref<double>.Null);
                    Advance();
                    Advance();
                    break;
                }
                Press(Pause, 110);
                Advance();
                break;
            case 7:
                if (Game.Class.ME?.paused == true && held == 0)
                {
                    report("COOP_QA NORMAL paused.");
                    Press(Pause, 110);
                    Advance();
                }
                else if (age > 2000)
                    Fail("pause menu did not open");
                break;
            case 8:
                if (verifyEffects)
                {
                    if (!VerifyEffects(players.Effects, age))
                        break;
                    if (mode == "effects")
                        return true;
                    Advance();
                    break;
                }
                if (Game.Class.ME?.paused == false && held == 0 && peerJumped && peerJumpRendered &&
                    peerRollRendered && players.RemoteSounds > soundCount)
                {
                    report("COOP_QA NORMAL resumed. Partner jump, dodge and movement sound observed.");
                    return true;
                }
                if (age > 2800)
                    Fail($"partner playback missing. Paused={Game.Class.ME?.paused} JumpInput={peerJumped} JumpSprite={peerJumpRendered} RollSprite={peerRollRendered} Audio={players.RemoteSounds - soundCount}");
                break;
            case 9:
                if (age < 700)
                    break;
                if (!fixtureEquipped)
                {
                    fixtureEquipped = Equip(hero, "StartShield");
                    stageStarted = Stopwatch.GetTimestamp();
                    break;
                }
                Press(EquippedKey(hero, "StartShield", false), 100);
                Advance();
                break;
            case 10:
                if (age < 850)
                    break;
                var effects = players.Effects;
                var shield = effects.Captured[(int)EffectKind.ShieldParry] && effects.Played[(int)EffectKind.ShieldParry];
                if (shield)
                {
                    report("COOP_QA EQUIPMENT ShieldParry=True");
                    hero._level.fx.healing(hero, 0x48FF5B);
                    fixtureEquipped = false;
                    Advance();
                }
                else if (age >= 1400 && !actionSent)
                {
                    actionSent = true;
                    Press(EquippedKey(hero, "StartShield", false), 100);
                }
                else if (age > 4500)
                    Fail($"shield parry missing. Sprite={local.SpriteGroup} Sent={string.Join(',', effects.Captured)} Received={string.Join(',', effects.Played)}");
                break;
            default:
                var fixture = (stage - 11) / 2;
                if (fixture >= EquipmentFixtures.Length)
                {
                    if (!smokeTested)
                    {
                        if (!fixtureEquipped)
                        {
                            fixtureEquipped = Equip(hero, "SmokeBomb", active: true);
                            stageStarted = Stopwatch.GetTimestamp();
                            break;
                        }
                        if (age < 450)
                            break;
                        Press(EquippedKey(hero, "SmokeBomb", active: true), 420);
                        smokeTested = true;
                        stageStarted = Stopwatch.GetTimestamp();
                        break;
                    }
                    var visible = players.Effects.Played;
                    if (!visible[(int)EffectKind.Heal] || !visible[(int)EffectKind.Deflagration] ||
                        !visible[(int)EffectKind.Smoke] || !players.PlayedEquipmentSound)
                    {
                        if (age > 4500)
                            Fail($"equipment effects missing. Received={string.Join(',', visible)} Sound={players.PlayedEquipmentSound}");
                        break;
                    }
                    report("COOP_QA EQUIPMENT Heal=True Explosion=True Smoke=True Sound=True");
                    return true;
                }
                var (item, kind) = EquipmentFixtures[fixture];
                var isActive = kind != EquipmentKind.Projectile;
                if (stage % 2 == 1)
                {
                    if (!fixtureEquipped)
                    {
                        fixtureEquipped = Equip(hero, item, isActive);
                        stageStarted = Stopwatch.GetTimestamp();
                        break;
                    }
                    if (age < 450)
                        break;
                    Press(EquippedKey(hero, item, isActive), 420);
                    Advance();
                }
                else if (age >= 1600 && players.Equipment.Captured.Contains(kind) && players.Equipment.Rendered.Contains(kind))
                {
                    report($"COOP_QA EQUIPMENT Item={item} Kind={kind} Captured=True Rendered=True");
                    fixtureEquipped = false;
                    Advance();
                }
                else if (kind == EquipmentKind.Projectile && projectileShots < 2 &&
                         age >= 1100 * (projectileShots + 1))
                {
                    projectileShots++;
                    hero.dir = -hero.dir;
                    Press(EquippedKey(hero, item, false), 180);
                }
                else if (age > 6500)
                    Fail($"{item} sprite missing. Sent={string.Join(',', players.Equipment.Captured)} Received={string.Join(',', players.Equipment.Rendered)}");
                break;
        }
        return false;
    }

    private bool Equip(Hero hero, string item, bool active = false)
    {
        var id = item.AsHaxeString();
        if (!active && hero.inventory.getEquippedWeaponOn(0)?.getItemKind()?.ToString() == item)
            return true;
        if (dc.Data.Class.item.byId.get(id) is null)
        {
            Fail($"{item} is unavailable");
            return false;
        }
        hero.inventory.removeAllEquipments();
        var equipment = hero.inventory.add(new InventItem(active ? new InventItemKind.Active(id) : new InventItemKind.Weapon(id)));
        if (!hero.inventory.equip(equipment))
        {
            Fail($"{item} could not be equipped");
            return false;
        }
        hero.onEquipedItemsChange(Ref<bool>.Null, Ref<bool>.Null, Ref<bool>.Null);
        report($"COOP_QA EQUIPPING Item={item} Key={EquippedKey(hero, item, active)}");
        return true;
    }

    private static int EquippedKey(Hero hero, string item, bool active)
    {
        var inventory = hero.inventory;
        var second = active ? inventory.getActiveOn(1) : inventory.getEquippedWeaponOn(1);
        if (second?.getItemKind()?.ToString() == item)
            return active ? LeftSkill : 50;
        return active ? RightSkill : Attack;
    }

    private bool VerifyEffects(NativePlayerEffects effects, double age)
    {
        static bool HasActions(bool[] events) =>
            HasLand(events) && events[(int)EffectKind.Dodge] && events[(int)EffectKind.Dash] && events[(int)EffectKind.Slash];
        if (HasActions(effects.Captured) && HasActions(effects.Played) && effects.LastTrailGroup is { } group)
        {
            report($"COOP_QA EFFECTS Land=True Dodge=True Dash=True StarterSword={group}");
            return true;
        }
        if (age > 4500)
            Fail($"effects missing. Sent={string.Join(',', effects.Captured)} Received={string.Join(',', effects.Played)} Trail={effects.LastTrailGroup}");
        return false;
    }

    private static bool HasLand(bool[] events) =>
        events[(int)EffectKind.LandSmoke] || events[(int)EffectKind.LandDirt] || events[(int)EffectKind.LandHeavy];

    private void Advance()
    {
        stage++;
        stageStarted = Stopwatch.GetTimestamp();
    }

    private void Fail(string problem)
    {
        stage = -1;
        Release();
        report($"COOP_QA FAIL: normal play {problem}.");
    }

    private void Press(int key, int milliseconds)
    {
        Release();
        Dispatch(key, new EventKind.EKeyDown());
        held = key;
        releaseAt = Stopwatch.GetTimestamp() + Stopwatch.Frequency * milliseconds / 1000;
    }

    private void Release()
    {
        if (held == 0)
            return;
        Dispatch(held, new EventKind.EKeyUp());
        held = 0;
    }

    private static void Dispatch(int key, EventKind kind)
    {
        var input = new Event(kind, Ref<double>.Null, Ref<double>.Null) { keyCode = key };
        Window.Class.getInstance.Invoke().@event(input);
    }
}
#endif
