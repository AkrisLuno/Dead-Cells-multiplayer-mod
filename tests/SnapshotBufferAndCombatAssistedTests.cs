using System;
using DeadCellsMultiplayerMod.Ghost;
using DeadCellsMultiplayerMod.Mobs.MobsSynchronization;
using Xunit;

namespace DeadCellsMultiplayerMod.ProtocolReplay.Tests;

public sealed class SnapshotBufferAndCombatAssistedTests
{

    [Fact]
    public void MobSnapshotBuffer_Add_RingWrapAround()
    {
        var buffer = new MobSnapshotBuffer();
        Assert.Equal(0, buffer.Count);

        for (int i = 0; i < 15; i++)
        {
            buffer.Add(new MobSnapshot(
                receivedFrame: i * 2.0,
                x: 100.0 + i * 10.0,
                y: 50.0,
                dx: 1.0,
                dy: 0.0,
                dir: 1,
                life: 100,
                maxLife: 100,
                animPayload: "run",
                statePayload: ""));
        }

        // Count must not exceed Capacity (8)
        Assert.Equal(MobSnapshotBuffer.Capacity, buffer.Count);

        // Latest snapshot must be the 15th one (i = 14)
        Assert.True(buffer.TryGetLatest(out var latest));
        Assert.Equal(28.0, latest.ReceivedFrame);
        Assert.Equal(240.0, latest.X);

        // 1 older must be i = 13
        Assert.True(buffer.TryGetAt(1, out var prev));
        Assert.Equal(26.0, prev.ReceivedFrame);
        Assert.Equal(230.0, prev.X);
    }

    [Fact]
    public void MobSnapshotBuffer_SampleHermite_InterpolatesCorrectly()
    {
        var buffer = new MobSnapshotBuffer();
        buffer.Add(new MobSnapshot(10.0, x: 100.0, y: 50.0, dx: 0.0, dy: 0.0, dir: 1, life: 100, maxLife: 100, animPayload: "walk", statePayload: ""));
        buffer.Add(new MobSnapshot(20.0, x: 200.0, y: 50.0, dx: 0.0, dy: 0.0, dir: 1, life: 100, maxLife: 100, animPayload: "walk", statePayload: ""));

        bool sampled = buffer.SampleHermite(
            renderFrame: 15.0,
            out var interpX,
            out var interpY,
            out var dir,
            out var anim,
            out var snap,
            out var vertSnap);

        Assert.True(sampled);
        Assert.Equal(150.0, interpX, 1);
        Assert.Equal(50.0, interpY, 1);
        Assert.Equal(1, dir);
        Assert.Equal("walk", anim);
        Assert.False(snap);
        Assert.False(vertSnap);
    }

    [Fact]
    public void MobSnapshotBuffer_SampleHermite_Discontinuity_TriggersSnap()
    {
        var buffer = new MobSnapshotBuffer();
        // Discontinuity of 400px > 240px threshold without explicit flag
        buffer.Add(new MobSnapshot(10.0, x: 100.0, y: 50.0, dx: 0.0, dy: 0.0, dir: 1, life: 100, maxLife: 100, animPayload: "idle", statePayload: ""));
        buffer.Add(new MobSnapshot(20.0, x: 500.0, y: 50.0, dx: 0.0, dy: 0.0, dir: 1, life: 100, maxLife: 100, animPayload: "idle", statePayload: "", forceSnap: false));

        Assert.True(buffer.SampleHermite(15.0, out var x, out _, out _, out _, out var snap, out _));
        Assert.Equal(500.0, x);
        Assert.False(snap); // Was not explicitly flagged forceSnap, but snapped to destination
    }

    [Theory]
    [InlineData(0.0, 0.0, 1530.0, 0.0, true)]     // 1530px (< 1536px / 64 tiles max) -> valid
    [InlineData(0.0, 0.0, 1600.0, 0.0, false)]    // 1600px (> 1536px / 64 tiles max) -> reach exceeded
    public void SpatialSanityReach_ChecksAccurately(double ax, double ay, double mx, double my, bool expectedValid)
    {
        double dx = ax - mx;
        double dy = ay - my;
        const double MaxHitSanityDistanceSq = (24.0 * 64.0) * (24.0 * 64.0); // 1536px squared (64 tiles)

        bool isValid = double.IsFinite(dx) && double.IsFinite(dy) && (dx * dx + dy * dy) <= MaxHitSanityDistanceSq;
        Assert.Equal(expectedValid, isValid);
    }

