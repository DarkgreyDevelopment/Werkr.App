using System.Text.Json;
using System.Threading.Channels;
using Werkr.Common.Attributes;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Core.Security;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>GetFileInfo</c> action - returns file or directory metadata
/// (exists, size, created, modified, isDirectory) as structured JSON output.
/// </summary>
/// <remarks>Creates a new <see cref="GetFileInfoHandler"/>.</remarks>
[ActionCategory( "File" )]
public sealed partial class GetFileInfoHandler( IFilePathResolver resolver, ILogger<GetFileInfoHandler> logger ) : IActionHandler {

    /// <summary>
    /// Resolves and validates file paths against the agent's allowed-path allowlist.
    /// </summary>
    private readonly IFilePathResolver _resolver = resolver;
    /// <summary>
    /// Logger for recording execution errors for this handler.
    /// </summary>
    private readonly ILogger<GetFileInfoHandler> _logger = logger;

    /// <inheritdoc/>
    public string Action => "GetFileInfo";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        string? inputVariableValue = null,
        CancellationToken cancellationToken = default
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

            return new ActionOperatorResult( Success: true, OutputVariableValue: json );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            LogActionFailed( _logger, ex );
            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Error, $"GetFileInfo failed: {ex.Message}" ),
                cancellationToken );
            return new ActionOperatorResult( Success: false, Exception: ex );
        }
    }

    [LoggerMessage( Level = LogLevel.Error, Message = "Action failed" )]
    private static partial void LogActionFailed( ILogger logger, Exception ex );
}
