using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.Tests;

public sealed class MapUpdateBatchTests
{
    [Fact]
    public void LargeCatalogRebuildsEachNativeCollectionOnlyOnce()
    {
        var calls = new List<string>();
        var batch = new MapUpdateBatch(calls.Add);
        using (batch.Begin())
        {
            for (var i = 0; i < 1000; i++)
            {
                Assert.True(batch.Defer("Pins"));
                Assert.True(batch.Defer("Elements"));
            }
            Assert.Empty(calls);
        }
        Assert.Equal(["Pins", "Elements"], calls);
        Assert.False(batch.Defer("Pins"));
    }

    [Fact]
    public void NestedUpdatesFlushAtOuterBoundaryAndDisposeIsIdempotent()
    {
        var calls = new List<string>();
        var batch = new MapUpdateBatch(calls.Add);
        var outer = batch.Begin();
        using (batch.Begin()) Assert.True(batch.Defer("Pins"));
        Assert.Empty(calls);
        outer.Dispose(); outer.Dispose();
        Assert.Single(calls);
        using (batch.Begin()) { }
        Assert.Single(calls);
    }

    [Fact]
    public void FailedNativeUpdateDoesNotLeaveFutureUpdatesDeferred()
    {
        var batch = new MapUpdateBatch(_ => throw new InvalidOperationException());
        Assert.Throws<InvalidOperationException>(() =>
        {
            using var scope = batch.Begin();
            batch.Defer("Pins");
        });
        Assert.False(batch.Defer("Pins"));
        using (batch.Begin()) { }
    }
}
