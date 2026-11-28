using System.Diagnostics;
using Xunit;

namespace DeadCellsMultiplayerMod;

public sealed class RemoteMotionTests
{
    [Fact]
    public void SmoothsUnevenPacketsBoundsPredictionAndSnapsTeleports()
    {
        var motion = new RemoteMotion();
        var pose = new PlayerState("PrisonStart", 0, 10, 1, "run");
        motion.Add(pose, At(1000));
        motion.Add(pose with { X = 30, Opacity = 0.5 }, At(1050));
        motion.Add(pose with { X = 78, Opacity = 0.25 }, At(1130));
        Assert.Equal(54, motion.Render(At(1190)).X, 3);
        Assert.Equal(0.375, motion.Render(At(1190)).Opacity, 3);
        Assert.Equal(126, motion.Render(At(1550)).X, 3);
        Assert.Equal(126, motion.Render(At(2000)).X, 3);
        motion.Add(pose with { X = 1000 }, At(2100));
        motion.Add(pose with { X = 80 }, At(1140));
        Assert.Equal(1000, motion.Render(At(2100)).X, 3);
    }

    [Fact]
    public void SoundPathsStayInsideRecognizedGameAssets()
    {
        var sound = new MovementSound(1, "sfx/hero/step1.wav", 50, 20, null);
        Assert.True(sound.IsValid);
        Assert.False((sound with { Path = "../step1.wav" }).IsValid);
        Assert.False((sound with { Path = "sfx/music/biome.ogg" }).IsValid);
        Assert.False((sound with { Volume = double.NaN }).IsValid);
        Assert.True((sound with { Path = "sfx/weapon/weapon_swipe_release1.wav", Pitch = 0.9 }).IsValid);
        Assert.False((sound with { Path = "sfx/active/../hero/step1.wav" }).IsValid);
        Assert.False((sound with { Pitch = double.PositiveInfinity }).IsValid);
    }

    [Fact]
    public void EquipmentSnapshotsValidateSpritesBeforeNativePlayback()
    {
        var projectile = new EquipmentVisual(1, EquipmentKind.Projectile, SpriteAtlas.Weapon,
            "arrow", 120, 200, -1, 1, 0.25, 1, 4);
        var player = new PlayerState("PrisonStart", 10, 20, 1, "idle") { Equipment = [projectile] };
        Assert.True(player.IsValid);
        Assert.True((projectile with { Atlas = SpriteAtlas.Cached, AtlasKey = "atlas/heroes/Owl" }).IsValid);
        Assert.False((projectile with { Atlas = SpriteAtlas.Cached, AtlasKey = "../invalid" }).IsValid);
        Assert.False((projectile with { Alpha = double.NaN }).IsValid);
        Assert.False((projectile with { Atlas = (SpriteAtlas)17 }).IsValid);
        Assert.False((player with { Equipment = Enumerable.Repeat(projectile, EquipmentVisual.MaxObjects + 1).ToArray() }).IsValid);
    }

    [Fact]
    public void MovementSoundFadesSmoothlyInTwoDimensionsAndStopsAtRange()
    {
        var sound = new MovementSound(1, "sfx/hero/step1.wav", 0, 0, 0.8);
        Assert.Equal(0.8, sound.VolumeAt(0, 72));
        Assert.Equal(0.4, sound.VolumeAt(0, 372), 6);
        Assert.Equal(sound.VolumeAt(372, 0), sound.VolumeAt(0, 372), 6);
        Assert.InRange(sound.VolumeAt(400, 400), 0.01, 0.2);
        Assert.Equal(0.0, sound.VolumeAt(0, 672));
        Assert.Equal(0.0, sound.VolumeAt(500, 500));
    }

    [Fact]
    public void EffectSnapshotsOnlyAcceptBoundedNativeEffectParameters()
    {
        var slash = new PlayerEffect(1, EffectKind.Slash, 14, -2, Scale: 1.2,
            Trail: new WeaponTrail("starter_trail", -1, 1));
        var player = new PlayerState("PrisonStart", 10, 20, 1, "idle") { Effects = [slash] };
        Assert.True(player.IsValid);
        Assert.False((slash with { X = double.NaN }).IsValid);
        Assert.False((slash with { Trail = new WeaponTrail("../other", 1, 1) }).IsValid);
        Assert.False(new PlayerEffect(2, EffectKind.Dodge, 1, 2, Variant: 4).IsValid);
        var explosion = new PlayerEffect(3, EffectKind.Deflagration, 84, 100, Color: 0x11FF44, Scale: 96, Extra: 1);
        Assert.True(explosion.IsValid);
        Assert.False((explosion with { Scale = 1000 }).IsValid);
        Assert.False((explosion with { Extra = double.NaN }).IsValid);
        Assert.False((player with { Effects = Enumerable.Repeat(slash, PlayerEffect.MaxCues + 1).ToArray() }).IsValid);
    }

    private static long At(int milliseconds) => milliseconds * Stopwatch.Frequency / 1000;
}
