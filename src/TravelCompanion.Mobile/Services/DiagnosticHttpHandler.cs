using System.Diagnostics;

namespace TravelCompanion.Mobile.Services;

public sealed class DiagnosticHttpHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
#if ANDROID
        MobileOperationMeasurement.Request();
#endif
        var watch = Stopwatch.StartNew();
        // Only a fixed API area is recorded. No path identifiers, query strings or headers.
        var area = request.RequestUri?.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1).FirstOrDefault();
        area = area is "auth" or "mobile" or "trips" or "reservations" or "assistant" or "passes"
            ? area : "other";
        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            ClientDiagnostics.Record("http_completed", new() { Area = area, Status = (int)response.StatusCode, ElapsedMs = watch.ElapsedMilliseconds });
            return response;
        }
        catch (Exception exception)
        {
            ClientDiagnostics.Record("http_failed", new() { Area = area, ElapsedMs = watch.ElapsedMilliseconds,
                Canceled = cancellationToken.IsCancellationRequested }, exception);
            throw;
        }
    }
}
