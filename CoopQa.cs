#if COOP_QA
using System;
using System.Diagnostics;
using File = System.IO.File;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using dc.en;
using dc.en.inter;
using dc.en.loot;
using dc.en.mob;
using dc.hl.types;
using dc.level;
using dc.tool;
using dc.tool.atk;
using HaxeProxy.Runtime;
using ModCore.Utilities;

namespace DeadCellsMultiplayerMod;

internal sealed class CoopQa(bool host, string mode, string? stopSignal, Action<string> report)
{
    private readonly NormalPlayQa? normal = mode is "normal" or "effects" or "equipment" or "summons" ? new NormalPlayQa(report, mode) : null;
    private static readonly string[] TestWeaponIds = ["RustySword", "TwinDaggers", "BloodSword", "Bow", "WoodShield",
        "BroadSword", "Torch", "IceBow", "Shovel", "Rapier", "BalancedBlade", "AssassinDagger"];
    private PlayerState? beginning;
    private PlayerState? remoteBeginning;
    private long readyAt;
    private long movedAt;
    private double targetX;
    private bool moved;
    private bool finished;
    private bool holdPoseAfterPass;
    private long lastProbe;
    private bool movementPassed;
    private int exitX;
    private int exitY;
    private long nextShutdownCheck;
    private long shutdownRequested;
    private ShopReroll? testShop;
    private ShopKey? shopKey;
    private int lastObservedUses = 2;
    private bool sentRoll;
    private long lootQueuedAt;
    private long receiptAt;
    private int dropX;
    private int dropY;
    private string? dropWeapon;
    private string? dropFood;
    private long pickedAt;
    private int moneyBeforePickup;
    private long presentationReadyAt;
    private long calledStepAt;
    private int heardBeforeStep;
    private long combatStartedAt;
    private long clientHitSeenAt;
    private int combatStep;
    private EnemyKey? combatKey;
    private Zombie? attackingZombie;
    private int initialCombatLife;
    private bool zombieTargetedGuest;
    private bool testedNativeStrike;
    private long lastBossHit;
    private long bossPhaseSeenAt;
    private int initialBossPhase;
    private dc.en.inter.door.BossDoor? bossGate;
    private bool bossFixtureGate;
    private long revivalStartedAt;
    private long reviveKeyAt;
    private long lastRevivePress;
    private long secondRevivalAt;
    private int revivalStep;
    private Hero? testedHero;

    internal bool CheckForShutdown()
    {
        normal?.UpdateInput();
        if (reviveKeyAt != 0 && Stopwatch.GetElapsedTime(reviveKeyAt).TotalMilliseconds >= 150)
        {
            NormalPlayQa.Dispatch(82, new dc.hxd.EventKind.EKeyUp());
            reviveKeyAt = 0;
        }
        var now = Stopwatch.GetTimestamp();
        if (stopSignal is null || now < nextShutdownCheck)
            return shutdownRequested != 0;
        nextShutdownCheck = now + Stopwatch.Frequency / 4;
        if (!File.Exists(stopSignal))
            return false;

        if (shutdownRequested == 0)
        {
            shutdownRequested = now;
            report("COOP_QA closing game.");
        }

        // The native save/quit hook may defer closure during a transition.
        if (dc.Main.Class.ME.onExit() || Stopwatch.GetElapsedTime(shutdownRequested, now).TotalSeconds >= 5)
            dc.hxd.System.Class.exit.Invoke();
        return true;
    }

    internal void DriveLobby(bool canConfirm, Action shortcut)
    {
        if (normal is not null)
            normal.Lobby(canConfirm);
        else if (canConfirm)
            shortcut();
    }

    internal DropReceipt? OutgoingReceipt(DropReceipt? receipt)
    {
        if (mode != "loot" || receipt is null)
            return receipt;
        if (receiptAt == 0)
            receiptAt = Stopwatch.GetTimestamp();
        return Stopwatch.GetElapsedTime(receiptAt).TotalMilliseconds >= 500 ? receipt : null;
    }

