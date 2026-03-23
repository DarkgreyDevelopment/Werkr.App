using System.Text.Json;
using System.Threading.Channels;
using Werkr.Common.Attributes;
using Werkr.Common.Models;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Core.Security;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>ListDirectory</c> action - enumerates files and/or directories
/// matching a pattern and writes results as a JSON array to the output channel.
/// </summary>
/// <remarks>Creates a new <see cref="ListDirectoryHandler"/>.</remarks>
[ActionCategory( "Directory" )]
public sealed partial class ListDirectoryHandler( IFilePathResolver resolver, ILogger<ListDirectoryHandler> logger ) : IActionHandler {

    /// <summary>
    /// Resolves and validates file paths against the agent's allowed-path allowlist.
    /// </summary>
    private readonly IFilePathResolver _resolver = resolver;
    /// <summary>
    /// Logger for recording execution errors for this handler.
    /// </summary>
    private readonly ILogger<ListDirectoryHandler> _logger = logger;

    /// <inheritdoc/>
    public string Action => "ListDirectory";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        string? inputVariableValue = null,
        CancellationToken cancellationToken = default
    ) {
        try {
            ListDirectoryParameters p = parameters.Deserialize<ListDirectoryParameters>( ActionJson.SerializerOptions )
                ?? throw new ArgumentException( "Failed to deserialize ListDirectory parameters." );

            string fullPath = _resolver.ResolveSinglePath( p.Path );

            if (!Directory.Exists( fullPath )) {
                throw new DirectoryNotFoundException( $"Directory not found: '{fullPath}'" );
            }

            SearchOption searchOption = p.Recursive
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            IEnumerable<string> entries = p.Type switch {
                PathType.File => Directory.EnumerateFiles( fullPath, p.Pattern, searchOption ),
                PathType.Directory => Directory.EnumerateDirectories( fullPath, p.Pattern, searchOption ),
                PathType.Any => Directory.EnumerateFileSystemEntries( fullPath, p.Pattern, searchOption ),
                _ => throw new ArgumentOutOfRangeException( nameof( parameters ), p.Type, "Unknown PathType value." )
            };

            List<string> results = [.. entries];

            results = p.SortBy switch {
                DirectoryListSortBy.Name => [.. results.OrderBy( Path.GetFileName )],
                DirectoryListSortBy.Modified => [.. results.OrderBy( File.GetLastWriteTimeUtc )],
                DirectoryListSortBy.Size => [.. results.OrderBy( e =>
                    File.Exists( e ) ? new FileInfo( e ).Length : 0L )],
                DirectoryListSortBy.None => results,
                _ => results
            };

            string json = JsonSerializer.Serialize( results, ActionJson.SerializerOptions );

            await output.WriteAsync(
                OperatorOutput.Create(
                    LogLevel.Information,
                    $"ListDirectory: found {results.Count} entries in '{fullPath}'" ),
                cancellationToken );

            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Information, json ),
                cancellationToken );

            return new ActionOperatorResult( Success: true, OutputVariableValue: json );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            LogActionFailed( _logger, ex );
            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Error, $"ListDirectory failed: {ex.Message}" ),
                cancellationToken );
            return new ActionOperatorResult( Success: false, Exception: ex );
        }
    }

    [LoggerMessage( Level = LogLevel.Error, Message = "Action failed" )]
    private static partial void LogActionFailed( ILogger logger, Exception ex );
}
