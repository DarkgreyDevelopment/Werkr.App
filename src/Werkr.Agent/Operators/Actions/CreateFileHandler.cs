using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Core.Security;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>CreateFile</c> action - creates a new file with optional content.
/// Optionally creates parent directories.
/// </summary>
public sealed class CreateFileHandler : IActionHandler {

    /// <summary>
    /// Resolves and validates file paths against the agent's allowed-path allowlist.
    /// </summary>
    private readonly IFilePathResolver _resolver;
    /// <summary>
    /// Logger for recording execution errors for this handler.
    /// </summary>
    private readonly ILogger<CreateFileHandler> _logger;

    /// <summary>Creates a new <see cref="CreateFileHandler"/>.</summary>
    public CreateFileHandler(
        IFilePathResolver resolver,
        ILogger<CreateFileHandler> logger
    ) {
        _resolver = resolver;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Action => "CreateFile";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        string? inputVariableValue = null,
        CancellationToken cancellationToken = default
    ) {
        try {
            CreateFileParameters p = parameters
                .Deserialize<CreateFileParameters>(
                    ActionJson.SerializerOptions
                )
                ?? throw new ArgumentException(
                    "Failed to deserialize CreateFile parameters."
                );

            string fullPath = _resolver.ResolveSinglePath( p.Path );

            FileInfo fileInfo = new( fullPath );
            string parentDir = fileInfo.DirectoryName
                ?? throw new InvalidOperationException( "Path must be rooted under a directory." );

            if (!Directory.Exists( parentDir )) {
                if (p.CreateParentDirectories) {
                    _ = Directory.CreateDirectory( parentDir );
                    await output.WriteAsync(
                        OperatorOutput.Create(
                            LogLevel.Information,
                            $"Created parent directory '{parentDir}'"
                        ),
                        cancellationToken
                    );
                } else {
                    throw new DirectoryNotFoundException( $"Parent directory '{parentDir}' does not exist." );
                }
            }

            if (File.Exists( fullPath ) || Directory.Exists( fullPath )) {
                if (!p.Overwrite) {
                    await output.WriteAsync(
                        OperatorOutput.Create(
                            LogLevel.Warning,
                            $"Path '{fullPath}' already exists and Overwrite is false."
                        ),
                        cancellationToken
                    );
                    return new ActionOperatorResult( Success: false );
                }
            }

            Encoding encoding = Encoding.GetEncoding( p.Encoding );

            if (p.Content != null) {
                await File.WriteAllTextAsync(
                    fullPath,
                    p.Content,
                    encoding,
                    cancellationToken
                );
            } else {
                await using FileStream fs = File.Create( fullPath );
            }

            await output.WriteAsync(
                OperatorOutput.Create(
                    LogLevel.Information,
                    $"Created file '{fullPath}'"
                ),
                cancellationToken
            );

            return new ActionOperatorResult( Success: true, OutputVariableValue: JsonSerializer.Serialize( fullPath, ActionJson.SerializerOptions ) );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogError(
                ex,
                "CreateFile action failed"
            );
            await output.WriteAsync(
                OperatorOutput.Create(
                    LogLevel.Error,
                    $"CreateFile failed: {ex.Message}"
                ),
                cancellationToken
            );
            return new ActionOperatorResult(
                Success: false,
                Exception: ex
            );
        }
    }
}