    internal void Tick(Hero? hero, PlayerState? remote, NativePlayers players, NativeLoot loot, NativeEnemies enemies,
        NativeRevival revival)
    {
        if (NativePlayers.Capture(hero) is not { } local)
            return;
        if (finished)
        {
            if (holdPoseAfterPass && local.Level == beginning!.Level)
                HoldPose(hero!, local);
            return;
        }
        if (remote is null || local.Level != remote.Level)
            return;

        if (normal is not null)
        {
            if (normal.Update(hero!, local, remote, players))
                Complete(hero!, local);
            return;
        }

        if (mode == "shop")
        {
            TickShop(hero!, local, remote);
            return;
        }
        if (mode == "loot")
        {
            TickLoot(hero!, local, loot);
            return;
        }
        if (mode == "presentation")
        {
            TickPresentation(hero!, local, remote, players);
            return;
        }
        if (mode == "combat")
        {
            TickCombat(hero!, local, remote, enemies);
            return;
        }
        if (mode == "enemy-target")
        {
            TickEnemyTarget(hero!, local, remote, players, enemies);
            return;
        }
        if (mode == "boss")
        {
            TickBoss(hero!, local, enemies);
            return;
        }
        if (mode == "revive")
        {
            TickRevival(hero!, local, remote, players, revival);
            return;
        }

        if (movementPassed)
        {
            if (local.Level != beginning!.Level && players.ReplicaAt(remote))
                Complete(hero!, local);
            return;
        }

        if (beginning is null || remoteBeginning is null)
        {
            if (!players.ReplicaAt(remote))
                return;
            beginning = local;
            remoteBeginning = remote;
            readyAt = Stopwatch.GetTimestamp();
            report(FormattableString.Invariant($"COOP_QA ready. Local=({local.X:F1},{local.Y:F1}) Remote=({remote.X:F1},{remote.Y:F1})"));
            return;
        }

        if (!moved && Stopwatch.GetElapsedTime(readyAt).TotalSeconds >= 2)
        {
            targetX = local.X + (host ? 24 : 48);
            moved = true;
            movedAt = Stopwatch.GetTimestamp();
            report(FormattableString.Invariant($"COOP_QA moving toward X={targetX:F1}."));
            return;
        }

        if (!moved)
            return;

        // Keep the scripted pose until both clients have had time to receive it.
        HoldPose(hero!, local);

        var close = players.ReplicaAt(remote);
        if (Math.Abs(local.X - beginning.X) < 5 || Math.Abs(remote.X - remoteBeginning.X) < 5 || !close)
        {
            var now = Stopwatch.GetTimestamp();
            if (Stopwatch.GetElapsedTime(lastProbe, now).TotalSeconds >= 4)
            {
                lastProbe = now;
                report(FormattableString.Invariant($"COOP_QA waiting. Local=({local.X:F1},{local.Y:F1}) Remote=({remote.X:F1},{remote.Y:F1}) ReplicaNear={close}"));
            }
            return;
        }
        if (Stopwatch.GetElapsedTime(movedAt).TotalSeconds < 2)
            return;

        if (mode != "exit")
        {
            holdPoseAfterPass = true;
            Complete(hero!, local);
            return;
        }

        var entities = hero!._level.entities;
        Exit? door = null;
        for (var i = 0; i < entities.length; i++)
        {
            if (entities.getDyn(i) is not Exit { destroyed: false } candidate ||
                string.IsNullOrEmpty(candidate.destLevel?.ToString()))
                continue;
            if (door is null || candidate.cx < door.cx || candidate.cx == door.cx && candidate.cy < door.cy)
                door = candidate;
        }

        if (door is null)
        {
            finished = true;
            report($"COOP_QA FAIL: no normal exit in {local.Level}.");
            return;
        }

        movementPassed = true;
        exitX = door.cx;
        exitY = door.cy;
        hero.setPosCase(exitX, exitY, 0.5, 0.9);
        report($"COOP_QA activating exit ({exitX},{exitY}) to {door.destLevel}.");
        door.onActivate(hero, true);
    }

