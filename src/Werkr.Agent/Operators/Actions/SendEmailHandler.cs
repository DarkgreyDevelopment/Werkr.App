using System.Text.Json;
using System.Threading.Channels;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Werkr.Common.Attributes;
using Werkr.Common.Models;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Core.Security;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>SendEmail</c> action — sends an email via SMTP using MailKit.
/// Credentials are loaded from <see cref="ISecretStore"/> when
/// <see cref="SendEmailParameters.CredentialName"/> is provided.
/// Attachments are resolved through <see cref="IFilePathResolver"/>.
/// </summary>
/// <remarks>Creates a new <see cref="SendEmailHandler"/>.</remarks>
[ActionCategory( "Network" )]
public sealed partial class SendEmailHandler(
    IOptionsMonitor<ActionOperatorConfiguration> config,
    ISecretStore secretStore,
    IFilePathResolver resolver,
    ILogger<SendEmailHandler> logger
    ) : IActionHandler {

    private readonly IOptionsMonitor<ActionOperatorConfiguration> _config = config;
    private readonly ISecretStore _secretStore = secretStore;
    private readonly IFilePathResolver _resolver = resolver;
    private readonly ILogger<SendEmailHandler> _logger = logger;

    /// <inheritdoc/>
    public string Action => "SendEmail";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        string? inputVariableValue = null,
        CancellationToken cancellationToken = default
    ) {
        try {
            ActionOperatorConfiguration config = _config.CurrentValue;
            if (!config.EnableNetworkActions) {
                throw new UnauthorizedAccessException(
                    "Network actions are disabled. Set EnableNetworkActions to true in configuration." );
            }

            SendEmailParameters p = parameters.Deserialize<SendEmailParameters>( ActionJson.SerializerOptions )
                ?? throw new ArgumentException( "Failed to deserialize SendEmail parameters." );

            if (p.To.Length == 0) {
                throw new ArgumentException( "At least one recipient is required." );
            }

            // 1. Build message
            MimeMessage message = new( );
            message.From.Add( MailboxAddress.Parse( p.From ) );
            foreach (string to in p.To) {
                message.To.Add( MailboxAddress.Parse( to ) );
            }

            if (p.Cc is not null) {
                foreach (string cc in p.Cc) {
                    message.Cc.Add( MailboxAddress.Parse( cc ) );
                }
            }

            message.Subject = p.Subject;

            // Body: parameter > inputVariableValue > empty
            string bodyText = p.Body ?? inputVariableValue ?? string.Empty;

            BodyBuilder bodyBuilder = new( );
            if (p.IsHtml) {
                bodyBuilder.HtmlBody = bodyText;
            } else {
                bodyBuilder.TextBody = bodyText;
            }

            // 2. Attachments
            if (p.Attachments is not null) {
                foreach (string attachment in p.Attachments) {
                    string resolved = _resolver.ResolveSinglePath( attachment );
                    if (!File.Exists( resolved )) {
                        throw new FileNotFoundException(
                            $"Attachment file not found: '{resolved}'" );
                    }
                    _ = await bodyBuilder.Attachments.AddAsync( resolved, cancellationToken );
                }
            }

            message.Body = bodyBuilder.ToMessageBody( );

            // 3. Send
            using SmtpClient smtp = new( );

            SecureSocketOptions socketOptions = p.UseSsl
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;

            await smtp.ConnectAsync( p.SmtpHost, p.Port, socketOptions, cancellationToken );

            if (!string.IsNullOrEmpty( p.CredentialName )) {
                // Try server-resolved credentials first (from dispatch), then fall back to local secret store
                string? secretJson = Werkr.Core.Credentials.ResolvedCredentialContext.TryResolve( p.CredentialName )
                    ?? await _secretStore.GetSecretAsync( p.CredentialName );

                if (string.IsNullOrEmpty( secretJson )) {
                    throw new InvalidOperationException(
                        $"Credential '{p.CredentialName}' not found in resolved credentials or secret store." );
                }

                SmtpCredentials creds = JsonSerializer.Deserialize<SmtpCredentials>( secretJson, ActionJson.SerializerOptions )
                    ?? throw new InvalidOperationException(
                        $"Failed to deserialize credentials for '{p.CredentialName}'." );

                await smtp.AuthenticateAsync( creds.Username, creds.Password, cancellationToken );
            }

            _ = await smtp.SendAsync( message, cancellationToken );
            await smtp.DisconnectAsync( quit: true, cancellationToken );

            int recipientCount = p.To.Length + (p.Cc?.Length ?? 0);
            await output.WriteAsync(
                OperatorOutput.Create(
                    LogLevel.Information,
                    $"SendEmail: sent to {recipientCount} recipient(s) via {p.SmtpHost}:{p.Port}" ),
                cancellationToken );

            string outputJson = JsonSerializer.Serialize( new {
                sent = true,
                recipientCount,
                subject = p.Subject,
            }, ActionJson.SerializerOptions );

            return new ActionOperatorResult( Success: true, OutputVariableValue: outputJson );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            LogActionFailed( _logger, ex );
            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Error, $"SendEmail failed: {ex.Message}" ),
                cancellationToken );
            return new ActionOperatorResult( Success: false, Exception: ex );
        }
    }

    /// <summary>Represents the SMTP credentials stored in the secret store.</summary>
    private sealed record SmtpCredentials {
        /// <summary>SMTP username.</summary>
        public required string Username { get; init; }

        /// <summary>SMTP password.</summary>
        public required string Password { get; init; }
    }

    [LoggerMessage( Level = LogLevel.Error, Message = "Action failed" )]
    private static partial void LogActionFailed( ILogger logger, Exception ex );
}
