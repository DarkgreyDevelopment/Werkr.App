using System.Text.Json;
using System.Threading.Channels;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Core.Security;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>CreateDirectory</c> action - creates a new directory (including parent directories).
/// </summary>
public sealed class CreateDirectoryHandler : IActionHandler {

    /// <summary>
    /// Resolves and validates file paths against the agent's allowed-path allowlist.
    /// </summary>
    private readonly IFilePathResolver _resolver;
    /// <summary>
    /// Logger for recording execution errors for this handler.
    /// </summary>
    private readonly ILogger<CreateDirectoryHandler> _logger;

    /// <summary>Creates a new <see cref="CreateDirectoryHandler"/>.</summary>
    public CreateDirectoryHandler(
        IFilePathResolver resolver,
        ILogger<CreateDirectoryHandler> logger
    ) {
        _resolver = resolver;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Action => "CreateDirectory";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        CancellationToken cancellationToken
    ) {
        try {
            CreateDirectoryParameters p = parameters
                .Deserialize<CreateDirectoryParameters>(
                    ActionJson.SerializerOptions
                )
                ?? throw new ArgumentException(
                    "Failed to deserialize CreateDirectory parameters."
                );

            string fullPath = _resolver.ResolveSinglePath( p.Path );

            if (File.Exists( fullPath )) {
                throw new ArgumentException(
                    $"Path '{fullPath}' already exists as a file. Cannot create directory."
                );
            }

            if (Directory.Exists( fullPath )) {
                await output.WriteAsync(
                    OperatorOutput.Create(
                        LogLevel.Information,
                        $"Directory '{fullPath}' already exists."
                    ),
                    cancellationToken
                );
                return new ActionOperatorResult( Success: true );
            }

            _ = Directory.CreateDirectory( fullPath );

            await output.WriteAsync(
                OperatorOutput.Create(
                    LogLevel.Information,
                    $"Created directory '{fullPath}'"
                ),
                cancellationToken
            );

            return new ActionOperatorResult( Success: true );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogError(
                ex,
                "CreateDirectory action failed"
            );
            await output.WriteAsync(
                OperatorOutput.Create(
                    LogLevel.Error,
                    $"CreateDirectory failed: {ex.Message}"
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
