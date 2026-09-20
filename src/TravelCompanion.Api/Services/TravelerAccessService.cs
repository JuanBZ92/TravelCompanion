using TravelCompanion.Api.Models;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed class TravelerAccessService(
    UserSessionService sessionService,
    FreeTrialAccessService? freeTrialAccessService = null)
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
            SessionAccessMode.Builder or SessionAccessMode.BuilderReadOnly => ExperienceMode.SelfServiceBuilder,
            _ => ExperienceMode.CuratedPremium
        };
        if (session.AccessMode == SessionAccessMode.FreeMapPreview)
        {
            if (freeTrialAccessService is null)
            {
                return new TravelerAccessContext(session, mode, CreateCapabilities(mode, false));
            }
            var trial = await freeTrialAccessService.GetStatusAsync(session.User.Id, cancellationToken);
            return new TravelerAccessContext(
                session,
                mode,
                new TravelerCapabilitiesDto(
                    CanViewFullMap: false,
                    CanSearchGooglePlaces: false,
                    CanEditItinerary: trial.CanEdit,
                    HasCuratedDocs: false,
                    RequiresTripSetup: !session.TripId.HasValue,
                    CanCalculateRoutes: false));
        }

        if (session.AccessMode == SessionAccessMode.BuilderReadOnly)
        {
            return new TravelerAccessContext(session, mode, new TravelerCapabilitiesDto(
                CanViewFullMap: false,
                CanSearchGooglePlaces: false,
                CanEditItinerary: false,
                HasCuratedDocs: false,
                RequiresTripSetup: false,
                CanCalculateRoutes: false));
        }

        return new TravelerAccessContext(session, mode, CreateCapabilities(mode, mode == ExperienceMode.SelfServiceBuilder && !session.TripId.HasValue));
    }

    public static TravelerCapabilitiesDto CreateCapabilities(ExperienceMode mode, bool requiresTripSetup) => mode switch
    {
        ExperienceMode.FreePreview => new(false, false, false, false, false, false),
        ExperienceMode.SelfServiceBuilder => new(true, true, true, false, requiresTripSetup, true),
        _ => new(true, true, false, true, false, true)
    };
}

public sealed record TravelerAccessContext(UserSessionContext Session, ExperienceMode ExperienceMode, TravelerCapabilitiesDto Capabilities)
{
    public AppUser User => Session.User;
    public Guid? TripId => Session.TripId;
}
