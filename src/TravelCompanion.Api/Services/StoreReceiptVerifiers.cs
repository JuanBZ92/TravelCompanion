using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TravelCompanion.Api.Options;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed class AppleStoreReceiptVerifier(
    IOptions<StorePurchaseOptions> options,
    AppleSignedDataVerifier signedDataVerifier,
    IHttpClientFactory httpClientFactory) : IStoreReceiptVerifier
{
    public StoreProvider Provider => StoreProvider.Apple;
    public async Task<VerifiedStorePurchase> VerifyAsync(string evidence, StoreEnvironment environment, CancellationToken cancellationToken)
    {
        try
        {
            if (!signedDataVerifier.TryReadVerifiedPayload(evidence, out var root))
                return Invalid("invalid_apple_jws");
            var initial = Parse(root, environment);
            if (!initial.IsValid || string.IsNullOrWhiteSpace(initial.TransactionId)) return initial;
            var settings = options.Value;
            if (string.IsNullOrWhiteSpace(settings.AppleIssuerId) || string.IsNullOrWhiteSpace(settings.AppleKeyId)
                || string.IsNullOrWhiteSpace(settings.ApplePrivateKeyPem))
                return Invalid("apple_server_api_not_configured");
            var baseUrl = initial.VerifiedEnvironment == StoreEnvironment.Sandbox
                ? "https://api.storekit-sandbox.apple.com" : "https://api.storekit.apple.com";
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{baseUrl}/inApps/v1/transactions/{Uri.EscapeDataString(initial.TransactionId)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateServerApiToken(settings));
            using var response = await httpClientFactory.CreateClient("AppleStoreServer").SendAsync(request, cancellationToken);
            if ((int)response.StatusCode is 408 or 429 || (int)response.StatusCode >= 500)
                throw new HttpRequestException("Apple Store Server API is temporarily unavailable.", null, response.StatusCode);
            if (!response.IsSuccessStatusCode) return Invalid($"apple_http_{(int)response.StatusCode}");
            using var status = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var signedCurrent = status.RootElement.GetProperty("signedTransactionInfo").GetString() ?? string.Empty;
            if (!signedDataVerifier.TryReadVerifiedPayload(signedCurrent, out var currentPayload))
                return Invalid("invalid_apple_status_jws");
            var current = Parse(currentPayload, initial.VerifiedEnvironment ?? environment);
            return current.TransactionId == initial.TransactionId ? current : Invalid("apple_transaction_mismatch");
        }
        catch (Exception exception) when (exception is JsonException or CryptographicException or FormatException or KeyNotFoundException)
        {
            return Invalid("invalid_apple_evidence");
        }
    }

    private VerifiedStorePurchase Parse(JsonElement root, StoreEnvironment environment)
    {
            var bundleId = root.GetProperty("bundleId").GetString();
            var productId = root.GetProperty("productId").GetString() ?? string.Empty;
            var transactionId = root.GetProperty("transactionId").GetString();
            var original = root.TryGetProperty("originalTransactionId", out var originalNode) ? originalNode.GetString() : null;
            var purchaseMs = root.GetProperty("purchaseDate").GetInt64();
            var appAccountToken = root.TryGetProperty("appAccountToken", out var accountNode)
                ? accountNode.GetString() : null;
            var payloadEnvironment = root.TryGetProperty("environment", out var environmentNode) ? environmentNode.GetString() : null;
            var expectedEnvironment = environment == StoreEnvironment.Sandbox ? "Sandbox" : "Production";
            if (bundleId != options.Value.AppleBundleId || productId != options.Value.AppleProductId
                || !string.Equals(payloadEnvironment, expectedEnvironment, StringComparison.OrdinalIgnoreCase))
                return Invalid("apple_claim_mismatch");
            var verifiedEnvironment = string.Equals(payloadEnvironment, "Sandbox", StringComparison.OrdinalIgnoreCase)
                ? StoreEnvironment.Sandbox : StoreEnvironment.Production;
            var revoked = root.TryGetProperty("revocationDate", out var revocation) && revocation.ValueKind == JsonValueKind.Number;
            var currency = root.TryGetProperty("currency", out var currencyNode) ? currencyNode.GetString() : null;
            decimal? gross = root.TryGetProperty("price", out var priceNode) && priceNode.TryGetDecimal(out var price)
                ? price / 1000m : null;
            return new VerifiedStorePurchase(!revoked, false, transactionId, original, productId,
                DateTimeOffset.FromUnixTimeMilliseconds(purchaseMs), ErrorCode: revoked ? "apple_revoked" : null,
                OpaqueAccountId: appAccountToken, VerifiedEnvironment: verifiedEnvironment, IsRevoked: revoked,
                Currency: currency, GrossAmount: gross);
    }

    private static string CreateServerApiToken(StorePurchaseOptions settings)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "ES256", kid = settings.AppleKeyId, typ = "JWT" }));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = settings.AppleIssuerId, iat = now, exp = now + 300, aud = "appstoreconnect-v1", bid = settings.AppleBundleId
        }));
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(settings.ApplePrivateKeyPem);
        var signature = ecdsa.SignData(Encoding.ASCII.GetBytes($"{header}.{payload}"), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{header}.{payload}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static VerifiedStorePurchase Invalid(string code) => new(false, false, null, null, string.Empty, default, ErrorCode: code);
}

