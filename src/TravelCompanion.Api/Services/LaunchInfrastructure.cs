using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Options;

namespace TravelCompanion.Api.Services;

public static class LaunchInfrastructure
{
    public static void AddLaunchInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TransactionalEmailOptions>(configuration.GetSection("TransactionalEmail"));
        services.AddHttpClient("TransactionalEmail", client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .RemoveAllLoggers();
        services.AddScoped<SmtpTransactionalEmailSender>();
        services.AddScoped<ResendTransactionalEmailSender>();
        services.AddScoped<ITransactionalEmailSender>(provider =>
            configuration["TransactionalEmail:Provider"] switch
            {
                null or "" or "Smtp" => provider.GetRequiredService<SmtpTransactionalEmailSender>(),
                "Resend" => provider.GetRequiredService<ResendTransactionalEmailSender>(),
                _ => throw new InvalidOperationException("Unknown transactional email provider.")
            });

        var protection = services.AddDataProtection().SetApplicationName("TravelCompanion.Api");
        if (configuration.GetValue<bool>("DataProtection:UseDatabase"))
        {
            var encoded = configuration["DataProtection:CertificateBase64"];
            if (string.IsNullOrWhiteSpace(encoded))
                throw new InvalidOperationException("Database key storage requires DataProtection:CertificateBase64.");
            var certificate = X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(encoded),
                configuration["DataProtection:CertificatePassword"], X509KeyStorageFlags.EphemeralKeySet);
            if (!certificate.HasPrivateKey)
                throw new InvalidOperationException("Data protection requires a certificate with its private key.");
            protection.PersistKeysToDbContext<TravelCompanionDbContext>().ProtectKeysWithCertificate(certificate);
            // Keep previous certificates available until every key they protect has been retired safely.
            var previous = configuration.GetSection("DataProtection:PreviousCertificates").Get<string[]>() ?? [];
            protection.UnprotectKeysWithAnyCertificate([certificate, .. previous.Select(value =>
                X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(value),
                    configuration["DataProtection:CertificatePassword"], X509KeyStorageFlags.EphemeralKeySet))]);
        }
        else if (configuration["DataProtection:KeysPath"] is { Length: > 0 } path)
            protection.PersistKeysToFileSystem(new DirectoryInfo(path));
    }
}

public sealed class TransactionalEmailOptions
{
    public string Provider { get; set; } = "Smtp";
    public string ApiKey { get; set; } = "";
    public string From { get; set; } = "";
}

public sealed class ResendTransactionalEmailSender(IHttpClientFactory clients,
    IOptions<TransactionalEmailOptions> options, IOptions<EmailVerificationOptions> verification) : ITransactionalEmailSender
{
    public async Task SendVerificationCodeAsync(string email, string code, string locale, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.ApiKey) || string.IsNullOrWhiteSpace(options.Value.From))
            throw new InvalidOperationException("Transactional email is not configured.");
        var spanish = locale.StartsWith("es", StringComparison.OrdinalIgnoreCase);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ApiKey);
        request.Content = JsonContent.Create(new
        {
            from = options.Value.From, to = new[] { email },
            subject = spanish ? "Tu código de YUKU" : "Your YUKU code",
            text = spanish
                ? $"Tu código de verificación es {code}. Caduca en {Math.Clamp(verification.Value.LifetimeMinutes, 1, 30)} minutos."
                : $"Your verification code is {code}. It expires in {Math.Clamp(verification.Value.LifetimeMinutes, 1, 30)} minutes."
        });
        using var response = await clients.CreateClient("TransactionalEmail").SendAsync(request, cancellationToken);
        // Provider response bodies can contain personal information. Do not log or surface them.
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("Transactional email delivery failed.", null, response.StatusCode);
    }
}
