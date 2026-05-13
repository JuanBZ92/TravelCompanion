using TravelCompanion.Api.Models;

namespace TravelCompanion.Api.Services;

public interface IUserInvitationSender
{
    Task SendTemporaryPasswordAsync(AppUser user, string temporaryPassword, CancellationToken cancellationToken = default);
}

public sealed class LoggingUserInvitationSender(ILogger<LoggingUserInvitationSender> logger) : IUserInvitationSender
{
    public Task SendTemporaryPasswordAsync(AppUser user, string temporaryPassword, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Temporary password generated for {Email}. Delivery channel is not configured yet.",
            user.Email);

        return Task.CompletedTask;
    }
}