    private void TickCombat(Hero hero, PlayerState local, PlayerState remote, NativeEnemies enemies)
    {
        if (combatStartedAt == 0)
        {
            if (hero.controlsLocked(Ref<bool>.Null) || Math.Abs(local.X - remote.X) > 96)
                return;
            combatStartedAt = Stopwatch.GetTimestamp();
            if (host)
            {
                var level = hero._level;
                var x = Math.Clamp(hero.cx + 3, 4, level.map.wid - 5);
                var y = Math.Clamp(hero.cy - 1, 2, level.map.hei - 2);
                var zombie = new Zombie(level, x, y, 1, 1);
                zombie.init();
                zombie.life = zombie.maxLife = 100;
            }
        }
        var frame = enemies.ObservedWorld;
        var state = frame?.Enemies.FirstOrDefault(enemy => combatKey is null
            ? enemy.Key.Kind == "Zombie" && enemy.MaxLife == 100
            : enemy.Key == combatKey);
        if (state is not null)
        {
            combatKey = state.Key;
            var mob = enemies.FindForQa(state.Key);
            switch (combatStep)
            {
                case 0 when state.Life == 100 && mob is { destroyed: false, life: 100 }:
                    if (!host)
                    {
                        var dx = (mob.cx + mob.xr) * 24 - state.X;
                        var dy = (mob.cy + mob.yr) * 24 - state.Y;
                        if (dx * dx + dy * dy > 24 * 24)
                            return;
                        DealQaHit(hero, mob, 30);
                        if (mob.life != 100)
                        {
                            finished = true;
                            report("COOP_QA FAIL: client changed enemy health before the host confirmed the hit.");
                            return;
                        }
                    }
                    report("COOP_QA COMBAT Spawn=True Life=100");
                    combatStep++;
                    break;
                case 1 when state.Life == 70 && state.Impact is { Revision: 1, Damage: 30 } &&
                                 mob is { destroyed: false, life: 70 }:
                    report("COOP_QA COMBAT ClientHit=30 Life=70");
                    clientHitSeenAt = Stopwatch.GetTimestamp();
                    combatStep++;
                    break;
                case 2 when host && Stopwatch.GetElapsedTime(clientHitSeenAt).TotalMilliseconds > 600 &&
                                 mob is { destroyed: false, life: 70 }:
                    DealQaHit(hero, mob, 20);
                    combatStep++;
                    break;
                case 2 or 3 when state.Life == 50 && state.Impact is { Revision: 2, Damage: 20 } &&
                                      mob is { destroyed: false, life: 50 }:
                    report("COOP_QA COMBAT HostHit=20 Life=50");
                    if (!host)
                        DealQaHit(hero, mob, 80);
                    combatStep = 4;
                    break;
                case 4 when state is { Life: 0, Removed: false, Impact.Revision: 3 } &&
                                 mob is { destroyed: true }:
                    if (host || enemies is { ConfirmedImpacts: 3, MirroredDeaths: 1 })
                    {
                        report("COOP_QA COMBAT Death=True FinalLife=0 Impacts=3");
                        Complete(hero, local);
                        return;
                    }
                    break;
            }
        }
        if (Stopwatch.GetElapsedTime(combatStartedAt).TotalSeconds > 14)
        {
            finished = true;
            report($"COOP_QA FAIL: combat step={combatStep} state={state} mobs={frame?.Enemies.Length}" +
                $" ack={frame?.Acknowledged} impacts={enemies.ConfirmedImpacts} deaths={enemies.MirroredDeaths}.");
        }
    }

    private void TickEnemyTarget(Hero hero, PlayerState local, PlayerState remote, NativePlayers players, NativeEnemies enemies)
    {
        if (combatStartedAt == 0)
        {
            if (players.Partner is not { } partner || hero.controlsLocked(Ref<bool>.Null))
                return;
            combatStartedAt = Stopwatch.GetTimestamp();
            initialCombatLife = hero.life;
            if (host)
            {
                var level = hero._level;
                hero.set_targetable(false);
                var cell = Math.Clamp(partner.cx + 1, 3, level.map.wid - 4);
                attackingZombie = new Zombie(level, cell, partner.cy, 1, 1);
                attackingZombie.init();
                attackingZombie.setPosPixel((partner.cx + partner.xr) * 24 + 18, (partner.cy + partner.yr) * 24);
                attackingZombie.updatePositionXY();
                attackingZombie.hasGravity = false;
                attackingZombie.dir = -1;
                attackingZombie.isAlwaysUpdated = true;
            }
        }
        if (host && !testedNativeStrike && players.Partner is { _targetable: true } guest &&
            Stopwatch.GetElapsedTime(combatStartedAt).TotalSeconds >= 1)
        {
            testedNativeStrike = true;
            attackingZombie!.setAttackTarget(guest);
            zombieTargetedGuest = ReferenceEquals(attackingZombie.aTarget, guest);
            AttackUtils.Class.createFromMobAndHit.Invoke(attackingZombie, 11.0, null, guest);
        }
        var passed = host
            ? zombieTargetedGuest && enemies.GuestStrikes > 0 && enemies.GuestStrikeReceipt > 0 && hero.life == initialCombatLife
            : enemies.GuestStrikes > 0 && hero.life > 0 && hero.life < initialCombatLife;
        if (passed)
        {
            report(host ? "COOP_QA ENEMY_TARGET HostTarget=True StrikeAcknowledged=True" :
                $"COOP_QA ENEMY_TARGET ClientHealthBefore={initialCombatLife} After={hero.life}");
            Complete(hero, local);
        }
        else if (Stopwatch.GetElapsedTime(combatStartedAt).TotalSeconds > 12)
        {
            finished = true;
            report($"COOP_QA FAIL: enemy target selected={zombieTargetedGuest} mobTarget={attackingZombie?.aTarget?.GetType().Name}" +
                $" targetable={players.Partner?._targetable} mob=({attackingZombie?.cx},{attackingZombie?.cy})" +
                $" client=({remote.X / 24:F2},{remote.Y / 24:F2}) strike={enemies.GuestStrikes}" +
                $" received={enemies.GuestStrikeReceipt} life={hero.life}/{initialCombatLife}" +
                $" guestOnTeam={players.Partner?._team?.members?.length}.");
        }
    }

