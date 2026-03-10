using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Core.Security;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>DownloadFile</c> action — downloads a file from a URL to a local
/// destination path. Streams the response body to disk and reports download progress.
/// </summary>
public sealed class DownloadFileHandler : IActionHandler {

    private readonly IUrlValidator _urlValidator;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFilePathResolver _resolver;
    private readonly ILogger<DownloadFileHandler> _logger;

    /// <summary>Creates a new <see cref="DownloadFileHandler"/>.</summary>
    public DownloadFileHandler(
        IUrlValidator urlValidator,
        IHttpClientFactory httpClientFactory,
        IFilePathResolver resolver,
        ILogger<DownloadFileHandler> logger
    ) {
        _urlValidator = urlValidator;
        _httpClientFactory = httpClientFactory;
        _resolver = resolver;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Action => "DownloadFile";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        string? inputVariableValue = null,
        CancellationToken cancellationToken = default
    ) {
        try {
            DownloadFileParameters p = parameters.Deserialize<DownloadFileParameters>( ActionJson.SerializerOptions )
                ?? throw new ArgumentException( "Failed to deserialize DownloadFile parameters." );

            // 1. Validate URL (includes EnableNetworkActions gate + SSRF protection)
            Uri uri = _urlValidator.ValidateUrl( p.Url );

            // 2. Validate destination path
            string fullDest = _resolver.ResolveSinglePath( p.Destination );

            // 3. Check overwrite
            if (File.Exists( fullDest ) && !p.Overwrite) {
                throw new IOException(
                    $"Destination file already exists: '{fullDest}'. Set Overwrite to true to replace." );
            }

            // 4. Send GET request
            HttpClient client = _httpClientFactory.CreateClient( "WerkrActions" );
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
            timeoutCts.CancelAfter( TimeSpan.FromSeconds( p.TimeoutSeconds ) );

            using HttpRequestMessage request = new( HttpMethod.Get, uri );
            if (p.Headers is not null) {
                foreach (KeyValuePair<string, string> header in p.Headers) {
                    _ = request.Headers.TryAddWithoutValidation( header.Key, header.Value );
                }
            }

            Stopwatch sw = Stopwatch.StartNew( );
            using HttpResponseMessage response = await client.SendAsync( request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token );
            _ = response.EnsureSuccessStatusCode( );

            string? contentType = response.Content.Headers.ContentType?.MediaType;

            // 5. Ensure destination directory exists
            string? dir = Path.GetDirectoryName( fullDest );
            if (!string.IsNullOrEmpty( dir ) && !Directory.Exists( dir )) {
                _ = Directory.CreateDirectory( dir );
            }

            // 6. Stream to file
            long bytesWritten = 0;
            await using (Stream responseStream = await response.Content.ReadAsStreamAsync( timeoutCts.Token )) {
                await using FileStream fs = new( fullDest, FileMode.Create, FileAccess.Write, FileShare.None );
                byte[] buffer = new byte[81_920];
                int bytesRead;
                while ((bytesRead = await responseStream.ReadAsync( buffer, timeoutCts.Token )) > 0) {
                    await fs.WriteAsync( buffer.AsMemory( 0, bytesRead ), timeoutCts.Token );
                    bytesWritten += bytesRead;
                }
            }

            sw.Stop( );
            long elapsedMs = sw.ElapsedMilliseconds;

            await output.WriteAsync(
                OperatorOutput.Create(
                    LogLevel.Information,
                    $"DownloadFile: downloaded {bytesWritten:N0} bytes to '{fullDest}' ({elapsedMs}ms)" ),
                cancellationToken );

            string outputJson = JsonSerializer.Serialize( new {
                path = fullDest,
                size = bytesWritten,
                contentType = contentType ?? "unknown",
                elapsedMs,
            }, ActionJson.SerializerOptions );

            return new ActionOperatorResult( Success: true, OutputVariableValue: outputJson );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogError( ex, "DownloadFile action failed" );
            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Error, $"DownloadFile failed: {ex.Message}" ),
                cancellationToken );
            return new ActionOperatorResult( Success: false, Exception: ex );
        }
    }
}
