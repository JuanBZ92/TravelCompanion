using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.Tests;

public sealed class CacheFreshnessTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IsFresh_AcceptsSnapshotInsideLifetime()
    {
        Assert.True(CacheFreshness.IsFresh(Now.AddMinutes(-1), TimeSpan.FromMinutes(2), Now));
    }

    [Fact]
    public void IsFresh_RejectsExpiredOrFutureSnapshot()
    {
        Assert.False(CacheFreshness.IsFresh(Now.AddMinutes(-3), TimeSpan.FromMinutes(2), Now));
        Assert.False(CacheFreshness.IsFresh(Now.AddMinutes(1), TimeSpan.FromMinutes(2), Now));
    }

    [Fact]
    public void IsFresh_RejectsMissingTimestamp()
    {
        Assert.False(CacheFreshness.IsFresh(null, TimeSpan.FromMinutes(2), Now));
    }
}
