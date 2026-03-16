using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Channels;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Core.Security;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>TestConnection</c> action — tests connectivity to a host/port using
/// TCP, HTTP, or HTTPS. Returns reachability data rather than throwing on unreachable targets.
/// </summary>
/// <remarks>Creates a new <see cref="TestConnectionHandler"/>.</remarks>
public sealed partial class TestConnectionHandler(
    IUrlValidator urlValidator,
    IHttpClientFactory httpClientFactory,
    ILogger<TestConnectionHandler> logger
    ) : IActionHandler {

    private readonly IUrlValidator _urlValidator = urlValidator;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ILogger<TestConnectionHandler> _logger = logger;

    /// <inheritdoc/>
    public string Action => "TestConnection";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        string? inputVariableValue = null,
        CancellationToken cancellationToken = default
    ) {
        try {
            TestConnectionParameters p = parameters.Deserialize<TestConnectionParameters>( ActionJson.SerializerOptions )
                ?? throw new ArgumentException( "Failed to deserialize TestConnection parameters." );

            bool reachable;
            int? statusCode = null;
            string? error;
            long elapsedMs;

            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
            timeoutCts.CancelAfter( TimeSpan.FromSeconds( p.TimeoutSeconds ) );

            if (p.Protocol is ConnectionProtocol.Http or ConnectionProtocol.Https) {
                string scheme = p.Protocol == ConnectionProtocol.Https ? "https" : "http";
                string url = $"{scheme}://{p.Host}:{p.Port}/";

                // Validate through IUrlValidator (includes SSRF check)
                Uri uri = _urlValidator.ValidateUrl( url );

                (reachable, statusCode, error, elapsedMs) = await TestHttpAsync(
                    uri, p.ExpectedStatusCode, timeoutCts.Token );
            } else {
                // TCP mode — validate host via URL validator if it's not a raw IP
                // Build a synthetic URL for validation
                string syntheticUrl = $"http://{p.Host}:{p.Port}/";
                _ = _urlValidator.ValidateUrl( syntheticUrl );

                (reachable, error, elapsedMs) = await TestTcpAsync(
                    p.Host, p.Port, timeoutCts.Token );
            }

            string protocol = p.Protocol.ToString( ).ToUpperInvariant( );
            string status = reachable ? "reachable" : "unreachable";
            await output.WriteAsync(
                OperatorOutput.Create(
                    LogLevel.Information,
                    $"TestConnection: {protocol} {p.Host}:{p.Port} is {status} ({elapsedMs}ms)" ),
                cancellationToken );

            string outputJson = JsonSerializer.Serialize( new {
                host = p.Host,
                port = p.Port,
                protocol,
                reachable,
                statusCode,
                error,
                elapsedMs,
            }, ActionJson.SerializerOptions );

            return new ActionOperatorResult( Success: true, OutputVariableValue: outputJson );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            LogActionFailed( _logger, ex );
            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Error, $"TestConnection failed: {ex.Message}" ),
                cancellationToken );
            return new ActionOperatorResult( Success: false, Exception: ex );
        }
    }

    private async Task<(bool Reachable, int? StatusCode, string? Error, long ElapsedMs)> TestHttpAsync(
        Uri uri, int? expectedStatusCode, CancellationToken cancellationToken
    ) {
        Stopwatch sw = Stopwatch.StartNew( );
        try {
            HttpClient client = _httpClientFactory.CreateClient( "WerkrActions" );
            using HttpRequestMessage request = new( HttpMethod.Head, uri );
            using HttpResponseMessage response = await client.SendAsync( request, cancellationToken );

            sw.Stop( );
            int code = (int)response.StatusCode;

            bool reachable = true;
            if (expectedStatusCode.HasValue && code != expectedStatusCode.Value) {
                reachable = false;
            }

            return (reachable, code, null, sw.ElapsedMilliseconds);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            sw.Stop( );
            return (false, null, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    private static async Task<(bool Reachable, string? Error, long ElapsedMs)> TestTcpAsync(
        string host, int port, CancellationToken cancellationToken
    ) {
        Stopwatch sw = Stopwatch.StartNew( );
        try {
            using TcpClient tcp = new( );
            await tcp.ConnectAsync( host, port, cancellationToken );
            sw.Stop( );
            return (true, null, sw.ElapsedMilliseconds);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            sw.Stop( );
            return (false, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    [LoggerMessage( Level = LogLevel.Error, Message = "Action failed" )]
    private static partial void LogActionFailed( ILogger logger, Exception ex );
}
