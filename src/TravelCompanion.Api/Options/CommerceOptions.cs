namespace TravelCompanion.Api.Options;

public sealed class EmailVerificationOptions
{
    public const string SectionName = "EmailVerification";
    public string HashSecret { get; set; } = string.Empty;
    public int LifetimeMinutes { get; set; } = 10;
    public int MaximumAttempts { get; set; } = 5;
    public int ResendSeconds { get; set; } = 60;
    public int MaximumRequestsPerHour { get; set; } = 5;
}

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";
    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "YUKU";
}

public sealed class StorePurchaseOptions
{
    public const string SectionName = "StorePurchases";
    public bool NewPurchasesEnabled { get; set; }
    public string AppleProductId { get; set; } = "com.yuku.travelcompanion.japan.trip";
    public string AppleBundleId { get; set; } = "com.yuku.travelcompanion.app";
    public string AppleRootCertificatesPath { get; set; } = string.Empty;
    public string AppleIssuerId { get; set; } = string.Empty;
    public string AppleKeyId { get; set; } = string.Empty;
    public string ApplePrivateKeyPem { get; set; } = string.Empty;
    public string GoogleProductId { get; set; } = "japan_trip_pass";
    public string GooglePackageName { get; set; } = "com.yuku.travelcompanion.app";
    public string GoogleServiceAccountJson { get; set; } = string.Empty;
    public string GoogleRtdnAudience { get; set; } = string.Empty;
    public string GoogleRtdnServiceAccountEmail { get; set; } = string.Empty;
    public int DailyAssistantLimit { get; set; } = 30;
    public decimal ReferencePrice { get; set; } = 24.99m;
    public string ReferenceCurrency { get; set; } = "EUR";
}

public sealed class ProductFeatureOptions
{
    public const string SectionName = "ProductFeatures";
    public bool AnalyticsEnabled { get; set; } = true;
    public bool PaywallEnabled { get; set; } = true;
    public bool ProposalsEnabled { get; set; } = true;
    public bool RoutesEnabled { get; set; } = true;
}
