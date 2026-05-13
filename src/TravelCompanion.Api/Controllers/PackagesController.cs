using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class PackagesController(
    TravelCompanionDbContext dbContext,
    UserSessionService sessionService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResultDto<TravelPackageDto>>> GetPackages(
        [FromQuery] string? destinationSlug = null,
        [FromQuery] int page = PaginationRequest.DefaultPage,
        [FromQuery] int pageSize = PaginationRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (!PaginationRequest.TryCreate(page, pageSize, out var pagination, out var error))
        {
            return this.ValidationError("pagination", error!);
        }

        var query = dbContext.TravelPackages.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(destinationSlug))
        {
            query = query.Where(package => package.Destination != null
                && package.Destination.Slug == destinationSlug);
        }

        var user = await sessionService.GetUserAsync(HttpContext, cancellationToken);
        var packages = await query
            .OrderBy(package => package.Price)
            .ToPagedResultAsync(pagination, cancellationToken);

        var items = packages.Items
            .Select(package => ToDto(package, user))
            .ToList();

        var response = new PagedResultDto<TravelPackageDto>(
            items,
            packages.Page,
            packages.PageSize,
            packages.TotalItems,
            packages.TotalPages,
            packages.HasPreviousPage,
            packages.HasNextPage);

        return Ok(response);
    }

    private static TravelPackageDto ToDto(TravelPackage package, AppUser? user)
    {
        var requiredAccessLevel = package.IsSubscription
            ? ContentAccessLevel.Subscription
            : ContentAccessLevel.Bundle;

        var activeEntitlements = GetActiveEntitlements(user);
        var isUnlocked = ContentAccessPolicy.IsUnlocked(
            requiredAccessLevel,
            activeEntitlements.Select(entitlement => entitlement.AccessLevel),
            activeEntitlements.Any(entitlement => entitlement.DestinationId == package.DestinationId),
            activeEntitlements.Any(entitlement => entitlement.TravelPackageId == package.Id));

        return new TravelPackageDto(
            package.Id,
            package.DestinationId,
            package.Name,
            package.Slug,
            package.Description,
            package.Price,
            package.Currency,
            package.IsSubscription,
            requiredAccessLevel,
            isUnlocked);
    }

    private static List<UserEntitlement> GetActiveEntitlements(AppUser? user)
    {
        var now = DateTimeOffset.UtcNow;
        return user?.Entitlements
            .Where(entitlement => entitlement.ExpiresAt is null || entitlement.ExpiresAt > now)
            .ToList() ?? [];
    }
}
