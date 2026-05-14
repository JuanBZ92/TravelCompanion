#if ANDROID
using Android.OS;
#endif

namespace TravelCompanion.Mobile.Services;

internal static class ApiEndpointResolver
{
    private const string ApiBaseUrlEnvironmentVariable = "TRAVELCOMPANION_API_BASE_URL";

    public static Uri Resolve()
    {
        var configuredValue = System.Environment.GetEnvironmentVariable(ApiBaseUrlEnvironmentVariable);
        var rawBaseUrl = string.IsNullOrWhiteSpace(configuredValue)
            ? GetDefaultBaseUrl()
            : configuredValue.Trim();

        if (!Uri.TryCreate(rawBaseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException(
                $"Invalid API base URL '{rawBaseUrl}'. Set {ApiBaseUrlEnvironmentVariable} to a valid absolute URL.");
        }

        if (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp)
        {
            throw new InvalidOperationException("API base URL must use http or https.");
        }

        if (baseUri.Scheme == Uri.UriSchemeHttp && !IsInsecureHttpAllowed(baseUri))
        {
            throw new InvalidOperationException(
                $"HTTP is only allowed for local development hosts (localhost, 127.0.0.1, ::1, 10.0.2.2). Current host: {baseUri.Host}. Use HTTPS for remote environments.");
        }

        return baseUri;
    }

    private static string GetDefaultBaseUrl()
    {
#if ANDROID
        return IsAndroidEmulator()
            ? "http://10.0.2.2:5289"
            : "http://127.0.0.1:5289";
#else
        return "https://localhost:7090";
#endif
    }

    private static bool IsInsecureHttpAllowed(Uri baseUri)
    {
        return IsLocalDevHost(baseUri.Host);
    }

    private static bool IsLocalDevHost(string host)
    {
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || host.Equals("::1", StringComparison.OrdinalIgnoreCase)
            || host.Equals("10.0.2.2", StringComparison.OrdinalIgnoreCase);
    }

#if ANDROID
    private static bool IsAndroidEmulator()
    {
        return (Build.Fingerprint?.Contains("generic", StringComparison.OrdinalIgnoreCase) ?? false)
            || (Build.Model?.Contains("Emulator", StringComparison.OrdinalIgnoreCase) ?? false)
            || (Build.Manufacturer?.Contains("Genymotion", StringComparison.OrdinalIgnoreCase) ?? false)
            || (Build.Brand?.StartsWith("generic", StringComparison.OrdinalIgnoreCase) ?? false)
            || (Build.Device?.StartsWith("generic", StringComparison.OrdinalIgnoreCase) ?? false)
            || (Build.Product?.Contains("sdk", StringComparison.OrdinalIgnoreCase) ?? false);
    }
#endif
}
