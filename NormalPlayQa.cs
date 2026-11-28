#if COOP_QA
using System;
using System.Diagnostics;
using Math = System.Math;
using dc.en;
using dc.hxd;
using dc.pr;
using HaxeProxy.Runtime;

namespace DeadCellsMultiplayerMod;

internal sealed class NormalPlayQa(Action<string> report)
{
    private const int Confirm = 13;
    private const int Left = 37;
    private const int Right = 39;
    private const int Jump = 32;
    private const int Dodge = 16;
    private const int Pause = 27;
    private const int Attack = 49;
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
                // This also exercises the empty weapon slot when no equipment has been collected.
                Press(Attack, 100);
                Advance();
                break;
            case 6:
                if (age < 300)
                    break;
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
                if (Game.Class.ME?.paused == false && held == 0 && peerJumped && peerJumpRendered &&
                    peerRollRendered && players.RemoteSounds > soundCount)
                {
                    report("COOP_QA NORMAL resumed. Partner jump, dodge and movement sound observed.");
                    return true;
                }
                if (age > 2800)
                    Fail($"partner playback missing. Paused={Game.Class.ME?.paused} JumpInput={peerJumped} JumpSprite={peerJumpRendered} RollSprite={peerRollRendered} Audio={players.RemoteSounds - soundCount}");
                break;
        }
        return false;
    }

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
