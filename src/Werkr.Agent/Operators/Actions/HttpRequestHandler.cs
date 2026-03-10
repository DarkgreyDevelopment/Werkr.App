using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Werkr.Common.Configuration;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Core.Security;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>HttpRequest</c> action — sends an HTTP request and captures the
/// response status, headers, and body. Supports all HTTP methods, custom headers,
/// request body from parameter or workflow variable, and optional file output.
/// </summary>
public sealed class HttpRequestHandler : IActionHandler {

    /// <summary>Maximum number of redirects to follow when <c>FollowRedirects</c> is true.</summary>
    private const int MaxRedirects = 10;

    private readonly IUrlValidator _urlValidator;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFilePathResolver _resolver;
    private readonly IOptions<WorkflowVariableOptions> _variableOptions;
    private readonly ILogger<HttpRequestHandler> _logger;

    /// <summary>Creates a new <see cref="HttpRequestHandler"/>.</summary>
    public HttpRequestHandler(
        IUrlValidator urlValidator,
        IHttpClientFactory httpClientFactory,
        IFilePathResolver resolver,
        IOptions<WorkflowVariableOptions> variableOptions,
        ILogger<HttpRequestHandler> logger
    ) {
        _urlValidator = urlValidator;
        _httpClientFactory = httpClientFactory;
        _resolver = resolver;
        _variableOptions = variableOptions;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Action => "HttpRequest";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        string? inputVariableValue = null,
        CancellationToken cancellationToken = default
    ) {
        try {
            HttpRequestParameters p = parameters.Deserialize<HttpRequestParameters>( ActionJson.SerializerOptions )
                ?? throw new ArgumentException( "Failed to deserialize HttpRequest parameters." );

            // 1. Validate URL (includes EnableNetworkActions gate + SSRF protection)
            Uri uri = _urlValidator.ValidateUrl( p.Url );

            // 2. Validate OutputFilePath if set
            string? resolvedOutputPath = null;
            if (!string.IsNullOrWhiteSpace( p.OutputFilePath )) {
                resolvedOutputPath = _resolver.ResolveSinglePath( p.OutputFilePath );
            }

            // 3. Build request
            HttpMethod method = new( p.Method );
            using HttpRequestMessage request = new( method, uri );

            // Custom headers
            if (p.Headers is not null) {
                foreach (KeyValuePair<string, string> header in p.Headers) {
                    _ = request.Headers.TryAddWithoutValidation( header.Key, header.Value );
                }
            }

            // Body: parameter takes precedence over inputVariableValue
            string? body = !string.IsNullOrEmpty( p.Body ) ? p.Body : inputVariableValue;
            if (body is not null) {
                string contentType = p.ContentType ?? "application/json";
                request.Content = new StringContent( body, Encoding.UTF8, contentType );
            }

            // 4. Send request (AllowAutoRedirect is disabled at handler level)
            HttpClient client = _httpClientFactory.CreateClient( "WerkrActions" );

            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
            timeoutCts.CancelAfter( TimeSpan.FromSeconds( p.TimeoutSeconds ) );

            Stopwatch sw = Stopwatch.StartNew( );
            using HttpResponseMessage response = await SendWithRedirectPolicy(
                client, request, p.FollowRedirects, timeoutCts.Token );
            sw.Stop( );

            int statusCode = (int)response.StatusCode;
            string reasonPhrase = response.ReasonPhrase ?? response.StatusCode.ToString( );
            long elapsedMs = sw.ElapsedMilliseconds;

            // 5. Check expected status codes
            if (p.ExpectedStatusCodes.Length > 0 && !p.ExpectedStatusCodes.Contains( statusCode )) {
                string expected = string.Join( ", ", p.ExpectedStatusCodes );
                throw new HttpRequestException(
                    $"HttpRequest received status {statusCode} ({reasonPhrase}), expected one of: [{expected}]." );
            }

            // 6. Collect response headers
            Dictionary<string, string> responseHeaders = new( StringComparer.OrdinalIgnoreCase );
            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers) {
                responseHeaders[header.Key] = string.Join( ", ", header.Value );
            }
            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Content.Headers) {
                responseHeaders[header.Key] = string.Join( ", ", header.Value );
            }

            long? contentLength = response.Content.Headers.ContentLength;

