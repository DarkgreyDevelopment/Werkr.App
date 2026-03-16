using System.Text.Json;
using System.Threading.Channels;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Core.Security;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>CopyFile</c> action - copies files or directories from source to destination.
/// Supports wildcard file resolution and recursive directory copy.
/// </summary>
/// <remarks>Creates a new <see cref="CopyFileHandler"/>.</remarks>
public sealed partial class CopyFileHandler(
    IFilePathResolver resolver,
    ILogger<CopyFileHandler> logger
    ) : IActionHandler {

    /// <summary>
    /// Resolves and validates file paths against the agent's allowed-path allowlist.
    /// </summary>
    private readonly IFilePathResolver _resolver = resolver;
    /// <summary>
    /// Logger for recording execution errors for this handler.
    /// </summary>
    private readonly ILogger<CopyFileHandler> _logger = logger;

    /// <inheritdoc/>
    public string Action => "CopyFile";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        string? inputVariableValue = null,
        CancellationToken cancellationToken = default
    ) {
        try {
            CopyFileParameters p = parameters.Deserialize<CopyFileParameters>( ActionJson.SerializerOptions )
                ?? throw new ArgumentException( "Failed to deserialize CopyFile parameters." );

            _resolver.ValidateSourceDestination(
                p.Source,
                p.Destination
            );

            string source = Path.GetFullPath( p.Source );
            string destination = _resolver.ResolveSinglePath( p.Destination );

            if (Directory.Exists( source )) {
                // Directory copy
                bool result = CopyDirectory( source, destination, p.Overwrite, p.Recursive );
                if (result) {
                    await output.WriteAsync(
                        OperatorOutput.Create(
                            LogLevel.Information,
                            $"Copied directory '{source}' → '{destination}'"
                        ),
                        cancellationToken
                    );
                } else {
                    await output.WriteAsync(
                        OperatorOutput.Create(
                            LogLevel.Warning,
                            $"Source directory '{source}' does not exist."
                        ),
                        cancellationToken
                    );
                    return new ActionOperatorResult( Success: false );
                }
            } else {
                // File copy (supports wildcards)
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
                    File.Copy(
                        file,
                        dest,
                        p.Overwrite
                    );
                    await output.WriteAsync(
                        OperatorOutput.Create(
                            LogLevel.Information,
                            $"Copied '{file}' → '{dest}'"
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
                    $"CopyFile failed: {ex.Message}"
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
    /// Recursively copies a directory and its contents to a new destination.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the directory was copied
    /// successfully;
    /// <see langword="false"/> if an error occurred.
    /// </returns>
    private static bool CopyDirectory(
        string source,
        string destination,
        bool overwrite,
        bool recursive
    ) {
        DirectoryInfo dir = new( source );
        if (!dir.Exists) { return false; }

        DirectoryInfo[] subDirs = dir.GetDirectories( );
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

        if (recursive) {
            foreach (DirectoryInfo subDir in subDirs) {
                string newDest = Path.Join(
                    destination,
                    subDir.Name
                );
                _ = CopyDirectory(
                    subDir.FullName,
                    newDest,
                    overwrite,
                    recursive
                );
            }
        } else {
            foreach (DirectoryInfo subDir in subDirs) {
                _ = Directory.CreateDirectory(
                    Path.Join(
                        destination,
                        subDir.Name
                    )
                );
            }
        }

        return true;
    }

    [LoggerMessage( Level = LogLevel.Error, Message = "Action failed" )]
    private static partial void LogActionFailed( ILogger logger, Exception ex );
}
