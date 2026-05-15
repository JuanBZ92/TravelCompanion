using System.ComponentModel.DataAnnotations;
using System.Reflection;
using TravelCompanion.Api.Pages.Admin;
using TravelCompanion.Shared;

namespace TravelCompanion.Api.Tests;

public sealed class AdminInputValidationTests
{
    public static TheoryData<Type, string> OptionalReferenceInputs => new()
    {
        { typeof(DestinationsModel.DestinationInput), nameof(DestinationsModel.DestinationInput.Country) },
        { typeof(DestinationsModel.DestinationInput), nameof(DestinationsModel.DestinationInput.HeroImageUrl) },
        { typeof(DestinationsModel.DestinationInput), nameof(DestinationsModel.DestinationInput.ShortDescription) },
        { typeof(PackagesModel.PackageInput), nameof(PackagesModel.PackageInput.Description) },
        { typeof(RecommendationsModel.RecommendationInput), nameof(RecommendationsModel.RecommendationInput.Neighborhood) },
        { typeof(ReservationsModel.ReservationInput), nameof(ReservationsModel.ReservationInput.LocationName) },
        { typeof(ReservationsModel.ReservationInput), nameof(ReservationsModel.ReservationInput.Address) },
        { typeof(ReservationsModel.ReservationInput), nameof(ReservationsModel.ReservationInput.ConfirmationCode) },
        { typeof(ReservationsModel.ReservationInput), nameof(ReservationsModel.ReservationInput.Notes) },
        { typeof(UsersModel.EntitlementForm), nameof(UsersModel.EntitlementForm.Source) }
    };

    [Theory]
    [MemberData(nameof(OptionalReferenceInputs))]
    public void Optional_admin_reference_inputs_are_nullable(Type inputType, string propertyName)
    {
        var property = inputType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"{inputType.Name}.{propertyName} was not found.");
        var nullability = new NullabilityInfoContext().Create(property);

        Assert.Equal(NullabilityState.Nullable, nullability.WriteState);
    }

    [Fact]
    public void Reservation_event_allows_empty_optional_fields()
    {
        var input = new ReservationsModel.ReservationInput
        {
            TripId = Guid.NewGuid(),
            Type = ReservationType.Event,
            Date = new DateOnly(2026, 5, 20),
            StartsAt = new TimeOnly(9, 0),
            Title = "Desayuno",
            City = "Tokyo",
            LocationName = "Cafe",
            Address = null,
            ConfirmationCode = null,
            Notes = null
        };

        var errors = Validate(input);

        Assert.Empty(errors);
    }

    [Fact]
    public void Package_allows_empty_optional_description()
    {
        var input = new PackagesModel.PackageInput
        {
            DestinationId = Guid.NewGuid(),
            Name = "Japon Essentials",
            Slug = "japon-essentials",
            Description = null,
            Price = 99,
            Currency = "USD"
        };

        var errors = Validate(input);

        Assert.Empty(errors);
    }

    private static List<ValidationResult> Validate(object instance)
    {
        var validationContext = new ValidationContext(instance);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, validationContext, results, validateAllProperties: true);
        return results;
    }
}
