using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/mobile/places")]
public sealed class PlacesController(
    TravelCompanionDbContext dbContext,
    TravelerAccessService accessService,
    IGooglePlacesService googlePlacesService) : ControllerBase
{
    [HttpPost("search-page")]
    public async Task<ActionResult<PagedResultDto<RecommendationDto>>> SearchPage(PlaceSearchRequest request, CancellationToken cancellationToken, [FromQuery] int page = 1)
    {
        var result = await Search(request, cancellationToken);
        if (result.Result is not OkObjectResult { Value: IReadOnlyList<RecommendationDto> places }) return result.Result!;
        const int pageSize = 10;
        var totalPages = Math.Max(1, (int)Math.Ceiling(places.Count / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        return Ok(new PagedResultDto<RecommendationDto>(places.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            page, pageSize, places.Count, totalPages, page > 1, page < totalPages));
    }

    [HttpPost("autocomplete")]
    public async Task<ActionResult<IReadOnlyList<PlaceSuggestionDto>>> Autocomplete(PlaceAutocompleteRequest request, CancellationToken cancellationToken)
    {
        var access = await accessService.GetAsync(HttpContext, cancellationToken);
        if (access?.Capabilities.CanSearchGooglePlaces != true) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Query) || request.Query.Trim().Length < 3) return Ok(Array.Empty<PlaceSuggestionDto>());
        if (request.Query.Length > 200 || request.City?.Length > 120 || !Guid.TryParse(request.SessionToken, out _)) return BadRequest();
        return Ok(await googlePlacesService.AutocompleteAsync(request, cancellationToken));
    }

    [HttpPost("details")]
    public async Task<ActionResult<RecommendationDto>> Details(PlaceDetailsRequest request, CancellationToken cancellationToken)
    {
        var access = await accessService.GetAsync(HttpContext, cancellationToken);
        if (access?.Capabilities.CanSearchGooglePlaces != true) return Forbid();
        if (string.IsNullOrWhiteSpace(request.PlaceId) || request.PlaceId.Length > 256 || !Guid.TryParse(request.SessionToken, out _)) return BadRequest();
        var now = DateTimeOffset.UtcNow;
        var destinationId = access.TripId.HasValue
            ? await dbContext.Trips
                .Where(trip => trip.Id == access.TripId && trip.AppUserId == access.User.Id)
                .Select(trip => trip.DestinationId)
                .SingleAsync(cancellationToken)
            : await dbContext.BuilderAccessGrants
                .Where(grant => grant.AppUserId == access.User.Id
                    && grant.Status == TravelCompanion.Shared.BuilderAccessStatus.Active
                    && grant.RevokedAtUtc == null
                    && (!grant.ExpiresAtUtc.HasValue || grant.ExpiresAtUtc > now))
                .OrderByDescending(grant => grant.CreatedAtUtc)
                .Select(grant => grant.DestinationId)
                .FirstAsync(cancellationToken);
        var place = await googlePlacesService.DetailsAsync(destinationId, request, cancellationToken);
        return place is null ? NotFound() : Ok(place);
    }

    [HttpPost("search")]
    public async Task<ActionResult<IReadOnlyList<RecommendationDto>>> Search(PlaceSearchRequest request, CancellationToken cancellationToken)
    {
        var access = await accessService.GetAsync(HttpContext, cancellationToken);
        if (access is null || (!access.Capabilities.CanViewFullMap
            && access.Session.AccessMode != TravelCompanion.Shared.SessionAccessMode.FreeMapPreview)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Query) || request.Query.Trim().Length < 2) return Ok(Array.Empty<RecommendationDto>());

        var now = DateTimeOffset.UtcNow;
        var destinationId = access.TripId.HasValue
            ? await dbContext.Trips
                .Where(trip => trip.Id == access.TripId && trip.AppUserId == access.User.Id)
                .Select(trip => trip.DestinationId)
                .SingleAsync(cancellationToken)
            : await dbContext.BuilderAccessGrants
                .Where(grant => grant.AppUserId == access.User.Id
                    && grant.Status == TravelCompanion.Shared.BuilderAccessStatus.Active
                    && grant.RevokedAtUtc == null
                    && (!grant.ExpiresAtUtc.HasValue || grant.ExpiresAtUtc > now))
                .OrderByDescending(grant => grant.CreatedAtUtc)
                .Select(grant => grant.DestinationId)
                .FirstAsync(cancellationToken);
        var activeEntitlements = access.User.Entitlements
            .Where(entitlement => entitlement.ExpiresAt is null || entitlement.ExpiresAt > now)
            .ToList();
        var entitlements = new UserEntitlementsDto(
            access.User.Id,
            access.User.Email,
            access.User.DisplayName,
            activeEntitlements.Select(entitlement => entitlement.AccessLevel).Distinct().ToList(),
            activeEntitlements.Where(entitlement => entitlement.DestinationId.HasValue)
                .Select(entitlement => entitlement.DestinationId!.Value).Distinct().ToList(),
            activeEntitlements.Where(entitlement => entitlement.TravelPackageId.HasValue)
                .Select(entitlement => entitlement.TravelPackageId!.Value).Distinct().ToList(),
            activeEntitlements.Select(entitlement => new UserEntitlementDto(
                entitlement.Id,
                entitlement.AccessLevel,
                entitlement.DestinationId,
                entitlement.TravelPackageId,
                entitlement.GrantedAt,
                entitlement.ExpiresAt,
                entitlement.Source)).ToList());
        var catalogCandidates = await dbContext.Recommendations
            .AsNoTracking()
            .UnlockedFor(destinationId, entitlements)
            .Select(item => new CatalogSearchCandidate(
                item.Id,
                item.ProviderPlaceId,
                item.Title,
                item.Neighborhood,
                item.Category,
                item.RefinedType,
                item.RefinedTypeEn,
                item.Tags,
                item.Description,
                item.DescriptionEn,
                item.ExtraDescription,
                item.ExtraDescriptionEn,
                item.Latitude,
                item.Longitude))
            .ToListAsync(cancellationToken);
        if (access.Session.AccessMode == TravelCompanion.Shared.SessionAccessMode.FreeMapPreview)
        {
            var freeCities = await dbContext.FreeMapCities.AsNoTracking()
                .Where(city => city.IsEnabled && city.DestinationId == destinationId)
                .ToListAsync(cancellationToken);
            catalogCandidates = catalogCandidates
                .Where(candidate => freeCities.Any(city =>
                    FreeMapPreviewService.CalculateDistanceKm(
                        city.CenterLatitude,
                        city.CenterLongitude,
                        candidate.Latitude,
                        candidate.Longitude) <= city.FreeRadiusKm))
                .ToList();
        }
        var score = CatalogSearch.CreateFieldScorer(request.Query);
        var rankedCatalog = catalogCandidates.Select(item => new
            {
                Item = item,
                Score = score(
                    item.Title,
                    $"{item.Neighborhood} {item.Category} {item.RefinedType} {item.RefinedTypeEn} {string.Join(' ', item.Tags)}",
                    $"{item.Description} {item.DescriptionEn} {item.ExtraDescription} {item.ExtraDescriptionEn}")
            })
            .Where(candidate => candidate.Score > 0).OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => request.Latitude.HasValue && request.Longitude.HasValue
                ? FreeMapPreviewService.CalculateDistanceKm(request.Latitude.Value, request.Longitude.Value, candidate.Item.Latitude, candidate.Item.Longitude) : 0)
            .ThenBy(candidate => candidate.Item.Title)
            .Select(candidate => candidate.Item)
            .ToList();
        var google = request.IncludeGoogle
            && rankedCatalog.Count == 0
            && access.Capabilities.CanSearchGooglePlaces
            ? await googlePlacesService.SearchAsync(destinationId, request, cancellationToken)
            : [];
        var googlePlaceIds = google
            .Where(item => !string.IsNullOrWhiteSpace(item.ProviderPlaceId))
            .Select(item => item.ProviderPlaceId!)
            .ToHashSet(StringComparer.Ordinal);
        var catalogIds = rankedCatalog.Select(item => item.Id)
            .Concat(catalogCandidates
                .Where(item => item.ProviderPlaceId is not null && googlePlaceIds.Contains(item.ProviderPlaceId))
                .Select(item => item.Id))
            .Distinct()
            .ToList();
        var catalogDetails = await dbContext.Recommendations
            .AsNoTracking()
            .Include(item => item.Packages)
            .Where(item => catalogIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var yuku = rankedCatalog
            .Where(item => catalogDetails.ContainsKey(item.Id))
            .Select(item => RecommendationPresentation.ToDto(catalogDetails[item.Id]))
            .ToList();
        var catalogByPlaceId = catalogCandidates
            .Where(item => !string.IsNullOrWhiteSpace(item.ProviderPlaceId) && catalogDetails.ContainsKey(item.Id))
            .GroupBy(item => item.ProviderPlaceId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => catalogDetails[group.First().Id], StringComparer.Ordinal);
        var results = yuku.Concat(google.Select(item => item.ProviderPlaceId is not null && catalogByPlaceId.TryGetValue(item.ProviderPlaceId, out var curated)
            ? RecommendationPresentation.ToDto(curated) : item));
        return Ok(results.DistinctBy(item => item.SelectionKey).ToList());
    }

    private sealed record CatalogSearchCandidate(
        Guid Id,
        string? ProviderPlaceId,
        string Title,
        string Neighborhood,
        string Category,
        string? RefinedType,
        string? RefinedTypeEn,
        List<string> Tags,
        string Description,
        string? DescriptionEn,
        string? ExtraDescription,
        string? ExtraDescriptionEn,
        decimal Latitude,
        decimal Longitude);
}
