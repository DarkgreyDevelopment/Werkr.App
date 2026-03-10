using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Channels;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Core.Security;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>UploadFile</c> action — uploads a local file to a URL using
/// multipart/form-data.
/// </summary>
public sealed class UploadFileHandler : IActionHandler {

    private readonly IUrlValidator _urlValidator;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFilePathResolver _resolver;
    private readonly ILogger<UploadFileHandler> _logger;

    /// <summary>Creates a new <see cref="UploadFileHandler"/>.</summary>
    public UploadFileHandler(
        IUrlValidator urlValidator,
        IHttpClientFactory httpClientFactory,
        IFilePathResolver resolver,
        ILogger<UploadFileHandler> logger
    ) {
        _urlValidator = urlValidator;
        _httpClientFactory = httpClientFactory;
        _resolver = resolver;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Action => "UploadFile";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        string? inputVariableValue = null,
        CancellationToken cancellationToken = default
    ) {
        try {
            UploadFileParameters p = parameters.Deserialize<UploadFileParameters>( ActionJson.SerializerOptions )
                ?? throw new ArgumentException( "Failed to deserialize UploadFile parameters." );

            // 1. Validate URL
            Uri uri = _urlValidator.ValidateUrl( p.Url );

            // 2. Resolve and validate source file
            string fullPath = _resolver.ResolveSinglePath( p.FilePath );
            if (!File.Exists( fullPath )) {
                throw new FileNotFoundException( $"Source file not found: '{fullPath}'" );
            }

            // 3. Build multipart request
            HttpClient client = _httpClientFactory.CreateClient( "WerkrActions" );
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
            timeoutCts.CancelAfter( TimeSpan.FromSeconds( p.TimeoutSeconds ) );

            HttpMethod method = new( p.Method.ToUpperInvariant( ) );
            using HttpRequestMessage request = new( method, uri );

            await using FileStream fs = new( fullPath, FileMode.Open, FileAccess.Read, FileShare.Read );
            StreamContent fileContent = new( fs );
            fileContent.Headers.ContentType = new MediaTypeHeaderValue( "application/octet-stream" );

            MultipartFormDataContent form = new( ) {
                { fileContent, p.FormFieldName, Path.GetFileName( fullPath ) }
            };
            request.Content = form;

            if (p.Headers is not null) {
                foreach (KeyValuePair<string, string> header in p.Headers) {
                    _ = request.Headers.TryAddWithoutValidation( header.Key, header.Value );
                }
            }

            // 4. Send
            Stopwatch sw = Stopwatch.StartNew( );
            using HttpResponseMessage response = await client.SendAsync( request, timeoutCts.Token );
            string responseBody = await response.Content.ReadAsStringAsync( timeoutCts.Token );
            sw.Stop( );

            int statusCode = (int)response.StatusCode;
            long elapsedMs = sw.ElapsedMilliseconds;
            long fileSize = new FileInfo( fullPath ).Length;

            await output.WriteAsync(
                OperatorOutput.Create(
                    LogLevel.Information,
                    $"UploadFile: {method} {uri} → {statusCode} ({fileSize:N0} bytes, {elapsedMs}ms)" ),
                cancellationToken );

            string outputJson = JsonSerializer.Serialize( new {
                statusCode,
                responseBody,
                fileName = Path.GetFileName( fullPath ),
                fileSize,
                elapsedMs,
            }, ActionJson.SerializerOptions );

            return new ActionOperatorResult( Success: true, OutputVariableValue: outputJson );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogError( ex, "UploadFile action failed" );
            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Error, $"UploadFile failed: {ex.Message}" ),
                cancellationToken );
            return new ActionOperatorResult( Success: false, Exception: ex );
        }
    }
}
