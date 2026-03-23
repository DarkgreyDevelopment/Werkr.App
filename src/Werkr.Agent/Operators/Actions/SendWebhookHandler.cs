using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Werkr.Common.Attributes;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Core.Security;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>SendWebhook</c> action — sends a JSON POST to a webhook endpoint.
/// The payload comes from the <see cref="SendWebhookParameters.Payload"/> parameter
/// or the input variable value (parameter takes precedence).
/// </summary>
/// <remarks>Creates a new <see cref="SendWebhookHandler"/>.</remarks>
[ActionCategory( "Network" )]
public sealed partial class SendWebhookHandler(
    IUrlValidator urlValidator,
    IHttpClientFactory httpClientFactory,
    ILogger<SendWebhookHandler> logger
    ) : IActionHandler {

    private readonly IUrlValidator _urlValidator = urlValidator;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ILogger<SendWebhookHandler> _logger = logger;

    /// <inheritdoc/>
    public string Action => "SendWebhook";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        string? inputVariableValue = null,
        CancellationToken cancellationToken = default
    ) {
        try {
            SendWebhookParameters p = parameters.Deserialize<SendWebhookParameters>( ActionJson.SerializerOptions )
                ?? throw new ArgumentException( "Failed to deserialize SendWebhook parameters." );

            // 1. Validate URL
            Uri uri = _urlValidator.ValidateUrl( p.Url );

            // 2. Resolve payload: parameter > inputVariableValue > empty
            string body = p.Payload ?? inputVariableValue ?? "{}";

            // 3. Send POST
            HttpClient client = _httpClientFactory.CreateClient( "WerkrActions" );
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
            timeoutCts.CancelAfter( TimeSpan.FromSeconds( p.TimeoutSeconds ) );

            using HttpRequestMessage request = new( HttpMethod.Post, uri );
            request.Content = new StringContent( body, Encoding.UTF8, "application/json" );

            if (p.Headers is not null) {
                foreach (KeyValuePair<string, string> header in p.Headers) {
                    _ = request.Headers.TryAddWithoutValidation( header.Key, header.Value );
                }
            }

            Stopwatch sw = Stopwatch.StartNew( );
            using HttpResponseMessage response = await client.SendAsync( request, timeoutCts.Token );
            string responseBody = await response.Content.ReadAsStringAsync( timeoutCts.Token );
            sw.Stop( );

            int statusCode = (int)response.StatusCode;
            long elapsedMs = sw.ElapsedMilliseconds;

            await output.WriteAsync(
                OperatorOutput.Create(
                    LogLevel.Information,
                    $"SendWebhook: POST {uri} → {statusCode} ({elapsedMs}ms)" ),
                cancellationToken );

            string outputJson = JsonSerializer.Serialize( new {
                statusCode,
                responseBody,
                elapsedMs,
            }, ActionJson.SerializerOptions );

            return new ActionOperatorResult( Success: true, OutputVariableValue: outputJson );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            LogActionFailed( _logger, ex );
            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Error, $"SendWebhook failed: {ex.Message}" ),
                cancellationToken );
            return new ActionOperatorResult( Success: false, Exception: ex );
        }
    }

    [LoggerMessage( Level = LogLevel.Error, Message = "Action failed" )]
    private static partial void LogActionFailed( ILogger logger, Exception ex );
}
