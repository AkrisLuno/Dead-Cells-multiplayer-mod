using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DeadCellsMultiplayerMod;

internal sealed class RemoteMotion
{
    private static readonly long PlaybackDelay = Stopwatch.Frequency / 10;
    private const double PredictionSeconds = 0.08;
    private const double MaxSpeed = 1100;
    private readonly List<(PlayerState Pose, long At)> samples = new(12);

    internal void Clear() => samples.Clear();

    internal void Add(PlayerState pose, long at)
    {
        if (samples.Count > 0)
        {
            var previous = samples[^1];
            if (at <= previous.At)
                return;
            var dx = pose.X - previous.Pose.X;
            var dy = pose.Y - previous.Pose.Y;
            if (dx * dx + dy * dy > 192 * 192)
                samples.Clear();
        }
        if (samples.Count == 12)
            samples.RemoveAt(0);
        samples.Add((pose, at));
    }

    internal PlayerState Render(long now)
    {
        var target = now - PlaybackDelay;
        while (samples.Count > 2 && samples[1].At <= target)
            samples.RemoveAt(0);

        var before = samples[0];
        if (samples.Count == 1 || target <= before.At)
            return before.Pose;

        var after = samples[1];
        var interval = Stopwatch.GetElapsedTime(before.At, after.At).TotalSeconds;
        if (target < after.At)
        {
            var blend = Stopwatch.GetElapsedTime(before.At, target).TotalSeconds / interval;
            return before.Pose with
            {
                X = before.Pose.X + (after.Pose.X - before.Pose.X) * blend,
                Y = before.Pose.Y + (after.Pose.Y - before.Pose.Y) * blend
            };
        }

        var prediction = Math.Min(PredictionSeconds, Stopwatch.GetElapsedTime(after.At, target).TotalSeconds);
        return after.Pose with
        {
            X = after.Pose.X + Math.Clamp((after.Pose.X - before.Pose.X) / interval, -MaxSpeed, MaxSpeed) * prediction,
            Y = after.Pose.Y + Math.Clamp((after.Pose.Y - before.Pose.Y) / interval, -MaxSpeed, MaxSpeed) * prediction
        };
    }
}