    private void TickBoss(Hero hero, PlayerState local, NativeEnemies enemies)
    {
        if (local.Level != "Bridge")
        {
            finished = true;
            report($"COOP_QA FAIL: the boss fixture expected Black Bridge, got {local.Level}.");
            return;
        }
        if (combatStartedAt == 0)
        {
            combatStartedAt = Stopwatch.GetTimestamp();
            hero.toggleFullInvincibility(true);
        }
        var state = enemies.ObservedWorld?.Enemies.FirstOrDefault(enemy => enemy.Key.Kind == "Behemoth");
        if (state is not null && enemies.FindForQa(state.Key) is dc.en.mob.boss.Behemoth boss && state.Boss is { } progress)
        {
            if (combatStep == 0)
            {
                if (bossGate is null)
                {
                    var level = hero._level;
                    if (level.entitiesByClass.get(dc.en.inter.door.BossDoor.Class.__clid) is ArrayObj gates)
                        bossGate = Enumerable.Range(0, gates.length)
                            .Select(index => gates.getDyn(index)).OfType<dc.en.inter.door.BossDoor>()
                            .OrderByDescending(door => door.cx).FirstOrDefault();
                    if (bossGate is null)
                    {
                        bossFixtureGate = true;
                        bossGate = new dc.en.inter.door.BossDoor(level, boss.cx + 8, boss.cy, false);
                        if (!bossGate.initDone)
                            bossGate.init();
                        bossGate.locked = true;
                    }
                }
                if (host && !boss.isReady() && !hero._level.game.hasCinematic())
                {
                    boss.isAlwaysUpdated = true;
                    boss.setReady();
                }
                if (boss.isReady() && progress.EncounterStarted && state.Life > 0 && bossGate is not null &&
                    !hero._level.game.hasCinematic())
                {
                    report($"COOP_QA BOSS Ready=True Life={state.Life} FixtureGate={bossFixtureGate}");
                    initialBossPhase = progress.Phase;
                    combatStep = 1;
                    lastBossHit = Stopwatch.GetTimestamp();
                }
            }
            else if (combatStep == 1 && !host && !testedNativeStrike &&
                     Stopwatch.GetElapsedTime(lastBossHit).TotalMilliseconds >= 600)
            {
                testedNativeStrike = true;
                hero.setPosPixel(state.X - 60, state.Y);
                hero.updatePositionXY();
                hero.spriteUpdate();
                DealQaHit(hero, boss, Math.Max(1, state.MaxLife / 5));
                report($"COOP_QA BOSS sent guest attack from ({hero.cx},{hero.cy}).");
            }
            else if (combatStep == 1 && state.Impact is { Revision: >= 1 } && state.Life < state.MaxLife &&
                     boss.life == state.Life)
            {
                report($"COOP_QA BOSS ClientHit=True Life={boss.life}");
                combatStep = 2;
                lastBossHit = Stopwatch.GetTimestamp();
            }
            else if (combatStep == 2 && progress.Phase > initialBossPhase && boss.bossLevel == progress.Phase)
            {
                report($"COOP_QA BOSS Phase={boss.bossLevel}");
                combatStep = 3;
                bossPhaseSeenAt = Stopwatch.GetTimestamp();
            }
            else if (combatStep == 3 && progress.Defeated && boss.life == 0 && boss.onDieDone &&
                     bossGate is { locked: false })
            {
                report("COOP_QA BOSS Defeated=True GateUnlocked=True");
                Complete(hero, local);
                return;
            }

            if (host && combatStep == 2 && state.Life > state.MaxLife / 2 &&
                Stopwatch.GetElapsedTime(lastBossHit).TotalMilliseconds >= 450)
            {
                lastBossHit = Stopwatch.GetTimestamp();
                DealQaHit(hero, boss, Math.Max(1, state.MaxLife / 5));
            }
            else if (host && combatStep == 2 && state.Life <= state.MaxLife / 2 && boss.bossLevel == initialBossPhase &&
                     Stopwatch.GetElapsedTime(lastBossHit).TotalSeconds >= 4)
            {
                boss.bossLevelUp();
                report("COOP_QA BOSS advanced phase using the native boss method.");
            }
            else if (host && combatStep == 3 && boss.life > 0 && !progress.Defeated &&
                     Stopwatch.GetElapsedTime(bossPhaseSeenAt).TotalMilliseconds >= 1200)
            {
                boss.kill();
            }
        }
        if (Stopwatch.GetElapsedTime(combatStartedAt).TotalSeconds > 26)
        {
            finished = true;
            report($"COOP_QA FAIL: boss step={combatStep} state={state} gateLocked={bossGate?.locked}" +
                $" target={(state is null ? null : enemies.FindForQa(state.Key)?.aTarget?.GetType().Name)}" +
                $" hero=({hero.cx},{hero.cy})" +
                $" cinematic={hero._level.game.curCine?.GetType().Name}.");
        }
    }

