using System.Text.Json;
using System.Threading.Channels;

using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Core.Security;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>DeleteFile</c> action — deletes a file or directory.
/// Supports recursive deletion and forced removal of read-only files.
/// </summary>
public sealed class DeleteFileHandler : IActionHandler {

    private readonly IFilePathResolver _resolver;
    private readonly ILogger<DeleteFileHandler> _logger;

    /// <summary>Creates a new <see cref="DeleteFileHandler"/>.</summary>
    public DeleteFileHandler( IFilePathResolver resolver, ILogger<DeleteFileHandler> logger ) {
        _resolver = resolver;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Action => "DeleteFile";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        CancellationToken cancellationToken ) {
        try {
            DeleteFileParameters p = parameters.Deserialize<DeleteFileParameters>( ActionJson.SerializerOptions )
                ?? throw new ArgumentException( "Failed to deserialize DeleteFile parameters." );

            string fullPath = _resolver.ResolveSinglePath( p.Path );

            if (p.Force) {
                RemoveReadOnlyAttribute( fullPath, p.Recursive );
            }

            if (Directory.Exists( fullPath )) {
                Directory.Delete( fullPath, p.Recursive );
                await output.WriteAsync(
                    OperatorOutput.Create( LogLevel.Information, $"Deleted directory '{fullPath}'" ),
                    cancellationToken );
            } else if (File.Exists( fullPath )) {
                File.Delete( fullPath );
                await output.WriteAsync(
                    OperatorOutput.Create( LogLevel.Information, $"Deleted file '{fullPath}'" ),
                    cancellationToken );
            } else {
                await output.WriteAsync(
                    OperatorOutput.Create( LogLevel.Warning, $"Path '{fullPath}' does not exist." ),
                    cancellationToken );
                return new ActionOperatorResult( Success: false );
            }

            return new ActionOperatorResult( Success: true );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogError( ex, "DeleteFile action failed" );
            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Error, $"DeleteFile failed: {ex.Message}" ),
                cancellationToken );
            return new ActionOperatorResult( Success: false, Exception: ex );
        }
    }

    private static void RemoveReadOnlyAttribute( string path, bool recursive ) {
        if (File.Exists( path )) {
            FileInfo info = new( path );
            File.SetAttributes( info.FullName, info.Attributes & ~FileAttributes.ReadOnly );
        } else if (Directory.Exists( path )) {
            DirectoryInfo dir = new( path );
            if (recursive) {
                foreach (FileSystemInfo fileInfo in dir.GetFileSystemInfos( "*", SearchOption.AllDirectories )) {
                    if (File.Exists( fileInfo.FullName )) {
                        File.SetAttributes( fileInfo.FullName, fileInfo.Attributes & ~FileAttributes.ReadOnly );
                    }
                }
            }
        }
    }
}