            // 7. Handle response body
            string outputJson;
            if (resolvedOutputPath is not null) {
                // Stream to file
                string? dir = Path.GetDirectoryName( resolvedOutputPath );
                if (!string.IsNullOrEmpty( dir ) && !Directory.Exists( dir )) {
                    _ = Directory.CreateDirectory( dir );
                }
                await using (FileStream fs = new( resolvedOutputPath, FileMode.Create, FileAccess.Write, FileShare.None )) {
                    await response.Content.CopyToAsync( fs, timeoutCts.Token );
                }
                outputJson = JsonSerializer.Serialize( new {
                    statusCode,
                    reasonPhrase,
                    headers = responseHeaders,
                    bodyFilePath = resolvedOutputPath,
                    contentLength,
                    elapsedMs,
                }, ActionJson.SerializerOptions );
            } else {
                // Read body to string with bounded buffer to prevent OOM
                int maxBodyChars = _variableOptions.Value.MaxValueSizeBytes;
                (string responseBody, bool bodyTruncated) = await ReadBoundedBodyAsync(
                    response, maxBodyChars, timeoutCts.Token );

                if (bodyTruncated) {
                    outputJson = JsonSerializer.Serialize( new {
                        statusCode,
                        reasonPhrase,
                        headers = responseHeaders,
                        body = responseBody,
                        bodyTruncated,
                        contentLength,
                        elapsedMs,
                    }, ActionJson.SerializerOptions );
                } else {
                    outputJson = JsonSerializer.Serialize( new {
                        statusCode,
                        reasonPhrase,
                        headers = responseHeaders,
                        body = responseBody,
                        contentLength,
                        elapsedMs,
                    }, ActionJson.SerializerOptions );
                }
            }

            await output.WriteAsync(
                OperatorOutput.Create(
                    LogLevel.Information,
                    $"HttpRequest: {p.Method} {p.Url} → {statusCode} {reasonPhrase} ({elapsedMs}ms)" ),
                cancellationToken );

            return new ActionOperatorResult( Success: true, OutputVariableValue: outputJson );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogError( ex, "HttpRequest action failed" );
            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Error, $"HttpRequest failed: {ex.Message}" ),
                cancellationToken );
            return new ActionOperatorResult( Success: false, Exception: ex );
        }
    }

    /// <summary>
    /// Sends an HTTP request with or without redirect following.
    /// When <paramref name="followRedirects"/> is <see langword="true"/>, follows up to
    /// <see cref="MaxRedirects"/> hops, re-validating each redirect target through
    /// <see cref="IUrlValidator"/> for SSRF protection.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRedirectPolicy(
        HttpClient client,
        HttpRequestMessage request,
        bool followRedirects,
        CancellationToken cancellationToken
    ) {
        HttpResponseMessage response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken );

        if (!followRedirects) {
            return response;
        }

        Uri currentUri = request.RequestUri!;
        int redirectCount = 0;
        while (IsRedirectStatusCode( response.StatusCode )
            && response.Headers.Location is not null
            && redirectCount < MaxRedirects) {
            Uri redirectUri = response.Headers.Location.IsAbsoluteUri
                ? response.Headers.Location
                : new Uri( currentUri, response.Headers.Location );

            // Dispose the intermediate response before validating (avoids leak if validation throws)
            response.Dispose( );

            // Re-validate the redirect target for SSRF protection
            _ = _urlValidator.ValidateUrl( redirectUri.AbsoluteUri );

            using HttpRequestMessage redirectRequest = new( HttpMethod.Get, redirectUri );
            response = await client.SendAsync(
                redirectRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken );
            currentUri = redirectUri;
            redirectCount++;
        }

        return response;
    }

    /// <summary>
    /// Reads the response body into a bounded string buffer (up to <paramref name="maxChars"/> chars).
    /// Returns the body text and whether it was truncated.
    /// </summary>
    private static async Task<(string Body, bool Truncated)> ReadBoundedBodyAsync(
        HttpResponseMessage response,
        int maxChars,
        CancellationToken cancellationToken
    ) {
        Stream stream = await response.Content.ReadAsStreamAsync( cancellationToken );
        await using (stream.ConfigureAwait( false )) {
            using StreamReader reader = new( stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true );
            char[] buffer = new char[maxChars + 1];
            int totalRead = 0;
            int charsRead;
            while (totalRead <= maxChars
                && (charsRead = await reader.ReadAsync( buffer.AsMemory( totalRead, buffer.Length - totalRead ), cancellationToken )) > 0) {
                totalRead += charsRead;
            }

            if (totalRead > maxChars) {
                return (new string( buffer, 0, maxChars ), true);
            }
            return (new string( buffer, 0, totalRead ), false);
        }
    }

    /// <summary>Returns <see langword="true"/> for 3xx redirect status codes.</summary>
    private static bool IsRedirectStatusCode( HttpStatusCode code ) =>
        code is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
}