    private static void DealQaHit(Hero hero, dc.en.Mob mob, int damage)
    {
        var attack = new AttackData().init(hero, (double)damage);
        attack.lastHitTarget = mob;
        attack.finalDmg = damage;
        attack.hitResult = new dc.HitResult.Normal();
        mob.applyAttackResult(attack);
    }

    private void TickRevival(Hero hero, PlayerState local, PlayerState remote, NativePlayers players, NativeRevival revival)
    {
        if (revivalStartedAt == 0)
        {
            if (hero.controlsLocked(Ref<bool>.Null) || !players.ReplicaAt(remote) ||
                Math.Abs(local.X - remote.X) > 48 || Math.Abs(local.Y - remote.Y) > 48)
                return;
            revivalStartedAt = Stopwatch.GetTimestamp();
            testedHero = hero;
            if (host)
            {
                // Start below the game's one-hit protection threshold.
                hero.setLifeAndRally(1, 0);
                var damage = hero.maxLife;
                var attack = new AttackData().init(hero, (double)damage);
                attack.lastHitTarget = hero;
                attack.finalDmg = damage;
                attack.hitResult = new dc.HitResult.Normal();
                hero.applyAttackResult(attack);
            }
        }

        var down = revival.IsDown(hero);
        var revived = !down && hero.life == Math.Max(1, (int)((long)hero.maxLife * 30 / 100));
        var freshMatch = ReferenceEquals(hero, testedHero) && !hero.destroyed;
        if (!freshMatch || (down && (!hero.controlsLocked(Ref<bool>.Null) || hero.life != 1)))
        {
            finished = true;
            report($"COOP_QA FAIL: downed hero state SameHero={freshMatch} Locked={hero.controlsLocked(Ref<bool>.Null)} Life={hero.life}.");
            return;
        }

        switch (revivalStep)
        {
            case 0 when host && down:
                revivalStep = 1;
                break;
            case 0 when !host && remote.Downed > 0 && players.PartnerShowsDowned:
                if (TryPressRevive(hero, local, remote))
                    revivalStep = 1;
                break;
            case 1 when host && revived:
                report("COOP_QA REVIVAL HostRevived=True");
                revivalStep = 2;
                break;
            case 1 when !host && remote.Downed == 0 && !players.PartnerShowsDowned:
                report("COOP_QA REVIVAL HostRevived=True");
                hero.kill();
                revivalStep = 2;
                break;
            case 1 when !host && remote.Downed > 0:
                TryPressRevive(hero, local, remote);
                break;
            case 2 when host && remote.Downed > 0 && players.PartnerShowsDowned:
                if (TryPressRevive(hero, local, remote))
                    revivalStep = 3;
                break;
            case 2 when !host && revived:
                report("COOP_QA REVIVAL ClientRevived=True");
                revivalStep = 4;
                break;
            case 3 when host && remote.Downed == 0 && !players.PartnerShowsDowned:
                report("COOP_QA REVIVAL ClientRevived=True");
                secondRevivalAt = Stopwatch.GetTimestamp();
                revivalStep = 4;
                break;
            case 3 when host && remote.Downed > 0:
                TryPressRevive(hero, local, remote);
                break;
            case 4 when host && Stopwatch.GetElapsedTime(secondRevivalAt).TotalMilliseconds >= 2600:
                hero.kill();
                revivalStep = 5;
                break;
            case 4 when !host && remote.Downed > 0 && players.PartnerShowsDowned:
                hero.kill();
                revivalStep = 5;
                break;
            case 5 when down && remote.Downed > 0:
                revivalStep = 6;
                break;
            case 6 when revived && !hero.controlsLocked(Ref<bool>.Null) &&
                             remote.Downed == 0 && !players.PartnerShowsDowned:
                report("COOP_QA REVIVAL BothRecovered=True SameHero=True");
                Complete(hero, local);
                return;
        }

        if (Stopwatch.GetElapsedTime(revivalStartedAt).TotalSeconds > 30)
        {
            finished = true;
            report($"COOP_QA FAIL: revival step={revivalStep} localDown={down} partnerDown={remote.Downed}" +
                $" partnerOutline={players.PartnerShowsDowned} health={hero.life}/{hero.maxLife}" +
                $" locked={hero.controlsLocked(Ref<bool>.Null)} awake={hero.awake} unconscious={hero.isUnconscious()}" +
                $" cinematic={hero._level.game.curCine?.GetType().Name ?? "none"} distance=({local.X - remote.X:F1},{local.Y - remote.Y:F1}).");
        }
    }

