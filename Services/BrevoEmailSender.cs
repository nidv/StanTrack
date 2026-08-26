using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using MimeKit;
using StanTrack.Models;

namespace StanTrack.Services
{
    // Two interfaces are in play:
    // - IEmailSender (from Identity.UI.Services) has SendEmailAsync(email, subject, htmlMessage). Used for generic email.
    // - IEmailSender<ApplicationUser> has typed methods like SendPasswordResetLinkAsync. This is what Identity's
    //   default ForgotPassword actually invokes. We must implement BOTH for password reset to send.
    public class BrevoEmailSender : IEmailSender, IEmailSender<ApplicationUser>
    {
        private readonly IConfiguration _config;
        private readonly ILogger<BrevoEmailSender> _logger;

        public BrevoEmailSender(IConfiguration config, ILogger<BrevoEmailSender> logger)
        {
            _config = config;
            _logger = logger;
        }

        public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
            => SendEmailAsync(email, "Confirm your StanTrack email",
                $"Please confirm your StanTrack account by <a href='{confirmationLink}'>clicking here</a>.");

        public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
            => SendEmailAsync(email, "Reset your StanTrack password",
                $"Reset your StanTrack password by <a href='{resetLink}'>clicking here</a>. If you didn't request this, you can ignore this email.");

        public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
            => SendEmailAsync(email, "Reset your StanTrack password",
                $"Your StanTrack password reset code is: <strong>{resetCode}</strong>. If you didn't request this, you can ignore this email.");

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            var host = _config["Email:Brevo:Host"];
            var portString = _config["Email:Brevo:Port"];
            var username = _config["Email:Brevo:Username"];
            var password = _config["Email:Brevo:Password"];
            var fromAddress = _config["Email:Brevo:FromAddress"];
            var fromName = _config["Email:Brevo:FromName"] ?? "StanTrack";

            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(fromAddress))
            {
                _logger.LogWarning("Brevo email settings are incomplete; skipping send to {Email}", email);
                return;
            }

            var port = int.TryParse(portString, out var p) ? p : 587;

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(fromName, fromAddress));
            message.To.Add(MailboxAddress.Parse(email));
            message.Subject = subject;
            message.Body = new BodyBuilder { HtmlBody = htmlMessage }.ToMessageBody();

            try
            {
                using var client = new SmtpClient();
                await client.ConnectAsync(host, port, SecureSocketOptions.StartTls);
                await client.AuthenticateAsync(username, password);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
                _logger.LogInformation("Sent email to {Email} via Brevo (subject: {Subject})", email, subject);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {Email} via Brevo (subject: {Subject})", email, subject);
                throw;
            }
        }
    }
}
