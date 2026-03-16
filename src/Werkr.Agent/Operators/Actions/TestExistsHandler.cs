using System.Text.Json;
using System.Threading.Channels;
using Werkr.Common.Models;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Core.Security;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>TestExists</c> action - tests whether a file or directory exists.
/// Uses <see cref="PathType"/> to discriminate between file, directory, or any.
/// </summary>
/// <remarks>Creates a new <see cref="TestExistsHandler"/>.</remarks>
public sealed partial class TestExistsHandler( IFilePathResolver resolver, ILogger<TestExistsHandler> logger ) : IActionHandler {

    /// <summary>
    /// Resolves and validates file paths against the agent's allowed-path allowlist.
    /// </summary>
    private readonly IFilePathResolver _resolver = resolver;
    /// <summary>
    /// Logger for recording execution errors for this handler.
    /// </summary>
    private readonly ILogger<TestExistsHandler> _logger = logger;

    /// <inheritdoc/>
    public string Action => "TestExists";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        string? inputVariableValue = null,
        CancellationToken cancellationToken = default
    ) {
        try {
            TestExistsParameters p = parameters.Deserialize<TestExistsParameters>( ActionJson.SerializerOptions )
                ?? throw new ArgumentException( "Failed to deserialize TestExists parameters." );

            string fullPath = _resolver.ResolveSinglePath( p.Path );

            bool exists = p.Type switch {
                PathType.File => File.Exists( fullPath ),
                PathType.Directory => Directory.Exists( fullPath ),
                PathType.Any => File.Exists( fullPath ) || Directory.Exists( fullPath ),
                _ => throw new ArgumentOutOfRangeException( nameof( parameters ), p.Type, "Unknown PathType value." )
            };

            string typeLabel = p.Type.ToString( ).ToLowerInvariant( );
            string status = exists ? "exists" : "does not exist";

            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Information, $"TestExists({typeLabel}): '{fullPath}' {status}" ),
                cancellationToken );

            // Success is true when the path exists, false when it does not.
            return new ActionOperatorResult( Success: exists, OutputVariableValue: exists ? "true" : "false" );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            LogActionFailed( _logger, ex );
            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Error, $"TestExists failed: {ex.Message}" ),
                cancellationToken );
            return new ActionOperatorResult( Success: false, Exception: ex );
        }
    }

    [LoggerMessage( Level = LogLevel.Error, Message = "Action failed" )]
    private static partial void LogActionFailed( ILogger logger, Exception ex );
}
