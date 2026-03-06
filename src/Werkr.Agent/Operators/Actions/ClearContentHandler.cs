using System.Text.Json;
using System.Threading.Channels;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Core.Security;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>ClearContent</c> action - clears the content of a file (truncates to zero bytes).
/// </summary>
public sealed class ClearContentHandler : IActionHandler {

    /// <summary>
    /// Resolves and validates file paths against the agent's allowed-path allowlist.
    /// </summary>
    private readonly IFilePathResolver _resolver;
    /// <summary>
    /// Logger for recording execution errors for this handler.
    /// </summary>
    private readonly ILogger<ClearContentHandler> _logger;

    /// <summary>Creates a new <see cref="ClearContentHandler"/>.</summary>
    public ClearContentHandler( IFilePathResolver resolver, ILogger<ClearContentHandler> logger ) {
        _resolver = resolver;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Action => "ClearContent";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        CancellationToken cancellationToken
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

            return new ActionOperatorResult( Success: true );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogError( ex, "ClearContent action failed" );
            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Error, $"ClearContent failed: {ex.Message}" ),
                cancellationToken );
            return new ActionOperatorResult( Success: false, Exception: ex );
        }
    }
}
