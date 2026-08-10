using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;

namespace TravelCompanion.Api.Services;

public sealed class FreePreviewAccountService(TravelCompanionDbContext dbContext)
{
    public const string AccountEmail = "free-preview@travelcompanion.system";

    public async Task<AppUser> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.AppUsers
            .FirstOrDefaultAsync(user => user.Email == AccountEmail, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var account = new AppUser
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Email = AccountEmail,
            DisplayName = "YUKU Preview",
            PasswordHash = string.Empty,
            MustChangePassword = false
        };
        dbContext.AppUsers.Add(account);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return account;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(account).State = EntityState.Detached;
            return await dbContext.AppUsers
                .SingleAsync(user => user.Email == AccountEmail, cancellationToken);
        }
    }
}