public sealed class AppleSignedDataVerifier
{
    private readonly IReadOnlyList<X509Certificate2> trustedRoots;

    public AppleSignedDataVerifier(IOptions<StorePurchaseOptions> options)
    {
        trustedRoots = LoadTrustedRoots(options.Value.AppleRootCertificatesPath);
    }

    public bool TryReadVerifiedPayload(string evidence, out JsonElement payload)
    {
        payload = default;
        try
        {
            if (trustedRoots.Count == 0) return false;
            var parts = evidence.Split('.');
            if (parts.Length != 3) return false;
            using var header = JsonDocument.Parse(Decode(parts[0]));
            if (!header.RootElement.TryGetProperty("alg", out var alg) || alg.GetString() != "ES256"
                || !header.RootElement.TryGetProperty("x5c", out var certificates) || certificates.GetArrayLength() == 0)
                return false;
            using var leaf = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(certificates[0].GetString()!));
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.DisableCertificateDownloads = true;
            foreach (var root in trustedRoots) chain.ChainPolicy.CustomTrustStore.Add(root);
            foreach (var item in certificates.EnumerateArray().Skip(1))
                chain.ChainPolicy.ExtraStore.Add(X509CertificateLoader.LoadCertificate(Convert.FromBase64String(item.GetString()!)));
            if (!chain.Build(leaf)) return false;
            using var key = leaf.GetECDsaPublicKey();
            var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
            if (key is null || !key.VerifyData(signingInput, Decode(parts[2]), HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                return false;
            using var document = JsonDocument.Parse(Decode(parts[1]));
            payload = document.RootElement.Clone();
            return true;
        }
        catch (Exception exception) when (exception is JsonException or CryptographicException or FormatException)
        {
            return false;
        }
    }

    private static IReadOnlyList<X509Certificate2> LoadTrustedRoots(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return [];
        var roots = new List<X509Certificate2>();
        foreach (var file in Directory.EnumerateFiles(path))
        {
            try
            {
                if (Path.GetExtension(file).Equals(".pem", StringComparison.OrdinalIgnoreCase))
                {
                    var collection = new X509Certificate2Collection();
                    collection.ImportFromPem(File.ReadAllText(file));
                    roots.AddRange(collection.Cast<X509Certificate2>());
                }
                else
                {
                    roots.Add(X509CertificateLoader.LoadCertificateFromFile(file));
                }
            }
            catch (CryptographicException)
            {
                // Invalid files do not expand the trust store. Startup remains available so
                // operators can correct the certificate mount without risking false grants.
            }
        }
        return roots;
    }

    private static byte[] Decode(string value)
    {
        value = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(value.PadRight(value.Length + (4 - value.Length % 4) % 4, '='));
    }
}

public sealed class GooglePlayReceiptVerifier(
    IHttpClientFactory httpClientFactory,
    IOptions<StorePurchaseOptions> options) : IStoreReceiptVerifier
{
    public StoreProvider Provider => StoreProvider.Google;
    public async Task<VerifiedStorePurchase> VerifyAsync(string evidence, StoreEnvironment environment, CancellationToken cancellationToken)
    {
        if (environment == StoreEnvironment.Sandbox)
        {
            // Google test-track transactions use the production endpoint and are identified by license-test accounts.
            environment = StoreEnvironment.Production;
        }
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.GoogleServiceAccountJson)) return Invalid("google_verifier_not_configured");
        try
        {
            var token = evidence.Trim();
            if (token.StartsWith('{'))
            {
                using var evidenceJson = JsonDocument.Parse(token);
                token = evidenceJson.RootElement.GetProperty("purchaseToken").GetString() ?? string.Empty;
            }
            if (string.IsNullOrWhiteSpace(token)) return Invalid("invalid_google_token");
            var accessToken = await CreateAccessTokenAsync(httpClientFactory, settings.GoogleServiceAccountJson, cancellationToken);
            var client = httpClientFactory.CreateClient("GooglePlayPublisher");
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://androidpublisher.googleapis.com/androidpublisher/v3/applications/{Uri.EscapeDataString(settings.GooglePackageName)}/purchases/productsv2/tokens/{Uri.EscapeDataString(token)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await client.SendAsync(request, cancellationToken);
            if ((int)response.StatusCode is 408 or 429 || (int)response.StatusCode >= 500)
                throw new HttpRequestException("Google Play Developer API is temporarily unavailable.", null, response.StatusCode);
            if (!response.IsSuccessStatusCode) return Invalid($"google_http_{(int)response.StatusCode}");
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var root = document.RootElement;
            var state = root.GetProperty("purchaseStateContext").GetProperty("purchaseState").GetString();
            if (state == "PENDING") return new(false, true, null, null, settings.GoogleProductId, default);
            if (state == "CANCELLED") return Invalid("google_purchase_cancelled");
            if (state != "PURCHASED") return Invalid("google_purchase_not_completed");
            var line = root.GetProperty("productLineItem")[0];
            var productId = line.GetProperty("productId").GetString() ?? string.Empty;
            if (productId != settings.GoogleProductId) return Invalid("google_product_mismatch");
            var orderId = root.TryGetProperty("orderId", out var orderNode) ? orderNode.GetString() : null;
            var transactionId = string.IsNullOrWhiteSpace(orderId)
                ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))) : orderId;
            var purchasedAt = root.TryGetProperty("purchaseCompletionTime", out var timeNode)
                && DateTimeOffset.TryParse(timeNode.GetString(), out var parsed) ? parsed : DateTimeOffset.UtcNow;
            var accountId = root.TryGetProperty("obfuscatedExternalAccountId", out var accountNode)
                ? accountNode.GetString() : null;
            var verifiedEnvironment = root.TryGetProperty("testPurchaseContext", out _)
                ? StoreEnvironment.Sandbox : StoreEnvironment.Production;
            var orderAmount = string.IsNullOrWhiteSpace(orderId)
                ? null : await TryGetOrderAmountAsync(client, accessToken, settings.GooglePackageName,
                    orderId, cancellationToken);
            return new(true, false, transactionId, null, productId, purchasedAt,
                Currency: orderAmount?.Currency, GrossAmount: orderAmount?.Amount,
                OpaqueAccountId: accountId, VerifiedEnvironment: verifiedEnvironment);
        }
        catch (Exception exception) when (exception is JsonException or CryptographicException or FormatException)
        {
            return Invalid("invalid_google_evidence");
        }
    }

    internal static async Task<string> CreateAccessTokenAsync(IHttpClientFactory httpClientFactory, string serviceAccountJson, CancellationToken ct)
    {
        using var account = JsonDocument.Parse(serviceAccountJson);
        var email = account.RootElement.GetProperty("client_email").GetString()!;
        var privateKey = account.RootElement.GetProperty("private_key").GetString()!;
        var tokenUri = account.RootElement.TryGetProperty("token_uri", out var uriNode)
            ? uriNode.GetString()! : "https://oauth2.googleapis.com/token";
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        var claims = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = email, scope = "https://www.googleapis.com/auth/androidpublisher", aud = tokenUri, iat = now, exp = now + 3600
        }));
        using var rsa = RSA.Create(); rsa.ImportFromPem(privateKey);
        var assertion = $"{header}.{claims}.{Base64Url(rsa.SignData(Encoding.ASCII.GetBytes($"{header}.{claims}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))}";
        var client = httpClientFactory.CreateClient("GooglePlayOAuth");
        using var response = await client.PostAsync(tokenUri, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer", ["assertion"] = assertion
        }), ct);
        response.EnsureSuccessStatusCode();
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return result.RootElement.GetProperty("access_token").GetString()!;
    }
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static VerifiedStorePurchase Invalid(string code) => new(false, false, null, null, string.Empty, default, ErrorCode: code);

    private static async Task<GoogleOrderAmount?> TryGetOrderAmountAsync(HttpClient client, string accessToken,
        string packageName, string orderId, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://androidpublisher.googleapis.com/androidpublisher/v3/applications/{Uri.EscapeDataString(packageName)}/orders/{Uri.EscapeDataString(orderId)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return ParseOrderAmount(document.RootElement);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or FormatException
            && !ct.IsCancellationRequested)
        {
            // Price enrichment never invalidates an otherwise verified entitlement.
            return null;
        }
    }

    internal static GoogleOrderAmount? ParseOrderAmount(JsonElement order)
    {
        if (!order.TryGetProperty("total", out var total)
            || !total.TryGetProperty("currencyCode", out var currencyNode)) return null;
        var currency = currencyNode.GetString();
        if (string.IsNullOrWhiteSpace(currency)) return null;
        var units = total.TryGetProperty("units", out var unitsNode)
            && decimal.TryParse(unitsNode.GetString(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var parsedUnits) ? parsedUnits : 0m;
        var nanos = total.TryGetProperty("nanos", out var nanosNode) && nanosNode.TryGetInt32(out var parsedNanos)
            ? parsedNanos : 0;
        return new GoogleOrderAmount(currency, units + nanos / 1_000_000_000m);
    }

    internal sealed record GoogleOrderAmount(string Currency, decimal Amount);
}

public sealed class GooglePlayPurchaseFinalizer(
    IHttpClientFactory httpClientFactory,
    IOptions<StorePurchaseOptions> options) : IStorePurchaseFinalizer
{
    public StoreProvider Provider => StoreProvider.Google;

    public async Task<bool> FinalizeAsync(string providerToken, string productId, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.GoogleServiceAccountJson)) return false;
        var accessToken = await GooglePlayReceiptVerifier.CreateAccessTokenAsync(
            httpClientFactory, settings.GoogleServiceAccountJson, cancellationToken);
        var client = httpClientFactory.CreateClient("GooglePlayPublisher");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://androidpublisher.googleapis.com/androidpublisher/v3/applications/{Uri.EscapeDataString(settings.GooglePackageName)}/purchases/products/{Uri.EscapeDataString(productId)}/tokens/{Uri.EscapeDataString(providerToken)}:consume");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Conflict;
    }
}
