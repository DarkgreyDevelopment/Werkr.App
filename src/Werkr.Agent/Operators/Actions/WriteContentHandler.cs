using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Werkr.Common.Attributes;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Core.Security;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>WriteContent</c> action - writes or appends text content to a file.
/// </summary>
/// <remarks>Creates a new <see cref="WriteContentHandler"/>.</remarks>
[ActionCategory( "File" )]
public sealed partial class WriteContentHandler(
    IFilePathResolver resolver,
    ILogger<WriteContentHandler> logger
    ) : IActionHandler {

    /// <summary>
    /// Resolves and validates file paths against the agent's allowed-path allowlist.
    /// </summary>
    private readonly IFilePathResolver _resolver = resolver;
    /// <summary>
    /// Logger for recording execution errors for this handler.
    /// </summary>
    private readonly ILogger<WriteContentHandler> _logger = logger;

    /// <inheritdoc/>
    public string Action => "WriteContent";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        string? inputVariableValue = null,
        CancellationToken cancellationToken = default
    ) {
        try {
            WriteContentParameters p = parameters.Deserialize<WriteContentParameters>( ActionJson.SerializerOptions )
                ?? throw new ArgumentException( "Failed to deserialize WriteContent parameters." );

            string fullPath = _resolver.ResolveSinglePath( p.Path );

            Encoding encoding = Encoding.GetEncoding( p.Encoding );

            if (p.Append) {
                await File.AppendAllTextAsync( fullPath, p.Content, encoding, cancellationToken );
                await output.WriteAsync(
                    OperatorOutput.Create( LogLevel.Information, $"Appended content to '{fullPath}'" ),
                    cancellationToken );
            } else {
                await File.WriteAllTextAsync( fullPath, p.Content, encoding, cancellationToken );
                await output.WriteAsync(
                    OperatorOutput.Create( LogLevel.Information, $"Wrote content to '{fullPath}'" ),
                    cancellationToken );
            }

            return new ActionOperatorResult( Success: true, OutputVariableValue: JsonSerializer.Serialize( fullPath, ActionJson.SerializerOptions ) );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            LogActionFailed( _logger, ex );
            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Error, $"WriteContent failed: {ex.Message}" ),
                cancellationToken );
            return new ActionOperatorResult( Success: false, Exception: ex );
        }
    }

    [LoggerMessage( Level = LogLevel.Error, Message = "Action failed" )]
    private static partial void LogActionFailed( ILogger logger, Exception ex );
}
