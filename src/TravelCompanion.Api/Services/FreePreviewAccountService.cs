using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using Microsoft.Extensions.Options;
using TravelCompanion.Api.Options;
using TravelCompanion.Shared;

namespace TravelCompanion.Api.Services;

public sealed class FreePreviewAccountService(TravelCompanionDbContext dbContext, IOptions<FreePreviewOptions>? options = null)
{
    public const string AccountEmail = "free-preview@travelcompanion.system";
    public const string AccountEmailPrefix = "free-preview+";

    public async Task<AppUser> GetOrCreateAsync(string? clientInstanceId, CancellationToken cancellationToken = default, bool supportsPersistentFree = false)
    {
        var instanceKey = string.IsNullOrWhiteSpace(clientInstanceId)
            ? "legacy"
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clientInstanceId.Trim()))).ToLowerInvariant()[..32];
        var email = instanceKey == "legacy" ? AccountEmail : $"{AccountEmailPrefix}{instanceKey}@travelcompanion.system";
        var existing = await dbContext.AppUsers
            .FirstOrDefaultAsync(user => user.Email == email, cancellationToken);
        if (existing is not null)
        {
            await EnsureTrialGrantAsync(existing, cancellationToken);
            return existing;
        }

        var account = new AppUser
        {
            Id = instanceKey == "legacy" ? Guid.Parse("00000000-0000-0000-0000-000000000001") : Guid.NewGuid(),
            Email = email,
            DisplayName = "YUKU Preview",
            PasswordHash = string.Empty,
            MustChangePassword = false
        };
        dbContext.AppUsers.Add(account);

        try
        {
            // Save the new account and its assigned policy together so a concurrent
            // login cannot create a legacy grant in the gap between two commits.
            await EnsureTrialGrantAsync(account, cancellationToken, supportsPersistentFree && instanceKey != "legacy");
            return account;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(account).State = EntityState.Detached;
            var racedAccount = await dbContext.AppUsers.SingleAsync(user => user.Email == email, cancellationToken);
            await EnsureTrialGrantAsync(racedAccount, cancellationToken);
            return racedAccount;
        }
    }

    private async Task EnsureTrialGrantAsync(AppUser account, CancellationToken cancellationToken, bool supportsPersistentFree = false)
    {
        if (await dbContext.BuilderAccessGrants.AnyAsync(grant => grant.AppUserId == account.Id && grant.IsTrial, cancellationToken))
        {
            return;
        }

        var destinationId = await dbContext.FreeMapCities.AsNoTracking()
            .Where(city => city.IsEnabled)
            .OrderBy(city => city.SortOrder)
            .Select(city => city.DestinationId)
            .FirstOrDefaultAsync(cancellationToken);
        if (destinationId == Guid.Empty)
        {
            destinationId = await dbContext.Destinations.AsNoTracking()
                .OrderBy(destination => destination.Name)
                .Select(destination => destination.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }
        if (destinationId == Guid.Empty)
        {
            throw new InvalidOperationException("A destination is required before free preview access can be created.");
        }

        dbContext.BuilderAccessGrants.Add(new BuilderAccessGrant
        {
            Id = Guid.NewGuid(),
            AppUserId = account.Id,
            DestinationId = destinationId,
            PinHash = string.Empty,
            IsTrial = true,
            FreePolicy = AssignPolicy(account.Id, supportsPersistentFree, options?.Value.PersistentFreePercent ?? 0),
            Status = TravelCompanion.Shared.BuilderAccessStatus.Active,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            if (!await dbContext.BuilderAccessGrants.AnyAsync(
                    grant => grant.AppUserId == account.Id && grant.IsTrial,
                    cancellationToken))
            {
                throw;
            }
        }
    }

    internal static FreeAccessPolicy AssignPolicy(Guid userId, bool supported, int percent)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"free-policy-v1:{userId:N}"));
        var bucket = ((hash[0] << 8) | hash[1]) % 100;
        return supported && bucket < Math.Clamp(percent, 0, 100)
            ? FreeAccessPolicy.PersistentFree : FreeAccessPolicy.TimedTrial;
    }
}