    private bool TryPressRevive(Hero hero, PlayerState local, PlayerState remote)
    {
        if (hero.controlsLocked(Ref<bool>.Null) || Stopwatch.GetElapsedTime(lastRevivePress).TotalMilliseconds < 500)
            return false;
        if (Math.Abs(local.X - remote.X) > 48 || Math.Abs(local.Y - remote.Y) > 48)
        {
            hero.setPosPixel(remote.X, remote.Y);
            hero.cancelVelocities();
            hero.updatePositionXY();
            hero.spriteUpdate();
            return false;
        }
        NormalPlayQa.Dispatch(82, new dc.hxd.EventKind.EKeyDown());
        lastRevivePress = reviveKeyAt = Stopwatch.GetTimestamp();
        return true;
    }

    private void Complete(Hero hero, PlayerState local)
    {
        finished = true;
        report($"COOP_QA PASS Level={local.Level} Seed={hero._level.game.data.gameSeed}");
    }

    private void TickShop(Hero hero, PlayerState local, PlayerState remote)
    {
        if (testShop is null)
        {
            var level = hero._level;
            var room = level.map.getRoomAt(hero.cx, hero.cy);
            if (room is null || room.wid < 14)
                return;
            // The native helper creates the concrete HashLink stock records used by ShopReroll.
            var generator = HaxeProxyUtils.AsHaxe<LootGen>(HaxeProxyUtils.GetHashlinkType(typeof(LootGen)).CreateInstance());
            var pool = (ArrayObj)level.entities.slice(0, 0);
            var available = TestWeaponIds.Select(id => id.AsHaxeString())
                .Where(id => dc.Data.Class.item.byId.get(id) is not null).Take(3).ToArray();
            for (var itemLevel = 2; itemLevel <= 3; itemLevel++)
            {
                foreach (var name in available)
                {
                    var item = new InventItem(new InventItemKind.Weapon(name));
                    item.setItemLevel(itemLevel);
                    generator._addToShop(pool, item, null, null);
                }
            }
            if (pool.length != 6)
            {
                finished = true;
                report($"COOP_QA FAIL: shop fixture created {pool.length} of 6 items.");
                return;
            }
            if (!host)
                pool.reverse();
            room.merchant = new MerchantData(new MerchantType.Weapons(), pool, false);
            testShop = new ShopReroll(level, room.x + 10, hero.cy, room.x + 1);
            testShop.remainUses = 2;
            testShop.init();
            shopKey = NativeShops.GetKey(level, testShop);
            if (shopKey is null)
            {
                finished = true;
                report("COOP_QA FAIL: shop fixture is outside its room.");
                return;
            }
            report($"COOP_QA shop ready room={shopKey.Room} at ({shopKey.X},{shopKey.Y}).");
        }

        var key = shopKey!;
        hero.setPosCase(key.X, key.Y, 0.5, 0.5);
        if (Math.Abs(remote.X / 24 - key.X) > 2 || Math.Abs(remote.Y / 24 - key.Y) > 2)
            return;

        if (testShop.remainUses < lastObservedUses && NativeShops.CaptureBooths(hero._level, key.Room) is { Length: 3 } currentStock)
        {
            lastObservedUses = testShop.remainUses;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(currentStock);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            report($"COOP_QA SHOP Revision={2 - lastObservedUses} Uses={lastObservedUses} Stock={hash}");
            if (lastObservedUses == 0)
            {
                Complete(hero, local);
                return;
            }
        }

        if (!sentRoll && lastObservedUses == (host ? 2 : 1))
        {
            sentRoll = true;
            testShop.onActivate(hero, true);
        }
    }

