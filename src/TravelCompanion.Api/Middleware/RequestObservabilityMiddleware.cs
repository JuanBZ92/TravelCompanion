using System.Diagnostics;
using Microsoft.Extensions.Options;
using TravelCompanion.Api.Options;

namespace TravelCompanion.Api.Middleware;

public sealed class RequestObservabilityMiddleware(
    RequestDelegate next,
    IOptions<ObservabilityOptions> options,
    ILogger<RequestObservabilityMiddleware> logger)
{
    private readonly ObservabilityOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationHeaderName = string.IsNullOrWhiteSpace(_options.CorrelationHeaderName)
            ? "X-Correlation-ID"
            : _options.CorrelationHeaderName;

        var correlationId = context.Request.Headers.TryGetValue(correlationHeaderName, out var requestedCorrelationId)
            && !string.IsNullOrWhiteSpace(requestedCorrelationId.ToString())
            ? requestedCorrelationId.ToString()
            : context.TraceIdentifier;

        context.Response.Headers[correlationHeaderName] = correlationId;

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["TraceId"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier
        });

        var stopwatch = Stopwatch.StartNew();
        var method = context.Request.Method;
        var path = context.Request.Path.HasValue ? context.Request.Path.Value! : "/";

        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            logger.LogError(
                exception,
                "Unhandled exception on request {Method} {Path} after {ElapsedMs}ms.",
                method,
                path,
                stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }

        stopwatch.Stop();
        var statusCode = context.Response.StatusCode;
        var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                "Request finished with server error {StatusCode} for {Method} {Path} in {ElapsedMs}ms.",
                statusCode,
                method,
                path,
                elapsedMs);
            return;
        }

        if (statusCode >= StatusCodes.Status400BadRequest)
        {
            logger.LogWarning(
                "Request finished with client error {StatusCode} for {Method} {Path} in {ElapsedMs}ms.",
                statusCode,
                method,
                path,
                elapsedMs);
            return;
        }

        if (elapsedMs >= _options.SlowRequestThresholdMs)
        {
            logger.LogWarning(
                "Slow request detected: {Method} {Path} took {ElapsedMs}ms (threshold {ThresholdMs}ms).",
                method,
                path,
                elapsedMs,
                _options.SlowRequestThresholdMs);
        }
    }
}
