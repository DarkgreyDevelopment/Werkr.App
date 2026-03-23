using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MimeKit;
using Werkr.Core.Notifications.Models;
using Werkr.Data;
using Werkr.Data.Entities.Configuration;

namespace Werkr.Core.Notifications.Channels;

/// <summary>
/// Email notification channel — delivers notifications via SMTP using MailKit.
/// SMTP credentials are resolved from the credential store by name.
/// </summary>
public sealed partial class EmailNotificationChannel(
    WerkrDbContext db,
    ILogger<EmailNotificationChannel> logger
) : INotificationChannel {

    private static readonly JsonSerializerOptions s_jsonOptions = new( JsonSerializerDefaults.Web );

    /// <inheritdoc/>
    public string ChannelType => "email";

    /// <inheritdoc/>
    public async Task<DeliveryResult> DeliverAsync( NotificationDeliveryRequest request, CancellationToken ct ) {
        try {
            EmailChannelConfig config = JsonSerializer.Deserialize<EmailChannelConfig>(
                request.ChannelConfig.ConfigurationJson, s_jsonOptions )
                ?? throw new InvalidOperationException( "Failed to deserialize email channel configuration." );

            // Resolve SMTP credential from credential store by name
            Credential? credential = await db.Credentials
                .AsNoTracking( )
                .FirstOrDefaultAsync( c => c.Name == config.SmtpCredentialName, ct );

            if (credential is null) {
                return new DeliveryResult( false, $"SMTP credential '{config.SmtpCredentialName}' not found.", DateTime.UtcNow );
            }

            SmtpCredentialValue smtpConfig = JsonSerializer.Deserialize<SmtpCredentialValue>(
                credential.EncryptedValue, s_jsonOptions )
                ?? throw new InvalidOperationException( $"Failed to deserialize SMTP credential '{config.SmtpCredentialName}'." );

            // Build message
            MimeMessage message = new( );
            message.From.Add( new MailboxAddress( config.SenderDisplayName, config.SenderAddress ) );
            message.To.Add( MailboxAddress.Parse( request.RecipientId ) );

            // Subject and body come from template variables (rendered by the delivery pipeline)
            message.Subject = request.TemplateVariables.TryGetValue( "_renderedSubject", out string? subject )
                ? subject
                : $"[Werkr] {request.EventTypeId}";

            BodyBuilder bodyBuilder = new( );
            if (request.TemplateVariables.TryGetValue( "_renderedBody", out string? body )) {
                bodyBuilder.HtmlBody = body;
                // Generate plain-text fallback by stripping HTML tags
                bodyBuilder.TextBody = StripHtmlTags( body );
            } else {
                bodyBuilder.TextBody = $"Notification: {request.EventTypeId}";
            }

            message.Body = bodyBuilder.ToMessageBody( );

            // Send via SMTP
            using SmtpClient smtp = new( );
            SecureSocketOptions socketOptions = smtpConfig.UseTls
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;

            await smtp.ConnectAsync( smtpConfig.Host, smtpConfig.Port, socketOptions, ct );

            if (!string.IsNullOrEmpty( smtpConfig.Username )) {
                await smtp.AuthenticateAsync( smtpConfig.Username, smtpConfig.Password, ct );
            }

            _ = await smtp.SendAsync( message, ct );
            await smtp.DisconnectAsync( quit: true, ct );

            LogEmailSent( logger, request.RecipientId, request.EventTypeId );
            return new DeliveryResult( true, null, DateTime.UtcNow );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            LogEmailFailed( logger, request.RecipientId, ex );
            return new DeliveryResult( false, ex.Message, DateTime.UtcNow );
        }
    }

    private static string StripHtmlTags( string html ) {
        // Simple HTML tag stripping — adequate for notification plain-text fallback
        return HtmlTagPattern( ).Replace( html, string.Empty ).Trim( );
    }

    [System.Text.RegularExpressions.GeneratedRegex( @"<[^>]+>" )]
    private static partial System.Text.RegularExpressions.Regex HtmlTagPattern( );

    /// <summary>SMTP credential format matching the SendEmail action handler.</summary>
    private sealed class SmtpCredentialValue {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 587;
        public bool UseTls { get; set; } = true;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    [LoggerMessage( Level = LogLevel.Information, Message = "Email notification sent to {Recipient} for {EventType}" )]
    private static partial void LogEmailSent( ILogger logger, string recipient, string eventType );

    [LoggerMessage( Level = LogLevel.Error, Message = "Email notification delivery failed for {Recipient}" )]
    private static partial void LogEmailFailed( ILogger logger, string recipient, Exception ex );
}