    private void HoldPose(Hero hero, PlayerState local)
    {
        if (Math.Abs(local.X - targetX) <= 2)
            return;
        hero.setPosPixel(targetX, local.Y);
        hero.updatePositionXY();
        hero.spriteUpdate();
    }

    private void TickPresentation(Hero hero, PlayerState local, PlayerState remote, NativePlayers players)
    {
        hero.dir = host ? 1 : -1;
        if (remote.Direction != (host ? -1 : 1) || !players.ReplicaShowsPartner(remote))
            return;
        if (presentationReadyAt == 0)
        {
            presentationReadyAt = Stopwatch.GetTimestamp();
            heardBeforeStep = players.RemoteSounds;
            report($"COOP_QA replica outlined and facing {remote.Direction}, visual group {remote.SpriteGroup}.");
        }
        var age = Stopwatch.GetElapsedTime(presentationReadyAt).TotalSeconds;
        if (calledStepAt == 0 && age >= 2)
        {
            hero.playStepSound();
            calledStepAt = Stopwatch.GetTimestamp();
            return;
        }
        if (calledStepAt == 0 || Stopwatch.GetElapsedTime(calledStepAt).TotalSeconds < 0.8)
            return;
        var presentationSounds = players.RemoteSounds - heardBeforeStep;
        if (presentationSounds == 0 && Stopwatch.GetElapsedTime(calledStepAt).TotalSeconds < 4)
            return;
        if (presentationSounds != 1 || !dc.Main.Class.ME.options.mute)
        {
            finished = true;
            report($"COOP_QA FAIL: expected one partner step and muted output, heard {presentationSounds} sound cues. Last={players.LastRemoteSound}.");
            return;
        }
        var (near, middle, farSilent) = players.ProbeSoundDistance(hero, remote);
        if (near <= 0 || Math.Abs(middle / near - 0.5) >= 0.001 || !farSilent)
        {
            finished = true;
            report($"COOP_QA FAIL: movement sound falloff near={near} middle={middle} farSilent={farSilent}.");
            return;
        }
        report(FormattableString.Invariant($"COOP_QA AUDIO Near={near:F3} Middle={middle:F3} FarSilent={farSilent}"));
        report($"COOP_QA PRESENTATION Outline=True Direction={remote.Direction} Sound={players.LastRemoteSound} Muted=True");
        Complete(hero, local);
    }

