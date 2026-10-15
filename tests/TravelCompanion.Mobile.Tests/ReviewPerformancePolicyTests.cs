using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.Tests;

public sealed class ReviewPerformancePolicyTests
{
    [Fact]
    public void ReviewCommandRequiresExplicitFlagLoopbackUnlockedSessionAndReviewPackage()
    {
        var trip = Guid.NewGuid();
        bool Allowed(bool enabled = true, string package = "com.yuku.travelcompanion.plannerpaidreview",
            string endpoint = "http://127.0.0.1:5189", bool session = true, Guid? tripId = null, string? route = "//main/schedule") =>
            ReviewPerformancePolicy.CanRun(enabled, package, new(endpoint), session, tripId ?? trip, route);
        Assert.True(Allowed());
        Assert.False(Allowed(enabled: false));
        Assert.False(Allowed(package: "com.yuku.travelcompanion.app"));
        Assert.False(Allowed(package: "other.review"));
        Assert.False(Allowed(endpoint: "https://example.test"));
        Assert.False(Allowed(endpoint: "http://10.0.2.2:5189"));
        Assert.False(Allowed(endpoint: "ftp://127.0.0.1:5189"));
        Assert.False(Allowed(session: false));
        Assert.False(Allowed(route: "//biometric-unlock"));
        Assert.False(Allowed(route: "//login"));
        Assert.False(Allowed(route: null));
        Assert.False(ReviewPerformancePolicy.CanRun(true, "com.yuku.travelcompanion.plannerpaidreview",
            new("http://localhost:5189"), true, null, "//main/journal"));
    }

    [Theory]
    [InlineData("bootstrap", true)]
    [InlineData("trip", true)]
    [InlineData("journal", true)]
    [InlineData("planning", true)]
    [InlineData("other", false)]
    [InlineData("private-user-data", false)]
    [InlineData(null, false)]
    public void OnlyConstantScenarioLabelsAreAccepted(string? scenario, bool expected) =>
        Assert.Equal(expected, ReviewPerformancePolicy.IsScenario(scenario));

    [Fact]
    public void BenignStateNotificationKeepsBatchButChangedContextCancelsIt()
    {
        var user = Guid.NewGuid(); var trip = Guid.NewGuid();
        Assert.True(ReviewPerformancePolicy.IsCurrent(4, 4, user, user, trip, trip, true));
        Assert.False(ReviewPerformancePolicy.IsCurrent(4, 5, user, user, trip, trip, true));
        Assert.False(ReviewPerformancePolicy.IsCurrent(4, 4, user, Guid.NewGuid(), trip, trip, true));
        Assert.False(ReviewPerformancePolicy.IsCurrent(4, 4, user, user, trip, Guid.NewGuid(), true));
        Assert.False(ReviewPerformancePolicy.IsCurrent(4, 4, user, user, trip, trip, false));
        Assert.False(ReviewPerformancePolicy.IsCurrent(4, 4, user, null, trip, trip, true));
    }
}
