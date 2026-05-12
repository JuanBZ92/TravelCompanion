using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class UsersController(TravelCompanionDbContext dbContext) : ControllerBase
{
    private const string DemoUserEmail = "demo@travelcompanion.local";

    [HttpGet("demo/entitlements")]
    public Task<ActionResult<UserEntitlementsDto>> GetDemoEntitlements()
    {
        return GetEntitlementsByEmailAsync(DemoUserEmail);
    }

    [HttpGet("{userId:guid}/entitlements")]
    public async Task<ActionResult<UserEntitlementsDto>> GetEntitlements(Guid userId)
    {
        var user = await dbContext.AppUsers
            .AsNoTracking()
            .Include(user => user.Entitlements)
            .FirstOrDefaultAsync(user => user.Id == userId);

        return user is null
            ? NotFound()
            : Ok(ToDto(user));
    }

    private async Task<ActionResult<UserEntitlementsDto>> GetEntitlementsByEmailAsync(string email)
    {
        var user = await dbContext.AppUsers
            .AsNoTracking()
            .Include(user => user.Entitlements)
            .FirstOrDefaultAsync(user => user.Email == email);

        return user is null
            ? NotFound()
            : Ok(ToDto(user));
    }

    private static UserEntitlementsDto ToDto(AppUser user)
    {
        var now = DateTimeOffset.UtcNow;
        var activeEntitlements = user.Entitlements
            .Where(entitlement => entitlement.ExpiresAt is null || entitlement.ExpiresAt > now)
            .OrderBy(entitlement => entitlement.AccessLevel)
            .ThenBy(entitlement => entitlement.GrantedAt)
            .ToList();

        return new UserEntitlementsDto(
            user.Id,
            user.Email,
            user.DisplayName,
            activeEntitlements.Select(entitlement => entitlement.AccessLevel).Distinct().ToList(),
            activeEntitlements
                .Where(entitlement => entitlement.DestinationId.HasValue)
                .Select(entitlement => entitlement.DestinationId!.Value)
                .Distinct()
                .ToList(),
            activeEntitlements
                .Where(entitlement => entitlement.TravelPackageId.HasValue)
                .Select(entitlement => entitlement.TravelPackageId!.Value)
                .Distinct()
                .ToList(),
            activeEntitlements
                .Select(entitlement => new UserEntitlementDto(
                    entitlement.Id,
                    entitlement.AccessLevel,
                    entitlement.DestinationId,
                    entitlement.TravelPackageId,
                    entitlement.GrantedAt,
                    entitlement.ExpiresAt,
                    entitlement.Source))
                .ToList());
    }
}
