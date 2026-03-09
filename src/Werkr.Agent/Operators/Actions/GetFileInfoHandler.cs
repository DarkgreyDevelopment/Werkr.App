using System.Text.Json;
using System.Threading.Channels;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Core.Security;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>GetFileInfo</c> action - returns file or directory metadata
/// (exists, size, created, modified, isDirectory) as structured JSON output.
/// </summary>
public sealed class GetFileInfoHandler : IActionHandler {

    /// <summary>
    /// Resolves and validates file paths against the agent's allowed-path allowlist.
    /// </summary>
    private readonly IFilePathResolver _resolver;
    /// <summary>
    /// Logger for recording execution errors for this handler.
    /// </summary>
    private readonly ILogger<GetFileInfoHandler> _logger;

    /// <summary>Creates a new <see cref="GetFileInfoHandler"/>.</summary>
    public GetFileInfoHandler( IFilePathResolver resolver, ILogger<GetFileInfoHandler> logger ) {
        _resolver = resolver;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Action => "GetFileInfo";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        CancellationToken cancellationToken
    ) {
        try {
            GetFileInfoParameters p = parameters.Deserialize<GetFileInfoParameters>( ActionJson.SerializerOptions )
                ?? throw new ArgumentException( "Failed to deserialize GetFileInfo parameters." );

            string fullPath = _resolver.ResolveSinglePath( p.Path );

            bool isDirectory = Directory.Exists( fullPath );
            bool isFile = File.Exists( fullPath );
            bool exists = isDirectory || isFile;

            object info;
            if (isFile) {
                FileInfo fi = new( fullPath );
                info = new {
                    path = fullPath,
                    exists = true,
                    isDirectory = false,
                    size = fi.Length,
                    createdUtc = fi.CreationTimeUtc,
                    modifiedUtc = fi.LastWriteTimeUtc,
                };
            } else if (isDirectory) {
                DirectoryInfo di = new( fullPath );
                info = new {
                    path = fullPath,
                    exists = true,
                    isDirectory = true,
                    size = (long?)null,
                    createdUtc = di.CreationTimeUtc,
                    modifiedUtc = di.LastWriteTimeUtc,
                };
            } else {
                info = new {
                    path = fullPath,
                    exists = false,
                    isDirectory = (bool?)null,
                    size = (long?)null,
                    createdUtc = (DateTime?)null,
                    modifiedUtc = (DateTime?)null,
                };
            }

            string json = JsonSerializer.Serialize( info, ActionJson.SerializerOptions );

            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Information, json ),
                cancellationToken );

            return new ActionOperatorResult( Success: true );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogError( ex, "GetFileInfo action failed" );
            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Error, $"GetFileInfo failed: {ex.Message}" ),
                cancellationToken );
            return new ActionOperatorResult( Success: false, Exception: ex );
        }
    }
}
