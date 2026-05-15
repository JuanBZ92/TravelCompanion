using TravelCompanion.Shared;

namespace TravelCompanion.Shared.Tests;

public sealed class ContentAccessPolicyTests
{
    [Fact]
    public void Free_content_is_visible_without_entitlements()
    {
        var isUnlocked = ContentAccessPolicy.IsUnlocked(ContentAccessLevel.Free, []);

        Assert.True(isUnlocked);
    }

    [Fact]
    public void Free_user_does_not_unlock_paid_or_subscription_content()
    {
        Assert.False(ContentAccessPolicy.IsUnlocked(ContentAccessLevel.Paid, []));
        Assert.False(ContentAccessPolicy.IsUnlocked(ContentAccessLevel.Subscription, []));
        Assert.False(ContentAccessPolicy.IsUnlocked(ContentAccessLevel.Bundle, []));
    }

    [Fact]
    public void Paid_user_unlocks_paid_content_only()
    {
        var activeLevels = new[] { ContentAccessLevel.Paid };

        Assert.True(ContentAccessPolicy.IsUnlocked(ContentAccessLevel.Paid, activeLevels));
        Assert.False(ContentAccessPolicy.IsUnlocked(ContentAccessLevel.Subscription, activeLevels));
        Assert.False(ContentAccessPolicy.IsUnlocked(ContentAccessLevel.Bundle, activeLevels));
    }

    [Fact]
    public void Subscription_user_unlocks_subscription_content_only()
    {
        var activeLevels = new[] { ContentAccessLevel.Subscription };

        Assert.True(ContentAccessPolicy.IsUnlocked(ContentAccessLevel.Subscription, activeLevels));
        Assert.False(ContentAccessPolicy.IsUnlocked(ContentAccessLevel.Paid, activeLevels));
        Assert.False(ContentAccessPolicy.IsUnlocked(ContentAccessLevel.Bundle, activeLevels));
    }

    [Fact]
    public void Bundle_user_unlocks_paid_and_package_content()
    {
        var activeLevels = new[] { ContentAccessLevel.Bundle };

        Assert.True(ContentAccessPolicy.IsUnlocked(ContentAccessLevel.Paid, activeLevels));
        Assert.True(ContentAccessPolicy.IsUnlocked(ContentAccessLevel.Bundle, activeLevels));
        Assert.False(ContentAccessPolicy.IsUnlocked(ContentAccessLevel.Subscription, activeLevels));
    }

    [Theory]
    [InlineData(false, ContentAccessLevel.Bundle)]
    [InlineData(true, ContentAccessLevel.Subscription)]
    public void Package_type_maps_to_the_expected_grant_level(bool isSubscription, ContentAccessLevel expectedGrantLevel)
    {
        var grantLevel = ProductAccessModel.GetPackageGrantLevel(isSubscription);

        Assert.Equal(expectedGrantLevel, grantLevel);
    }
}