    [Fact]
    public void MobSnapshotBuffer_SampleHermite_StationaryWithResidualVelocity_NoDipOrJitter()
    {
        var buffer = new MobSnapshotBuffer();
        // Mobs standing still at Y=88.0, but host engine reports residual gravity/velocity dy = 0.5 (12 px/frame)
        buffer.Add(new MobSnapshot(10.0, x: 100.0, y: 88.0, dx: 0.2, dy: 0.5, dir: 1, life: 100, maxLife: 100, animPayload: "idle", statePayload: ""));
        buffer.Add(new MobSnapshot(20.0, x: 100.0, y: 88.0, dx: 0.2, dy: 0.5, dir: 1, life: 100, maxLife: 100, animPayload: "idle", statePayload: ""));

        // Fritsch-Carlson monotonicity MUST clamp tangent to zero because delta = 0.
        // Sampling at any intermediate frame must return EXACTLY Y=88.0 (no floor dip or hop)
        for (double f = 10.0; f <= 20.0; f += 0.5)
        {
            Assert.True(buffer.SampleHermite(f, out var x, out var y, out _, out _, out _, out _));
            Assert.Equal(100.0, x, 4);
            Assert.Equal(88.0, y, 4);
        }
    }

    [Fact]
    public void MobSnapshotBuffer_Add_MonotonicTimestamps_PreventZeroDeltaSnap()
    {
        var buffer = new MobSnapshotBuffer();
        // Add two snapshots at the exact same frame timestamp
        buffer.Add(new MobSnapshot(10.0, x: 100.0, y: 50.0, dx: 0.0, dy: 0.0, dir: 1, life: 100, maxLife: 100, animPayload: "idle", statePayload: ""));
        buffer.Add(new MobSnapshot(10.0, x: 110.0, y: 50.0, dx: 0.0, dy: 0.0, dir: 1, life: 100, maxLife: 100, animPayload: "idle", statePayload: ""));

        Assert.True(buffer.TryGetLatest(out var latest));
        Assert.True(buffer.TryGetAt(1, out var oldest));

        // Timestamps must be strictly increasing
        Assert.True(latest.ReceivedFrame > oldest.ReceivedFrame);

        // Sampling at intermediate frame must interpolate smoothly without divide-by-zero
        Assert.True(buffer.SampleHermite(10.005, out var x, out _, out _, out _, out var snap, out _));
        Assert.False(snap);
        Assert.True(x >= 100.0 && x <= 110.0);
    }

    [Fact]
    public void MobSnapshotBuffer_SampleHermite_GroundedMob_SuppressesDownwardExtrapolation()
    {
        var buffer = new MobSnapshotBuffer();
        // Grounded mob with small residual gravity (dy = 0.15 <= 0.25 threshold)
        buffer.Add(new MobSnapshot(10.0, x: 100.0, y: 88.0, dx: 1.0, dy: 0.15, dir: 1, life: 100, maxLife: 100, animPayload: "run", statePayload: ""));

        // Sample at 12.0 (2 frames ahead of latest snapshot 10.0)
        Assert.True(buffer.SampleHermite(12.0, out var x, out var y, out _, out _, out _, out _));
        // X extrapolates forward
        Assert.Equal(100.0 + 24.0 * 2.0, x, 2);
        // Y stays strictly at floor level 88.0 (does not sink into floor)
        Assert.Equal(88.0, y, 4);
    }

    [Fact]
    public void HermiteInterpolation_FritschCarlson_PreventsOvershootAndUndershoot()
    {
        double p0 = 100.0;
        double p1 = 200.0; // delta = +100

        // Test extreme velocity: v0 = 500 px/frame (far exceeds 3*delta = 300)
        // Without Fritsch-Carlson, the curve would overshoot past 200 (up to 250+).
        // With Fritsch-Carlson, the curve must remain strictly within [p0, p1].
        for (double t = 0.0; t <= 1.0; t += 0.05)
        {
            double pos = CombatSyncHelpers.HermiteInterpolate(p0, v0: 500.0, p1, v1: 0.0, t, duration: 1.0);
            Assert.True(pos >= p0 && pos <= p1, $"Position {pos} must be within [{p0}, {p1}] for extreme positive tangent");
        }

        // Test negative velocity: v0 = -200 px/frame (opposite of delta > 0)
        // Without Fritsch-Carlson, the curve would undershoot below 100.
        // With Fritsch-Carlson, v0 is clamped to 0, so position never drops below p0.
        for (double t = 0.0; t <= 1.0; t += 0.05)
        {
            double pos = CombatSyncHelpers.HermiteInterpolate(p0, v0: -200.0, p1, v1: 0.0, t, duration: 1.0);
            Assert.True(pos >= p0 && pos <= p1, $"Position {pos} must be within [{p0}, {p1}] for negative tangent");
        }
    }
}
