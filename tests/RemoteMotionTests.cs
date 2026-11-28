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
        motion.Add(pose with { X = 30 }, At(1050));
        motion.Add(pose with { X = 78 }, At(1130));
        Assert.Equal(54, motion.Render(At(1190)).X, 3);
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

    private static long At(int milliseconds) => milliseconds * Stopwatch.Frequency / 1000;
}
