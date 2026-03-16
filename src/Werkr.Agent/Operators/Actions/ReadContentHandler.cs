using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Core.Security;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>ReadContent</c> action - reads file content and emits it as action output.
/// Supports configurable encoding and optional byte-count truncation.
/// </summary>
/// <remarks>Creates a new <see cref="ReadContentHandler"/>.</remarks>
public sealed partial class ReadContentHandler( IFilePathResolver resolver, ILogger<ReadContentHandler> logger ) : IActionHandler {

    /// <summary>
    /// Resolves and validates file paths against the agent's allowed-path allowlist.
    /// </summary>
    private readonly IFilePathResolver _resolver = resolver;
    /// <summary>
    /// Logger for recording execution errors for this handler.
    /// </summary>
    private readonly ILogger<ReadContentHandler> _logger = logger;

    /// <inheritdoc/>
    public string Action => "ReadContent";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        string? inputVariableValue = null,
        CancellationToken cancellationToken = default
    ) {
        try {
            ReadContentParameters p = parameters.Deserialize<ReadContentParameters>( ActionJson.SerializerOptions )
                ?? throw new ArgumentException( "Failed to deserialize ReadContent parameters." );

            string fullPath = _resolver.ResolveSinglePath( p.Path );

            if (!File.Exists( fullPath )) {
                throw new FileNotFoundException( $"File not found: '{fullPath}'" );
            }

            Encoding encoding = Encoding.GetEncoding( p.Encoding );
            string content;

            if (p.MaxBytes is long maxBytes && maxBytes >= 0) {
                await using FileStream fs = new( fullPath, FileMode.Open, FileAccess.Read, FileShare.Read );
                int bytesToRead = (int)Math.Min( maxBytes, fs.Length );
                byte[] buffer = new byte[bytesToRead];
                int bytesRead = await fs.ReadAsync( buffer.AsMemory( 0, bytesToRead ), cancellationToken );
                content = encoding.GetString( buffer, 0, bytesRead );
            } else {
                content = await File.ReadAllTextAsync( fullPath, encoding, cancellationToken );
            }

            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Information, content ),
                cancellationToken );

            return new ActionOperatorResult( Success: true, OutputVariableValue: JsonSerializer.Serialize( content, ActionJson.SerializerOptions ) );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            LogActionFailed( _logger, ex );
            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Error, $"ReadContent failed: {ex.Message}" ),
                cancellationToken );
            return new ActionOperatorResult( Success: false, Exception: ex );
        }
    }

    [LoggerMessage( Level = LogLevel.Error, Message = "Action failed" )]
    private static partial void LogActionFailed( ILogger logger, Exception ex );
}
