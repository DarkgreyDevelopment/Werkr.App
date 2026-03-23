using System.Text.Json;
using System.Threading.Channels;
using Werkr.Common.Attributes;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Core.Security;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>ClearContent</c> action - clears the content of a file (truncates to zero bytes).
/// </summary>
/// <remarks>Creates a new <see cref="ClearContentHandler"/>.</remarks>
[ActionCategory( "File" )]
public sealed partial class ClearContentHandler( IFilePathResolver resolver, ILogger<ClearContentHandler> logger ) : IActionHandler {

    /// <summary>
    /// Resolves and validates file paths against the agent's allowed-path allowlist.
    /// </summary>
    private readonly IFilePathResolver _resolver = resolver;
    /// <summary>
    /// Logger for recording execution errors for this handler.
    /// </summary>
    private readonly ILogger<ClearContentHandler> _logger = logger;

    /// <inheritdoc/>
    public string Action => "ClearContent";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        string? inputVariableValue = null,
        CancellationToken cancellationToken = default
    ) {
        try {
            ClearContentParameters p = parameters.Deserialize<ClearContentParameters>( ActionJson.SerializerOptions )
                ?? throw new ArgumentException( "Failed to deserialize ClearContent parameters." );

            string fullPath = _resolver.ResolveSinglePath( p.Path );

            if (!File.Exists( fullPath )) {
                throw new FileNotFoundException( $"File '{fullPath}' does not exist.", fullPath );
            }

            await File.WriteAllTextAsync( fullPath, string.Empty, cancellationToken );

            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Information, $"Cleared content of '{fullPath}'" ),
                cancellationToken );

            return new ActionOperatorResult( Success: true, OutputVariableValue: JsonSerializer.Serialize( fullPath, ActionJson.SerializerOptions ) );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            LogActionFailed( _logger, ex );
            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Error, $"ClearContent failed: {ex.Message}" ),
                cancellationToken );
            return new ActionOperatorResult( Success: false, Exception: ex );
        }
    }

    [LoggerMessage( Level = LogLevel.Error, Message = "Action failed" )]
    private static partial void LogActionFailed( ILogger logger, Exception ex );
}
