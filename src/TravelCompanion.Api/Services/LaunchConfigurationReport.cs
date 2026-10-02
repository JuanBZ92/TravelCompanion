namespace TravelCompanion.Api.Services;

/// <summary>Reports configuration presence only; never credentials or provider responses.</summary>
public static class LaunchConfigurationReport
{
    public static object Create(IConfiguration configuration, string environment) => new
    {
        Environment = environment,
        NewPurchasesEnabled = configuration.GetValue<bool>("StorePurchases:NewPurchasesEnabled"),
        PersistentFreePercent = configuration.GetValue<int>("FreePreview:PersistentFreePercent"),
        EmailProvider = configuration["TransactionalEmail:Provider"] ?? "Smtp",
        EmailConfigured = configuration["TransactionalEmail:Provider"] == "Resend"
            ? Present(configuration, "TransactionalEmail:ApiKey") && Present(configuration, "TransactionalEmail:From")
            : configuration.GetValue<bool>("Smtp:Enabled") && Present(configuration, "Smtp:Host"),
        DurableKeysConfigured = configuration.GetValue<bool>("DataProtection:UseDatabase")
            && Present(configuration, "DataProtection:CertificateBase64"),
        KeyDirectoryConfigured = Present(configuration, "DataProtection:KeysPath"),
        EmailHashSecretConfigured = Present(configuration, "EmailVerification:HashSecret"),
        OpenAiEnabled = configuration.GetValue<bool>("OpenAI:Enabled"),
        GooglePlacesEnabled = configuration.GetValue<bool>("GooglePlaces:Enabled"),
        GoogleRoutesEnabled = configuration.GetValue("GoogleRoutes:Enabled", true),
        MigrationsOnStartup = configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"),
        ProviderAndDeviceValidation = "Not checked by this report"
    };

    private static bool Present(IConfiguration configuration, string key) => !string.IsNullOrWhiteSpace(configuration[key]);
}
