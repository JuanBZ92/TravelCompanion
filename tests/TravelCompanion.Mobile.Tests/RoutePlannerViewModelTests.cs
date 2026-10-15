using TravelCompanion.Mobile.Services;
using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Tests;

[Collection("Schedule localization")]
public sealed class RoutePlannerViewModelTests : IDisposable
{
    private readonly string culture = LocalizationResourceManager.Instance.CurrentCulture.Name;
    private static readonly ScheduleItemDto Destination = new(Guid.NewGuid(), null, ReservationType.Event,
        new(2026, 11, 20), new(12, 0), null, null, "Museum", "Tokyo", "Museum", "Museum address", "", "",
        null, null, null, null, null, null, ProviderPlaceId: "museum-place");
    private static readonly TodayHotelBaseDto Hotel = new("Hotel Tokyo", "Hotel address", "hotel-place", 35m, 139m);

    [Fact]
    public async Task Selection_is_sent_with_mode_margin_and_previous_id_and_clear_estimation()
    {
        LocalizationResourceManager.Instance.SetCulture("es");
        ItineraryRouteRequest? sent = null;
        var previous = Destination with { Id = Guid.NewGuid(), StartsAt = new(9, 0), Title = "Garden" };
        using var vm = Create(async (request, _) => { sent = request; await Task.Yield(); return new(request.Mode, "Available", "Garden", 25, DepartureLabel: "11:20", OriginKind: request.OriginKind) { ArrivalMarginMinutes = request.ArrivalMarginMinutes }; }, items: [previous]);
        vm.SelectedOrigin = vm.Origins.Single(option => option.Kind == "PreviousActivity"); vm.Mode = "TRANSIT"; vm.Margin = 15;
        await vm.EstimateAsync();
        Assert.Equal("TRANSIT", sent!.Mode); Assert.Equal(15, sent.ArrivalMarginMinutes); Assert.Equal(previous.Id, sent.PreviousActivityId);
        Assert.Equal("PreviousActivity", sent.OriginKind); Assert.True(vm.HasResult); Assert.Contains("25", vm.Duration); Assert.Contains("11:20", vm.Departure);
        Assert.Contains("Asia/Tokyo", vm.Timing); Assert.Contains("Garden", vm.OriginSummary); Assert.Empty(vm.Status);
        Assert.Contains("no garantizan", LocalizationResourceManager.Instance["RoutePlannerDisclaimer"]);
    }

    [Fact]
    public async Task Failed_estimate_keeps_reservation_and_directions_with_selected_origin_and_mode()
    {
        using var vm = Create((_, _) => throw new HttpRequestException("synthetic"));
        vm.Mode = "DRIVE";
        await vm.EstimateAsync();
        Assert.False(vm.HasResult); Assert.Equal(Destination, vm.Destination);
        var url = vm.MapsUri.AbsoluteUri;
        Assert.Contains("origin=35%2C139", url); Assert.Contains("origin_place_id=hotel-place", url);
        Assert.Contains("destination_place_id=museum-place", url); Assert.Contains("travelmode=driving", url);
        Assert.NotEmpty(vm.Status); Assert.True(vm.CanEstimate);
    }

    [Fact]
    public async Task Malformed_response_leaves_a_retryable_message_and_maps_instead_of_loading_text()
    {
        LocalizationResourceManager.Instance.SetCulture("es");
        using var vm = Create((_, _) => throw new System.Text.Json.JsonException("synthetic malformed response"));
        await vm.EstimateAsync();
        Assert.False(vm.Busy); Assert.False(vm.HasResult); Assert.True(vm.CanEstimate);
        Assert.Equal(LocalizationResourceManager.Instance["RoutePlannerUnavailable"], vm.Status);
        Assert.DoesNotContain("Calculando", vm.Status); Assert.Equal(Destination, vm.Destination);
        Assert.Contains("destination_place_id=museum-place", vm.MapsUri.AbsoluteUri);
    }

