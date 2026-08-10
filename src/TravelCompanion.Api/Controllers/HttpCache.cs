using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace TravelCompanion.Api.Controllers;

internal static class HttpCache
{
    private const string PublicCacheControlValue = "public, max-age=300, must-revalidate";
    private const string PrivateCacheControlValue = "private, max-age=300, must-revalidate";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static IActionResult OkOrNotModified<T>(
        ControllerBase controller,
        T response,
        bool isPublic = true)
    {
        var etag = CreateWeakETag(response);
        controller.Response.Headers["ETag"] = etag;
        controller.Response.Headers["Cache-Control"] = isPublic
            ? PublicCacheControlValue
            : PrivateCacheControlValue;

        return RequestMatches(controller.Request, etag)
            ? controller.StatusCode(StatusCodes.Status304NotModified)
            : controller.Ok(response);
    }

    private static string CreateWeakETag<T>(T response)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(response, JsonOptions);
        var hash = SHA256.HashData(bytes);
        return $"W/\"{Convert.ToHexString(hash).ToLowerInvariant()}\"";
    }

    private static bool RequestMatches(HttpRequest request, string etag)
    {
        var ifNoneMatch = request.Headers.IfNoneMatch.ToString();
        if (string.IsNullOrWhiteSpace(ifNoneMatch))
        {
            return false;
        }

        return ifNoneMatch
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(candidate => candidate == "*" || string.Equals(candidate, etag, StringComparison.Ordinal));
    }
}
