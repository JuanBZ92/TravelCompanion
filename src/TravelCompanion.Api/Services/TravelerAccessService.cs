using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed class TravelerAccessService(UserSessionService sessionService)
{
    public async Task<TravelerAccessContext?> GetAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        var session = await sessionService.GetSessionContextAsync(httpContext, cancellationToken);
        if (session is null)
        {
            return null;
        }

        var mode = session.AccessMode switch
        {
            SessionAccessMode.FreeMapPreview => ExperienceMode.FreePreview,
            SessionAccessMode.Builder => ExperienceMode.SelfServiceBuilder,
            _ => ExperienceMode.CuratedPremium
        };
        return new TravelerAccessContext(session, mode, CreateCapabilities(mode, mode == ExperienceMode.SelfServiceBuilder && !session.TripId.HasValue));
    }

    public static TravelerCapabilitiesDto CreateCapabilities(ExperienceMode mode, bool requiresTripSetup) => mode switch
    {
        ExperienceMode.FreePreview => new(false, false, false, false, false),
        ExperienceMode.SelfServiceBuilder => new(true, true, true, false, requiresTripSetup),
        _ => new(true, true, false, true, false)
    };
}

public sealed record TravelerAccessContext(UserSessionContext Session, ExperienceMode ExperienceMode, TravelerCapabilitiesDto Capabilities)
{
    public AppUser User => Session.User;
    public Guid? TripId => Session.TripId;
}
