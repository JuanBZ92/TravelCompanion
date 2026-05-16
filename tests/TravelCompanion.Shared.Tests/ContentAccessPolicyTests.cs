using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Shared.Tests;

public sealed class ContentAccessPolicyTests
{
    private static readonly Guid JapanDestinationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FranceDestinationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid EssentialsPackageId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid PremiumPackageId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid FrancePackageId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public void Free_recommendation_without_packages_is_visible_without_entitlements()
    {
        var isUnlocked = ContentAccessPolicy.IsRecommendationUnlocked(
            entitlements: null,
            ContentAccessLevel.Free,
            JapanDestinationId,
            []);

        Assert.True(isUnlocked);
    }

    [Fact]
    public void Packaged_recommendation_is_locked_without_package_or_destination_subscription()
    {
        var isUnlocked = ContentAccessPolicy.IsRecommendationUnlocked(
            entitlements: null,
            ContentAccessLevel.Free,
            JapanDestinationId,
            [EssentialsPackageId]);

        Assert.False(isUnlocked);
    }

    [Fact]
    public void Paid_package_grant_unlocks_only_that_package()
    {
        var entitlements = CreateEntitlements(
            accessLevels: [ContentAccessLevel.Paid],
            packageIds: [EssentialsPackageId]);

        Assert.True(ContentAccessPolicy.IsPackageUnlocked(entitlements, JapanDestinationId, EssentialsPackageId));
        Assert.False(ContentAccessPolicy.IsPackageUnlocked(entitlements, JapanDestinationId, PremiumPackageId));
    }

    [Fact]
    public void Destination_subscription_unlocks_every_package_in_that_destination()
    {
        var entitlements = CreateEntitlements(
            accessLevels: [ContentAccessLevel.Subscription],
            destinationIds: [JapanDestinationId],
            entitlements:
            [
                new UserEntitlementDto(
                    Guid.NewGuid(),
                    ContentAccessLevel.Subscription,
                    JapanDestinationId,
                    null,
                    DateTimeOffset.UtcNow,
                    null,
                    Source: "test")
            ]);

        Assert.True(ContentAccessPolicy.IsPackageUnlocked(entitlements, JapanDestinationId, EssentialsPackageId));
        Assert.True(ContentAccessPolicy.IsPackageUnlocked(entitlements, JapanDestinationId, PremiumPackageId));
    }

    [Fact]
    public void Destination_subscription_unlocks_subscription_recommendation_without_packages()
    {
        var entitlements = CreateEntitlements(
            accessLevels: [ContentAccessLevel.Subscription],
            destinationIds: [JapanDestinationId],
            entitlements:
            [
                new UserEntitlementDto(
                    Guid.NewGuid(),
                    ContentAccessLevel.Subscription,
                    JapanDestinationId,
                    null,
                    DateTimeOffset.UtcNow,
                    null,
                    Source: "test")
            ]);

        var isUnlocked = ContentAccessPolicy.IsRecommendationUnlocked(
            entitlements,
            ContentAccessLevel.Subscription,
            JapanDestinationId,
            []);

        Assert.True(isUnlocked);
    }

    [Fact]
    public void Subscription_recommendation_without_packages_is_locked_without_destination_subscription()
    {
        var isUnlocked = ContentAccessPolicy.IsRecommendationUnlocked(
            entitlements: null,
            ContentAccessLevel.Subscription,
            JapanDestinationId,
            []);

        Assert.False(isUnlocked);
    }

    [Fact]
    public void Destination_subscription_does_not_unlock_other_destinations()
    {
        var entitlements = CreateEntitlements(
            accessLevels: [ContentAccessLevel.Subscription],
            destinationIds: [JapanDestinationId],
            entitlements:
            [
                new UserEntitlementDto(
                    Guid.NewGuid(),
                    ContentAccessLevel.Subscription,
                    JapanDestinationId,
                    null,
                    DateTimeOffset.UtcNow,
                    null,
                    Source: "test")
            ]);

        Assert.False(ContentAccessPolicy.IsPackageUnlocked(entitlements, FranceDestinationId, FrancePackageId));
    }

    [Fact]
    public void Package_type_always_maps_to_package_grant()
    {
        Assert.Equal(ContentAccessLevel.Paid, ProductAccessModel.GetPackageGrantLevel(isSubscription: false));
        Assert.Equal(ContentAccessLevel.Paid, ProductAccessModel.GetPackageGrantLevel(isSubscription: true));
    }

    [Fact]
    public void Missing_deserialized_collections_are_treated_as_empty()
    {
        var isUnlocked = ContentAccessPolicy.IsRecommendationUnlocked(
            entitlements: null,
            ContentAccessLevel.Free,
            JapanDestinationId,
            packageIds: null);

        Assert.True(isUnlocked);
        Assert.False(ContentAccessPolicy.HasPackageAccess(entitlements: null, EssentialsPackageId));
        Assert.Contains(ContentAccessLevel.Free, ContentAccessPolicy.GetUnlockedContentLevels(activeAccessLevels: null));
    }

    private static UserEntitlementsDto CreateEntitlements(
        IReadOnlyList<ContentAccessLevel>? accessLevels = null,
        IReadOnlyList<Guid>? destinationIds = null,
        IReadOnlyList<Guid>? packageIds = null,
        IReadOnlyList<UserEntitlementDto>? entitlements = null)
    {
        return new UserEntitlementsDto(
            Guid.NewGuid(),
            "test@travelcompanion.local",
            "Test User",
            accessLevels ?? [],
            destinationIds ?? [],
            packageIds ?? [],
            entitlements ?? []);
    }
}
