using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using TravelCompanion.Api.Options;

namespace TravelCompanion.Api.Services;

public interface ITransactionalEmailSender
{
    Task SendVerificationCodeAsync(string email, string code, string locale, CancellationToken cancellationToken);
}

public sealed class SmtpTransactionalEmailSender(
    IOptions<SmtpOptions> options,
    IWebHostEnvironment environment,
    ILogger<SmtpTransactionalEmailSender> logger) : ITransactionalEmailSender
{
    public async Task SendVerificationCodeAsync(string email, string code, string locale, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            {
                throw new InvalidOperationException("Transactional email is not configured.");
            }

            logger.LogInformation("Development verification code generated for EmailDomain={EmailDomain}; Code={Code}.",
                email.Split('@').LastOrDefault() ?? "unknown", code);
            return;
        }

        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(settings.Username, settings.Password)
        };
        var spanish = locale.StartsWith("es", StringComparison.OrdinalIgnoreCase);
        using var message = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, settings.FromName),
            Subject = spanish ? "Tu código de YUKU" : "Your YUKU code",
            Body = spanish
                ? $"Tu código de verificación es {code}. Caduca en 10 minutos."
                : $"Your verification code is {code}. It expires in 10 minutes.",
            IsBodyHtml = false
        };
        message.To.Add(email);
        await client.SendMailAsync(message, cancellationToken);
    }
}