    private void TickLoot(Hero hero, PlayerState local, NativeLoot loot)
    {
        var level = hero._level;
        if (lootQueuedAt == 0)
        {
            var weapon = TestWeaponIds.Select(id => id.AsHaxeString())
                .FirstOrDefault(id => dc.Data.Class.item.byId.get(id) is not null);
            var nativeFood = LootGen.Class.getSmallHealingKind.Invoke();
            var food = new[] { nativeFood, "SmallFood".AsHaxeString(), "Chicken".AsHaxeString(), "Croissant".AsHaxeString() }
                .FirstOrDefault(id => id is not null && dc.Data.Class.item.byId.get(id) is not null);
            if (weapon is null || food is null)
            {
                finished = true;
                report($"COOP_QA FAIL: missing native loot fixture Weapon={weapon} Food={food} DefaultFood={nativeFood}.");
                return;
            }
            dropWeapon = weapon.ToString();
            dropFood = food.ToString();
            dropX = Math.Clamp(hero.cx + 9, 4, level.map.wid - 5);
            dropY = Math.Clamp(level.map.getGroundY(dropX, hero.cy), 2, level.map.hei - 2);
            var drops = (ArrayObj)level.entities.slice(0, 0);
            if (host)
            {
                drops.pushDyn(new dc.LootType.MoneyGold(371));
                drops.pushDyn(new dc.LootType.MoneyGem(317));
                drops.pushDyn(new dc.LootType.GenericCell());
                drops.pushDyn(new dc.LootType.HealDrop(0.25));
                var item = new InventItem(new InventItemKind.Weapon(weapon));
                item.setItemLevel(7);
                drops.pushDyn(new dc.LootType.InvItem(item));
                drops.pushDyn(new dc.LootType.InvItem(new InventItem(new InventItemKind.Consumable(food))));
                level.dropMobLoots(dropX, dropY, drops, 2.0);
                var duplicateValues = (ArrayObj)level.entities.slice(0, 0);
                duplicateValues.pushDyn(new dc.LootType.MoneyGold(371));
                level.dropMobLoots(dropX, dropY, duplicateValues, 2.0);
            }
            else
            {
                drops.pushDyn(new dc.LootType.MoneyGold(913));
                level.dropMobLoots(dropX, dropY, drops, 2.0);
            }
            report($"COOP_QA loot queued at ({dropX},{dropY}). NativeQueue={level.newLoots.length}.");
            lootQueuedAt = Stopwatch.GetTimestamp();
            return;
        }

        if (Stopwatch.GetElapsedTime(lootQueuedAt).TotalSeconds < 2 ||
            (host ? loot.Frame is not null : loot.Receipt?.Sequence != 2))
            return;

        var ore = 0;
        GoldOre? oreToPick = null;
        var gem = 0;
        var cell = 0;
        var heal = 0;
        var weaponCount = 0;
        var foodCount = 0;
        var clientOnly = 0;
        var entities = level.entities;
        for (var i = 0; i < entities.length; i++)
        {
            switch (entities.getDyn(i))
            {
                case GoldOre { destroyed: false, value: 371 } gold:
                    ore++;
                    oreToPick = gold;
                    break;
                case Gem { destroyed: false, value: 317 }: gem++; break;
                case GenericCell { destroyed: false } entity when Math.Abs(entity.cx - dropX) <= 12: cell++; break;
                case HealDrop { destroyed: false, healPct: 0.25 }: heal++; break;
                case GoldOre { destroyed: false, value: 913 }: clientOnly++; break;
                case ItemDrop { destroyed: false } entity when Math.Abs(entity.cx - dropX) <= 12:
                    var id = entity.item.getItemKind()?.ToString();
                    if (id == dropWeapon && entity.item.getRawItemLevel() == 7)
                        weaponCount++;
                    if (id == dropFood)
                        foodCount++;
                    break;
            }
        }
        var counts = $"Gold={ore} Gem={gem} Cells={cell} Heal={heal} Weapon={weaponCount} Food={foodCount} ClientOnly={clientOnly}";
        if (pickedAt != 0)
        {
            if (Stopwatch.GetElapsedTime(pickedAt).TotalMilliseconds < 500)
                return;
            var received = level.game.data.money - moneyBeforePickup;
            var orePaid = received - (gem == 0 ? 317 : 0);
            if (orePaid != 371 || ore != 1 || gem > 1)
            {
                finished = true;
                report($"COOP_QA FAIL: pickup paid {received} and left {ore} ores and {gem} gems.");
                return;
            }
            report($"COOP_QA PICKUP Paid={orePaid} RemainingGold={ore}");
            Complete(hero, local);
            return;
        }
        if (ore != 2 || gem != 1 || cell != 1 || heal != 1 || weaponCount != 1 || foodCount != 1 || clientOnly != 0)
        {
            if (Stopwatch.GetElapsedTime(lootQueuedAt).TotalSeconds < 12 && clientOnly == 0)
                return;
            finished = true;
            var nearby = Enumerable.Range(0, entities.length).Select(entities.getDyn).OfType<Loot>()
                .Where(entity => !entity.destroyed).Take(25).Select(entity => $"{entity.GetType().Name}@{entity.cx},{entity.cy}");
            report($"COOP_QA FAIL: loot counts {counts} NativeQueue={level.newLoots.length}. Observed {string.Join(" ", nearby)}.");
            return;
        }
        report($"COOP_QA LOOT {counts}");
        moneyBeforePickup = level.game.data.money;
        oreToPick!.setPosCase(hero.cx, hero.cy, hero.xr, hero.yr);
        oreToPick.pickUpdate();
        pickedAt = Stopwatch.GetTimestamp();
    }
}
#endif
