using System;
using System.Collections.Generic;
using DeadCellsMultiplayerMod.Mobs.MobsSynchronization;
using Xunit;

namespace DeadCellsMultiplayerMod.ProtocolReplay.Tests;

public sealed class AnimPayloadKeyTests
{

    [Fact]
    public void AnimPayloadKey_NaN_HandledConsistently()
    {
        var keyNaN1 = new MobsSynchronization.AnimPayloadKey("atk", false, double.NaN);
        var keyNaN2 = new MobsSynchronization.AnimPayloadKey("atk", false, double.NaN);

        Assert.True(keyNaN1.Equals(keyNaN2));
        Assert.True(keyNaN1 == keyNaN2);
        Assert.Equal(keyNaN1.GetHashCode(), keyNaN2.GetHashCode());
    }

    [Fact]
    public void AnimPayloadKey_DictionaryLookup_AndZeroAllocationOnHit()
    {
        var cache = new Dictionary<MobsSynchronization.AnimPayloadKey, string>();
        var sampleKey = new MobsSynchronization.AnimPayloadKey("idle", false, 1.0);
        const string expectedPayload = "idle~0~1";
        cache[sampleKey] = expectedPayload;

        // Verify retrieval
        var lookupKey = new MobsSynchronization.AnimPayloadKey("idle", false, 1.0);
        Assert.True(cache.TryGetValue(lookupKey, out var result));
        Assert.Same(expectedPayload, result);

        // Warm up JIT
        for (int i = 0; i < 100; i++)
        {
            var k = new MobsSynchronization.AnimPayloadKey("idle", false, 1.0);
            cache.TryGetValue(k, out _);
        }

        // Measure allocations across 50,000 dictionary lookups
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50000; i++)
        {
            var k = new MobsSynchronization.AnimPayloadKey("idle", false, 1.0);
            bool found = cache.TryGetValue(k, out var hit);
            if (!found || !ReferenceEquals(hit, expectedPayload))
            {
                throw new InvalidOperationException("Lookup failed");
            }
        }
        long after = GC.GetAllocatedBytesForCurrentThread();

        long allocated = after - before;
        Assert.Equal(0L, allocated);
    }
}
