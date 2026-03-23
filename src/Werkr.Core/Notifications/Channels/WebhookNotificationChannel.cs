using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Werkr.Core.Notifications.Models;
using Werkr.Data;
using Werkr.Data.Entities.Configuration;

namespace Werkr.Core.Notifications.Channels;

/// <summary>
/// Webhook notification channel — delivers notifications via HTTP POST with JSON payload.
/// Supports header-based auth, HMAC-SHA-512 signature, or no auth.
/// This channel is distinct from the SendWebhook action handler.
/// </summary>
public sealed partial class WebhookNotificationChannel(
    HttpClient httpClient,
    WerkrDbContext db,
    ILogger<WebhookNotificationChannel> logger
) : INotificationChannel {

    private static readonly JsonSerializerOptions s_jsonOptions = new( JsonSerializerDefaults.Web );

    /// <inheritdoc/>
    public string ChannelType => "webhook";

    /// <inheritdoc/>
    public async Task<DeliveryResult> DeliverAsync( NotificationDeliveryRequest request, CancellationToken ct ) {
        try {
            WebhookChannelConfig config = JsonSerializer.Deserialize<WebhookChannelConfig>(
                request.ChannelConfig.ConfigurationJson, s_jsonOptions )
                ?? throw new InvalidOperationException( "Failed to deserialize webhook channel configuration." );

            if (string.IsNullOrWhiteSpace( config.Url )) {
                return new DeliveryResult( false, "Webhook URL is not configured.", DateTime.UtcNow );
            }

            // Build JSON payload
            object payload = new {
                eventType = request.EventTypeId,
                eventCategory = request.EventCategoryId,
                timestamp = DateTime.UtcNow.ToString( "O" ),
                data = request.TemplateVariables,
            };

            string payloadJson = JsonSerializer.Serialize( payload, s_jsonOptions );
            byte[] payloadBytes = Encoding.UTF8.GetBytes( payloadJson );

            using HttpRequestMessage httpRequest = new( HttpMethod.Post, config.Url );
            httpRequest.Content = new ByteArrayContent( payloadBytes );
            httpRequest.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue( "application/json" );

            // Apply authentication
            await ApplyAuthenticationAsync( httpRequest, payloadBytes, config, ct );

            using HttpResponseMessage response = await httpClient.SendAsync( httpRequest, ct );

            if (response.IsSuccessStatusCode) {
                LogWebhookSent( logger, config.Url, request.EventTypeId );
                return new DeliveryResult( true, null, DateTime.UtcNow );
            }

            string errorBody = await response.Content.ReadAsStringAsync( ct );
            string errorMessage = $"HTTP {(int) response.StatusCode}: {errorBody}";

            LogWebhookFailed( logger, config.Url, (int)response.StatusCode );
            return new DeliveryResult( false, errorMessage, DateTime.UtcNow );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            LogWebhookException( logger, request.EventTypeId, ex );
            return new DeliveryResult( false, ex.Message, DateTime.UtcNow );
        }
    }

    private async Task ApplyAuthenticationAsync(
        HttpRequestMessage httpRequest,
        byte[] payloadBytes,
        WebhookChannelConfig config,
        CancellationToken ct
    ) {
        if (string.IsNullOrEmpty( config.AuthType )) {
            return;
        }

        if (string.Equals( config.AuthType, "header", StringComparison.OrdinalIgnoreCase )) {
            if (string.IsNullOrEmpty( config.AuthCredentialName )) {
                return;
            }

            Credential? credential = await db.Credentials
                .AsNoTracking( )
                .FirstOrDefaultAsync( c => c.Name == config.AuthCredentialName, ct ) ?? throw new InvalidOperationException( $"Auth credential '{config.AuthCredentialName}' not found." );
            string headerName = string.IsNullOrEmpty( config.AuthHeaderName )
                ? "X-Werkr-Secret"
                : config.AuthHeaderName;

            _ = httpRequest.Headers.TryAddWithoutValidation( headerName, credential.EncryptedValue );
        } else if (string.Equals( config.AuthType, "hmac", StringComparison.OrdinalIgnoreCase )) {
            if (string.IsNullOrEmpty( config.HmacCredentialName )) {
                return;
            }

            Credential? credential = await db.Credentials
                .AsNoTracking( )
                .FirstOrDefaultAsync( c => c.Name == config.HmacCredentialName, ct ) ?? throw new InvalidOperationException( $"HMAC credential '{config.HmacCredentialName}' not found." );
            byte[] secretBytes = Encoding.UTF8.GetBytes( credential.EncryptedValue );
            string signature = ComputeHmacSha512( payloadBytes, secretBytes );
            _ = httpRequest.Headers.TryAddWithoutValidation( "X-Werkr-Signature", $"sha512={signature}" );
        }
    }

    /// <summary>
    /// Computes HMAC-SHA-512 of the payload using the shared secret and returns the hex-encoded result.
    /// </summary>
    internal static string ComputeHmacSha512( byte[] payload, byte[] secret ) {
        byte[] hash = HMACSHA512.HashData( secret, payload );
        return Convert.ToHexStringLower( hash );
    }

    [LoggerMessage( Level = LogLevel.Information, Message = "Webhook notification sent to {Url} for {EventType}" )]
    private static partial void LogWebhookSent( ILogger logger, string url, string eventType );

    [LoggerMessage( Level = LogLevel.Warning, Message = "Webhook delivery to {Url} returned {StatusCode}" )]
    private static partial void LogWebhookFailed( ILogger logger, string url, int statusCode );

    [LoggerMessage( Level = LogLevel.Error, Message = "Webhook delivery exception for {EventType}" )]
    private static partial void LogWebhookException( ILogger logger, string eventType, Exception ex );
}
