using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Options;
using TravelCompanion.Api.Services;

namespace TravelCompanion.Api.Tests;

public sealed class LaunchInfrastructureTests
{
    [Fact]
    public void Configuration_report_redacts_secrets_and_matches_option_defaults()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TransactionalEmail:Provider"] = "Resend", ["TransactionalEmail:ApiKey"] = "secret-api-token",
            ["TransactionalEmail:From"] = "private@example.test", ["DataProtection:CertificateBase64"] = "secret-certificate"
        }).Build();
        var json = JsonSerializer.Serialize(LaunchConfigurationReport.Create(config, "Production"));
        Assert.DoesNotContain("secret-api-token", json);
        Assert.DoesNotContain("secret-certificate", json);
        Assert.DoesNotContain("private@example.test", json);
        using var report = JsonDocument.Parse(json);
        Assert.True(report.RootElement.GetProperty("GoogleRoutesEnabled").GetBoolean());
        Assert.True(report.RootElement.GetProperty("EmailConfigured").GetBoolean());
        Assert.False(report.RootElement.GetProperty("DurableKeysConfigured").GetBoolean());
    }
    [Theory]
    [InlineData("es", "Caduca en 4 minutos")]
    [InlineData("en", "expires in 4 minutes")]
    public async Task Https_email_sends_the_code_and_configured_expiry(string locale, string expected)
    {
        var handler = new EmailHandler(HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        var sender = Sender(client);
        await sender.SendVerificationCodeAsync("traveler@example.test", "123456", locale, default);
        Assert.Equal("https://api.resend.com/emails", handler.Url);
        Assert.Equal("Bearer test-key", handler.Authorization);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("traveler@example.test", body.RootElement.GetProperty("to")[0].GetString());
        Assert.Contains("123456", body.RootElement.GetProperty("text").GetString());
        Assert.Contains(expected, body.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Email_failure_does_not_expose_provider_response()
    {
        using var client = new HttpClient(new EmailHandler(HttpStatusCode.TooManyRequests));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Sender(client)
            .SendVerificationCodeAsync("traveler@example.test", "123456", "es", default));
        Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
        Assert.DoesNotContain("private-provider-response", error.ToString());
    }

    [Fact]
    public void Protected_purchase_payload_survives_process_restart_and_requires_external_certificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=launch-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(5));
        var encoded = Convert.ToBase64String(cert.Export(X509ContentType.Pfx));
        var root = new InMemoryDatabaseRoot();
        ServiceProvider Create(string pfx)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<TravelCompanionDbContext>(o => o.UseInMemoryDatabase("durable-keys", root));
            services.AddLaunchInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:UseDatabase"] = "true", ["DataProtection:CertificateBase64"] = pfx
            }).Build());
            return services.BuildServiceProvider();
        }
        string protectedValue;
        using (var first = Create(encoded))
        {
            protectedValue = first.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("TravelCompanion.StorePurchaseEvidence.v1").Protect("pending-receipt");
            using var scope = first.CreateScope();
            var xml = Assert.Single(scope.ServiceProvider.GetRequiredService<TravelCompanionDbContext>().DataProtectionKeys).Xml;
            Assert.Contains("encryptedSecret", xml);
        }
        using (var restarted = Create(encoded))
            Assert.Equal("pending-receipt", restarted.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("TravelCompanion.StorePurchaseEvidence.v1").Unprotect(protectedValue));
        using var otherRsa = RSA.Create(2048);
        using var otherCert = new CertificateRequest("CN=wrong", otherRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(5));
        using var wrong = Create(Convert.ToBase64String(otherCert.Export(X509ContentType.Pfx)));
        Assert.Throws<CryptographicException>(() => wrong.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("TravelCompanion.StorePurchaseEvidence.v1").Unprotect(protectedValue));
    }

    private static ResendTransactionalEmailSender Sender(HttpClient client) => new(new ClientFactory(client),
        Microsoft.Extensions.Options.Options.Create(new TransactionalEmailOptions { ApiKey = "test-key", From = "YUKU <sender@example.test>" }),
        Microsoft.Extensions.Options.Options.Create(new EmailVerificationOptions { LifetimeMinutes = 4 }));
    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
    private sealed class EmailHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public string? Body, Url, Authorization;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            Url = request.RequestUri!.ToString();
            Authorization = request.Headers.Authorization!.ToString();
            return new(status) { Content = new StringContent("private-provider-response") };
        }
    }
}
