using System.Text.Json;
using System.Threading.Channels;
using Werkr.Common.Attributes;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Core.Security;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>CreateDirectory</c> action - creates a new directory (including parent directories).
/// </summary>
/// <remarks>Creates a new <see cref="CreateDirectoryHandler"/>.</remarks>
[ActionCategory( "Directory" )]
public sealed partial class CreateDirectoryHandler(
    IFilePathResolver resolver,
    ILogger<CreateDirectoryHandler> logger
    ) : IActionHandler {

    /// <summary>
    /// Resolves and validates file paths against the agent's allowed-path allowlist.
    /// </summary>
    private readonly IFilePathResolver _resolver = resolver;
    /// <summary>
    /// Logger for recording execution errors for this handler.
    /// </summary>
    private readonly ILogger<CreateDirectoryHandler> _logger = logger;

    /// <inheritdoc/>
    public string Action => "CreateDirectory";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        string? inputVariableValue = null,
        CancellationToken cancellationToken = default
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
                return new ActionOperatorResult( Success: true, OutputVariableValue: JsonSerializer.Serialize( fullPath, ActionJson.SerializerOptions ) );
            }

            _ = Directory.CreateDirectory( fullPath );

            await output.WriteAsync(
                OperatorOutput.Create(
                    LogLevel.Information,
                    $"Created directory '{fullPath}'"
                ),
                cancellationToken
            );

            return new ActionOperatorResult( Success: true, OutputVariableValue: JsonSerializer.Serialize( fullPath, ActionJson.SerializerOptions ) );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            LogActionFailed( _logger, ex );
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

    [LoggerMessage( Level = LogLevel.Error, Message = "Action failed" )]
    private static partial void LogActionFailed( ILogger logger, Exception ex );
}
