using System.Reflection;
using System.Text.Json;
using TravelCompanion.Api.Controllers;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class EntitlementMappingCharacterizationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Consumers_preserve_every_field_expiry_filter_and_their_existing_order(bool empty)
    {
        var granted = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.FromHours(9));
        var destination = Guid.NewGuid();
        var package = Guid.NewGuid();
        var user = new AppUser { Id = Guid.NewGuid(), Email = "test@example.test", DisplayName = "Viajero 日本" };
        var subscription = new UserEntitlement
        {
            Id = Guid.NewGuid(), AccessLevel = ContentAccessLevel.Subscription, DestinationId = destination,
            TravelPackageId = package, GrantedAt = granted, ExpiresAt = granted.AddYears(100), Source = "Store"
        };
        var paidLater = new UserEntitlement
        {
            Id = Guid.NewGuid(), AccessLevel = ContentAccessLevel.Paid, DestinationId = destination,
            GrantedAt = granted.AddDays(1), Source = "PIN"
        };
        var paidEarlier = new UserEntitlement
        {
            Id = Guid.NewGuid(), AccessLevel = ContentAccessLevel.Paid, GrantedAt = granted, Source = "Support"
        };
        if (!empty)
        {
            user.Entitlements = [subscription, paidLater, new()
            {
                Id = Guid.NewGuid(), AccessLevel = ContentAccessLevel.AdminOnly,
                GrantedAt = granted.AddYears(-1), ExpiresAt = granted, Source = "Expired"
            }, paidEarlier];
        }

        foreach (var (type, name, sorted) in Consumers)
        {
            var expectedItems = empty ? Array.Empty<UserEntitlement>()
                : sorted ? [paidEarlier, paidLater, subscription] : new[] { subscription, paidLater, paidEarlier };
            var expected = new UserEntitlementsDto(user.Id, user.Email, user.DisplayName,
                empty ? [] : sorted ? [ContentAccessLevel.Paid, ContentAccessLevel.Subscription] : [ContentAccessLevel.Subscription, ContentAccessLevel.Paid],
                empty ? [] : [destination], empty ? [] : [package],
                expectedItems.Select(item => new UserEntitlementDto(item.Id, item.AccessLevel, item.DestinationId,
                    item.TravelPackageId, item.GrantedAt, item.ExpiresAt, item.Source)).ToList());
            var actual = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static, [typeof(AppUser)])!
                .Invoke(null, [user]);
            Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
            Assert.Equal(expectedItems.Select(item => item.Id), ((UserEntitlementsDto)actual!).Entitlements.Select(item => item.Id));
        }
    }

    private static readonly (Type Type, string Name, bool Sorted)[] Consumers =
    [
        (typeof(TravelRecommendationPlanningService), "ToEntitlementsDto", false),
        (typeof(TodayRecommendationService), "ToEntitlementsDto", false),
        (typeof(UsersController), "ToDto", true),
        (typeof(MobileController), "ToEntitlementsDto", true)
    ];
}
