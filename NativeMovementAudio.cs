using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using dc.en;
using dc.hxd.res;
using dc.level;
using dc.level._LevelAudio;
using dc.pr;
using dc.tool.skill;
using ModCore.Utilities;

namespace DeadCellsMultiplayerMod;

internal sealed class NativeMovementAudio
{
    private readonly Queue<(long At, MovementSound Cue)> pending = new(MovementSound.MaxCues);
    private MovementSound[]? outgoing;
    private Hero? capturing;
    private Level? level;
    private int nextSequence;
    private int lastHeard;
#if COOP_QA
    internal int Played { get; private set; }
    internal int OutOfRange { get; private set; }
    internal bool PlayedEquipment { get; private set; }
    internal string? LastAsset { get; private set; }
    internal double LastVolume { get; private set; }
    private MovementSound? lastCue;
#endif

    internal NativeMovementAudio(NativeEquipmentVisuals equipment)
    {
        Hook_Hero.playStepSound += (original, hero) => Capture(hero, () => original(hero));
        Hook_Hero.playClimbSfx += (original, hero) => Capture(hero, () => original(hero));
        Hook_Hero.onLand += (original, hero, velocity) => Capture(hero, () => original(hero, velocity));
        Hook_Hero.landStun += (original, hero, velocity, forced) => Capture(hero, () => original(hero, velocity, forced));
        Hook_LevelAudio.playEvent += OnSound;
        Hook_OldSkill.playSfx += (original, skill, sound, canPlay, force, volume, pitch, priority) =>
        {
            if (skill.owner is Hero hero)
                Capture(hero, () => original(skill, sound, canPlay, force, volume, pitch, priority));
            else
                original(skill, sound, canPlay, force, volume, pitch, priority);
        };
        Hook_LevelAudio.playEventOn += (original, audio, sound, owner, volume, pitch, key) =>
        {
            if ((ReferenceEquals(owner, Game.Class.ME?.hero) || equipment.Owns(owner)) &&
                owner?._level is { } current && owner.spr is { } sprite)
                Record(current, audio, sound, sprite.x, sprite.y, volume, pitch);
            return original(audio, sound, owner, volume, pitch, key);
        };
        Hook_LevelAudio.playEventAt += (original, audio, sound, x, y, volume, pitch, key) =>
        {
            if (capturing?._level is { } current)
                Record(current, audio, sound, x, y, volume, pitch);
            return original(audio, sound, x, y, volume, pitch, key);
        };
    }

    private void Capture(Hero hero, Action original)
    {
        var previous = capturing;
        if (ReferenceEquals(hero, Game.Class.ME?.hero))
            capturing = hero;
        try { original(); }
        finally { capturing = previous; }
    }

    private Event OnSound(Hook_LevelAudio.orig_playEvent original, LevelAudio audio, Sound sound,
        double? volume, double? pitch, dc.String? key)
    {
        if (capturing is { _level: { } current, spr: { } sprite })
            Record(current, audio, sound, sprite.x, sprite.y, volume, pitch);
        return original(audio, sound, volume, pitch, key);
    }

    private void Record(Level current, LevelAudio audio, Sound? sound, double x, double y, double? volume, double? pitch)
    {
        if (!ReferenceEquals(audio, current.lAudio) || sound?.entry?.get_path()?.ToString() is not { } path)
            return;
        var cue = new MovementSound(nextSequence + 1, path, x, y, volume, pitch);
        if (!cue.IsValid)
            return;
        SetLevel(current);
        nextSequence++;
        if (pending.Count == MovementSound.MaxCues)
            pending.Dequeue();
        pending.Enqueue((Stopwatch.GetTimestamp(), cue));
        outgoing = pending.Select(entry => entry.Cue).ToArray();
    }

    internal MovementSound[]? Outgoing(Level current)
    {
        SetLevel(current);
        var updated = false;
        while (pending.TryPeek(out var cue) && Stopwatch.GetElapsedTime(cue.At).TotalMilliseconds > 280)
        {
            updated = true;
            pending.Dequeue();
        }
        if (updated)
            outgoing = pending.Count > 0 ? pending.Select(sound => sound.Cue).ToArray() : null;
        return outgoing;
    }

    internal void ResetPlayback() => lastHeard = 0;

    internal void Play(Level current, PlayerState state, double listenerX, double listenerY)
    {
        if (state.Sounds is not { } cues || current.lAudio is not { } audio)
            return;
        foreach (var cue in cues)
        {
            if (cue.Sequence <= lastHeard)
                continue;
            lastHeard = cue.Sequence;
            var volume = cue.VolumeAt(listenerX, listenerY);
            if (volume <= 0)
            {
#if COOP_QA
                OutOfRange++;
#endif
                continue;
            }
            var path = cue.Path.AsHaxeString();
            var loader = dc.hxd.Res.Class.get_loader.Invoke();
            if (loader?.exists(path) != true)
                continue;
            var playback = audio.playEventAt(loader.load(path).toSound(), cue.X, cue.Y, volume, cue.Pitch, null);
#if COOP_QA
            Played++;
            PlayedEquipment |= MovementSound.IsEquipmentAsset(cue.Path);
            LastAsset = cue.Path;
            LastVolume = playback.volume;
            lastCue = cue;
#endif
        }
    }

#if COOP_QA
    internal (double Near, double Middle, bool FarSilent) ProbeDistance(Level current, PlayerState state)
    {
        var cue = lastCue!;
        void Probe(double offset) => Play(current, state with { Sounds = [cue with { Sequence = lastHeard + 1 }] },
            cue.X + offset, cue.Y);

        Probe(0);
        var near = LastVolume;
        Probe(372);
        var middle = LastVolume;
        var previousPlayed = Played;
        var previousSilent = OutOfRange;
        Probe(720);
        return (near, middle, Played == previousPlayed && OutOfRange == previousSilent + 1);
    }
#endif

    private void SetLevel(Level current)
    {
        if (ReferenceEquals(level, current))
            return;
        level = current;
        pending.Clear();
        outgoing = null;
    }
}
