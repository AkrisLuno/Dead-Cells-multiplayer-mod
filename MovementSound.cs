using System;
using System.Linq;

namespace DeadCellsMultiplayerMod;

internal sealed record MovementSound(int Sequence, string Path, double X, double Y, double? Volume)
{
    private const double FullVolumeDistance = 72;
    private const double SilentDistance = 672;

    internal bool IsValid => Sequence > 0 && IsMovementAsset(Path) &&
                             Math.Abs(X) <= 50000 && Math.Abs(Y) <= 50000 &&
                             Volume is null or >= 0 and <= 2;

    internal double VolumeAt(double listenerX, double listenerY)
    {
        var dx = X - listenerX;
        var dy = Y - listenerY;
        var distanceSquared = dx * dx + dy * dy;
        if (!(distanceSquared < SilentDistance * SilentDistance))
            return 0;
        var gain = Volume ?? 1;
        if (distanceSquared <= FullVolumeDistance * FullVolumeDistance)
            return gain;

        var fade = (Math.Sqrt(distanceSquared) - FullVolumeDistance) / (SilentDistance - FullVolumeDistance);
        return gain * (1 - fade * fade * (3 - 2 * fade));
    }

    private static bool IsMovementAsset(string? path)
    {
        if (path is not { Length: > 0 and <= 128 } || path.StartsWith('/') || path.Contains("..", StringComparison.Ordinal) ||
            path.Contains("//", StringComparison.Ordinal) ||
            !path.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '_' or '-' or '.') ||
            !(path.EndsWith(".wav", StringComparison.Ordinal) || path.EndsWith(".ogg", StringComparison.Ordinal)))
            return false;
        var name = path.AsSpan(path.LastIndexOf('/') + 1);
        return name.StartsWith("step", StringComparison.Ordinal) ||
               name.StartsWith("climb", StringComparison.Ordinal) ||
               name.StartsWith("armor_step", StringComparison.Ordinal) ||
               name.SequenceEqual("land.wav") || name.SequenceEqual("hero_fall.wav") || name.SequenceEqual("hero_stun.wav");
    }
}
