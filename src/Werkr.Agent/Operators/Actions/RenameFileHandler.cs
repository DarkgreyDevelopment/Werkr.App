using System.Text.Json;
using System.Threading.Channels;
using Werkr.Common.Attributes;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Core.Security;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>RenameFile</c> action - renames a file or directory in place.
/// </summary>
/// <remarks>Creates a new <see cref="RenameFileHandler"/>.</remarks>
[ActionCategory( "File" )]
public sealed partial class RenameFileHandler( IFilePathResolver resolver, ILogger<RenameFileHandler> logger ) : IActionHandler {

    /// <summary>
    /// Resolves and validates file paths against the agent's allowed-path allowlist.
    /// </summary>
    private readonly IFilePathResolver _resolver = resolver;
    /// <summary>
    /// Logger for recording execution errors for this handler.
    /// </summary>
    private readonly ILogger<RenameFileHandler> _logger = logger;

    /// <inheritdoc/>
    public string Action => "RenameFile";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        string? inputVariableValue = null,
        CancellationToken cancellationToken = default
    ) {
        try {
            RenameFileParameters p = parameters.Deserialize<RenameFileParameters>( ActionJson.SerializerOptions )
                ?? throw new ArgumentException( "Failed to deserialize RenameFile parameters." );

            string fullPath = _resolver.ResolveSinglePath( p.Path );
            string? resultPath = null;

            if (Directory.Exists( fullPath )) {
                // Rename directory
                DirectoryInfo dir = new( fullPath );
                string updatePath = dir.Parent == null
                    ? p.NewName
                    : Path.Join( dir.Parent.FullName, p.NewName );

                _ = _resolver.ResolveSinglePath( updatePath );

                if (Directory.Exists( updatePath )) {
                    await output.WriteAsync(
                        OperatorOutput.Create( LogLevel.Warning, $"Destination directory '{updatePath}' already exists." ),
                        cancellationToken );
                    return new ActionOperatorResult( Success: false );
                }

                Directory.Move( dir.FullName, updatePath );
                resultPath = updatePath;
                await output.WriteAsync(
                    OperatorOutput.Create( LogLevel.Information, $"Renamed directory '{fullPath}' → '{updatePath}'" ),
                    cancellationToken );
            } else if (File.Exists( fullPath )) {
                // Rename file
                FileInfo file = new( fullPath );
                string updatePath = file.Directory == null
                    ? p.NewName
                    : Path.Join( file.Directory.FullName, p.NewName );

                _ = _resolver.ResolveSinglePath( updatePath );

                if (File.Exists( updatePath ) && !p.Overwrite) {
                    await output.WriteAsync(
                        OperatorOutput.Create( LogLevel.Warning, $"Destination file '{updatePath}' already exists and Overwrite is false." ),
                        cancellationToken );
                    return new ActionOperatorResult( Success: false );
                }

                File.Move( file.FullName, updatePath, p.Overwrite );
                resultPath = updatePath;
                await output.WriteAsync(
                    OperatorOutput.Create( LogLevel.Information, $"Renamed file '{fullPath}' → '{updatePath}'" ),
                    cancellationToken );
            } else {
                throw new InvalidOperationException( $"Source path '{fullPath}' does not exist." );
            }

            return new ActionOperatorResult( Success: true, OutputVariableValue: resultPath is not null ? JsonSerializer.Serialize( resultPath, ActionJson.SerializerOptions ) : null );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            LogActionFailed( _logger, ex );
            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Error, $"RenameFile failed: {ex.Message}" ),
                cancellationToken );
            return new ActionOperatorResult( Success: false, Exception: ex );
        }
    }

    [LoggerMessage( Level = LogLevel.Error, Message = "Action failed" )]
    private static partial void LogActionFailed( ILogger logger, Exception ex );
}
