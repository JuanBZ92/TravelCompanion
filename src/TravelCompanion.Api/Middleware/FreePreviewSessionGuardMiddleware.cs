using TravelCompanion.Api.Services;
using TravelCompanion.Shared;

namespace TravelCompanion.Api.Middleware;

public sealed class FreePreviewSessionGuardMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, UserSessionService sessionService)
    {
        if (context.Request.Path.StartsWithSegments("/api")
            && context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var session = await sessionService.GetSessionContextAsync(context, context.RequestAborted);
            if (session?.AccessMode == SessionAccessMode.FreeMapPreview
                && !IsAllowedFreePreviewPath(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "This session only provides access to the free map preview.")
                    .ExecuteAsync(context);
                return;
            }
        }

        await next(context);
    }

    private static bool IsAllowedFreePreviewPath(HttpRequest request)
    {
        if (HttpMethods.IsPost(request.Method)
            && string.Equals(request.Path.Value, "/api/auth/logout", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (request.Path.StartsWithSegments("/api/mobile/builder/setup")
            || request.Path.StartsWithSegments("/api/mobile/itinerary")
            || request.Path.StartsWithSegments("/api/mobile/places")
            || request.Path.StartsWithSegments("/api/mobile/pass")
            || request.Path.StartsWithSegments("/api/ai/travel-chat")
            || request.Path.StartsWithSegments("/api/ai/save-itinerary-item")
            || request.Path.StartsWithSegments("/api/ai/save_itinerary_item")
            || request.Path.StartsWithSegments("/api/ai/feedback")
            || request.Path.StartsWithSegments("/api/me/travel-preference-profile")
            || request.Path.StartsWithSegments("/api/mobile/proposals"))
        {
            return true;
        }

        if (HttpMethods.IsGet(request.Method)
            && (request.Path.StartsWithSegments("/api/mobile/sync-state")
                || request.Path.StartsWithSegments("/api/mobile/bootstrap")
                || request.Path.StartsWithSegments("/api/mobile/today")
                || request.Path.StartsWithSegments("/api/mobile/recommendations")))
        {
            return true;
        }

        return HttpMethods.IsGet(request.Method)
            && request.Path.StartsWithSegments("/api/mobile/free-map");
    }
}
