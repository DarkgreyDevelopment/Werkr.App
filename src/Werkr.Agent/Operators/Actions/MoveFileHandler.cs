using System.Text.Json;
using System.Threading.Channels;
using Werkr.Common.Attributes;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Core.Security;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>MoveFile</c> action - moves files or directories from source to destination.
/// Supports wildcard file resolution. Directory move is implemented as copy + delete.
/// </summary>
/// <remarks>Creates a new <see cref="MoveFileHandler"/>.</remarks>
[ActionCategory( "File" )]
public sealed partial class MoveFileHandler(
    IFilePathResolver resolver,
    ILogger<MoveFileHandler> logger
    ) : IActionHandler {

    /// <summary>
    /// Resolves and validates file paths against the agent's allowed-path allowlist.
    /// </summary>
    private readonly IFilePathResolver _resolver = resolver;
    /// <summary>
    /// Logger for recording execution errors for this handler.
    /// </summary>
    private readonly ILogger<MoveFileHandler> _logger = logger;

    /// <inheritdoc/>
    public string Action => "MoveFile";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        string? inputVariableValue = null,
        CancellationToken cancellationToken = default
    ) {
        try {
            MoveFileParameters p = parameters
                .Deserialize<MoveFileParameters>(
                    ActionJson.SerializerOptions
                )
                ?? throw new ArgumentException(
                    "Failed to deserialize MoveFile parameters."
                );

            _resolver.ValidateSourceDestination(
                p.Source,
                p.Destination
            );

            string source = Path.GetFullPath( p.Source );
            string destination = _resolver.ResolveSinglePath( p.Destination );

            if (Directory.Exists( source )) {
                // Directory move (copy + delete)
                CopyDirectoryRecursive(
                    source,
                    destination,
                    p.Overwrite
                );
                Directory.Delete(
                    source,
                    recursive: true
                );
                await output.WriteAsync(
                    OperatorOutput.Create(
                        LogLevel.Information,
                        $"Moved directory '{source}' → '{destination}'"
                    ),
                    cancellationToken
                );
            } else {
                // File move (supports wildcards)
                string[] files = _resolver.ResolveFiles( source );
                if (files.Length == 0) {
                    await output.WriteAsync(
                        OperatorOutput.Create(
                            LogLevel.Warning,
                            $"No files found matching '{source}'."
                        ),
                        cancellationToken
                    );
                    return new ActionOperatorResult( Success: false );
                }

                foreach (string file in files) {
                    cancellationToken.ThrowIfCancellationRequested( );
                    string dest = Directory.Exists( destination )
                        ? Path.Join(
                            destination,
                            Path.GetFileName( file )
                        )
                        : destination;
                    File.Move(
                        file,
                        dest,
                        p.Overwrite
                    );
                    await output.WriteAsync(
                        OperatorOutput.Create(
                            LogLevel.Information,
                            $"Moved '{file}' → '{dest}'"
                        ),
                        cancellationToken
                    );
                }
            }

            return new ActionOperatorResult( Success: true, OutputVariableValue: JsonSerializer.Serialize( destination, ActionJson.SerializerOptions ) );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            LogActionFailed( _logger, ex );
            await output.WriteAsync(
                OperatorOutput.Create(
                    LogLevel.Error,
                    $"MoveFile failed: {ex.Message}"
                ),
                cancellationToken
            );
            return new ActionOperatorResult(
                Success: false,
                Exception: ex
            );
        }
    }

    /// <summary>
    /// Recursively copies a directory tree from source to
    /// destination, used as part of the directory move
    /// operation (copy + delete source).
    /// </summary>
    private static void CopyDirectoryRecursive(
        string source,
        string destination,
        bool overwrite
    ) {
        DirectoryInfo dir = new( source );
        if (!dir.Exists) { return; }

        _ = Directory.CreateDirectory( destination );

        foreach (FileInfo file in dir.GetFiles( )) {
            string targetPath = Path.Join(
                destination,
                file.Name
            );
            _ = file.CopyTo(
                targetPath,
                overwrite
            );
        }

        foreach (DirectoryInfo subDir in dir.GetDirectories( )) {
            string newDest = Path.Join(
                destination,
                subDir.Name
            );
            CopyDirectoryRecursive(
                subDir.FullName,
                newDest,
                overwrite
            );
        }
    }

    [LoggerMessage( Level = LogLevel.Error, Message = "Action failed" )]
    private static partial void LogActionFailed( ILogger logger, Exception ex );
}
