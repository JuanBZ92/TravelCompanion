using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;

namespace TravelCompanion.Api.Services;

public sealed class FreePreviewAccountService(TravelCompanionDbContext dbContext)
{
    public const string AccountEmail = "free-preview@travelcompanion.system";
    public const string AccountEmailPrefix = "free-preview+";

    public async Task<AppUser> GetOrCreateAsync(string? clientInstanceId, CancellationToken cancellationToken = default)
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
            await dbContext.SaveChangesAsync(cancellationToken);
            await EnsureTrialGrantAsync(account, cancellationToken);
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

    private async Task EnsureTrialGrantAsync(AppUser account, CancellationToken cancellationToken)
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
}