    [Fact]
    public async Task Location_denial_does_not_call_provider_and_maps_can_ask_for_departure()
    {
        var calls = 0;
        using var vm = Create((_, _) => { calls++; return Task.FromResult<ItineraryRouteDto?>(null); }, locate: _ => Task.FromResult<GeoPointDto?>(null));
        vm.SelectedOrigin = vm.Origins[0];
        await vm.EstimateAsync();
        Assert.Equal(0, calls); Assert.False(vm.Busy); Assert.DoesNotContain("&origin=", vm.MapsUri.AbsoluteUri);
        Assert.Contains("destination=", vm.MapsUri.AbsoluteUri);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Context_or_access_change_discards_even_uncancelable_late_result(bool changeContext)
    {
        var current = true; var allowed = true;
        var pending = new TaskCompletionSource<ItineraryRouteDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var vm = Create((_, _) => pending.Task, current: () => current, allowed: () => allowed);
        var operation = vm.EstimateAsync();
        if (changeContext) current = false; else allowed = false;
        vm.RefreshAccess(); pending.SetResult(new("WALK", "Available", "Hotel", 10, DepartureLabel: "11:40")); await operation;
        Assert.False(vm.HasResult); Assert.False(vm.CanEstimate); Assert.False(vm.Busy);
    }

    [Fact]
    public async Task Editing_options_cancels_and_drops_previous_estimate_and_blocks_duplicate_press()
    {
        var calls = 0; CancellationToken captured = default;
        var pending = new TaskCompletionSource<ItineraryRouteDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var vm = Create((_, ct) => { calls++; captured = ct; return pending.Task; });
        var operation = vm.EstimateAsync(); await vm.EstimateAsync(); Assert.Equal(1, calls);
        vm.Margin = 30; Assert.True(captured.IsCancellationRequested);
        pending.SetResult(new("WALK", "Available", "Hotel", 10, DepartureLabel: "11:40")); await operation;
        Assert.False(vm.HasResult); Assert.True(vm.CanEstimate); Assert.Empty(vm.Departure);
    }

    [Fact]
    public async Task Server_access_denial_cannot_be_overridden_by_cached_capability_or_changing_options()
    {
        var calls = 0;
        using var vm = Create((request, _) => { calls++; return Task.FromResult<ItineraryRouteDto?>(new(request.Mode, "AccessDenied", "")); });
        await vm.EstimateAsync();
        Assert.False(vm.CanEstimate); Assert.False(vm.HasResult); Assert.NotEmpty(vm.Status);
        vm.Mode = "DRIVE"; vm.Margin = 30; vm.RefreshAccess(); await vm.EstimateAsync();
        Assert.Equal(1, calls); Assert.False(vm.CanEstimate); Assert.Equal(Destination, vm.Destination);
        Assert.Contains("travelmode=driving", vm.MapsUri.AbsoluteUri);
    }

    [Theory]
    [InlineData("Hotel", 10)]
    [InlineData("PreviousActivity", 10)]
    public async Task Older_server_response_cannot_misrepresent_selected_origin_or_margin(string origin, int margin)
    {
        var previous = Destination with { Id = Guid.NewGuid(), StartsAt = new(9, 0), Title = "Garden" };
        using var vm = Create((_, _) => Task.FromResult<ItineraryRouteDto?>(new("WALK", "Available", "Old hotel", 20,
            DepartureLabel: "11:30", OriginKind: origin) { ArrivalMarginMinutes = margin }), items: [previous]);
        vm.SelectedOrigin = vm.Origins.Single(option => option.Kind == "PreviousActivity"); vm.Margin = 15;
        await vm.EstimateAsync();
        Assert.False(vm.HasResult); Assert.Empty(vm.Departure); Assert.NotEmpty(vm.Status);
        Assert.Contains("origin_place_id=museum-place", vm.MapsUri.AbsoluteUri);
    }

    [Fact]
    public async Task Location_acquisition_observes_cancellation_before_http()
    {
        var calls = 0; CancellationToken captured = default;
        var pending = new TaskCompletionSource<GeoPointDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var vm = Create((_, _) => { calls++; return Task.FromResult<ItineraryRouteDto?>(null); },
            locate: ct => { captured = ct; return pending.Task; });
        vm.SelectedOrigin = vm.Origins[0]; var operation = vm.EstimateAsync(); vm.Cancel();
        pending.SetResult(new(35m, 139m)); await operation;
        Assert.True(captured.IsCancellationRequested); Assert.Equal(0, calls); Assert.Empty(vm.Status);
    }

    [Fact]
    public void Directions_use_address_when_coordinates_missing_and_escape_untrusted_names()
    {
        var origin = new RouteOriginOption("Hotel", "Hotel", "Hotel & Spa, Tokyo", "verified place");
        var uri = RouteDirectionsUri.Create(Destination, origin, "TRANSIT");
        Assert.Contains("origin=Hotel%20%26%20Spa%2C%20Tokyo", uri.AbsoluteUri); Assert.Contains("travelmode=transit", uri.AbsoluteUri);
    }

    [Fact]
    public void Directions_keep_long_japanese_addresses_within_provider_url_limit()
    {
        var longName = string.Concat(Enumerable.Repeat("日本ホテル🏨", 200));
        var destination = Destination with { LocationName = longName, Address = longName, Title = longName };
        var uri = RouteDirectionsUri.Create(destination, new("Hotel", longName, longName), "WALK");
        Assert.True(uri.AbsoluteUri.Length <= 2048); Assert.Contains("destination_place_id=museum-place", uri.AbsoluteUri);
    }

    private static RoutePlannerViewModel Create(Func<ItineraryRouteRequest, CancellationToken, Task<ItineraryRouteDto?>> estimate,
        IReadOnlyList<ScheduleItemDto>? items = null, Func<bool>? current = null, Func<bool>? allowed = null,
        Func<CancellationToken, Task<GeoPointDto?>>? locate = null) => new(Destination, Hotel, items ?? [], "Asia/Tokyo",
        current ?? (() => true), allowed ?? (() => true), locate ?? (_ => Task.FromResult<GeoPointDto?>(new(35m, 139m))), estimate);

    public void Dispose() => LocalizationResourceManager.Instance.SetCulture(culture);
}
