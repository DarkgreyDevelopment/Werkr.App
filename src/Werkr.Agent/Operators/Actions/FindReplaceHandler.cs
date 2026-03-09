using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Core.Security;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>FindReplace</c> action - performs string or regex find-and-replace
/// within a file. Includes ReDoS protection via a 30-second regex match timeout.
/// </summary>
public sealed class FindReplaceHandler : IActionHandler {

    /// <summary>Maximum time a regex match is allowed to execute.</summary>
    private static readonly TimeSpan s_regexTimeout = TimeSpan.FromSeconds( 30 );

    /// <summary>
    /// Resolves and validates file paths against the agent's allowed-path allowlist.
    /// </summary>
    private readonly IFilePathResolver _resolver;
    /// <summary>
    /// Logger for recording execution errors for this handler.
    /// </summary>
    private readonly ILogger<FindReplaceHandler> _logger;

    /// <summary>Creates a new <see cref="FindReplaceHandler"/>.</summary>
    public FindReplaceHandler( IFilePathResolver resolver, ILogger<FindReplaceHandler> logger ) {
        _resolver = resolver;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Action => "FindReplace";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        CancellationToken cancellationToken
    ) {
        try {
            FindReplaceParameters p = parameters.Deserialize<FindReplaceParameters>( ActionJson.SerializerOptions )
                ?? throw new ArgumentException( "Failed to deserialize FindReplace parameters." );

            string fullPath = _resolver.ResolveSinglePath( p.Path );

            if (!File.Exists( fullPath )) {
                throw new FileNotFoundException( $"File not found: '{fullPath}'" );
            }

            Encoding encoding = Encoding.GetEncoding( p.Encoding );
            string content = await File.ReadAllTextAsync( fullPath, encoding, cancellationToken );

            string result;
            int count;

            if (p.IsRegex) {
                RegexOptions options = RegexOptions.Compiled;
                if (!p.CaseSensitive) {
                    options |= RegexOptions.IgnoreCase;
                }

                Regex regex = new( p.Find, options, s_regexTimeout );
                count = regex.Matches( content ).Count;
                result = regex.Replace( content, p.Replace );
            } else {
                StringComparison comparison = p.CaseSensitive
                    ? StringComparison.Ordinal
                    : StringComparison.OrdinalIgnoreCase;

                count = CountOccurrences( content, p.Find, comparison );
                result = content.Replace( p.Find, p.Replace, comparison );
            }

            await File.WriteAllTextAsync( fullPath, result, encoding, cancellationToken );

            await output.WriteAsync(
                OperatorOutput.Create(
                    LogLevel.Information,
                    $"FindReplace: {count} replacement(s) made in '{fullPath}'" ),
                cancellationToken );

            return new ActionOperatorResult( Success: true );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogError( ex, "FindReplace action failed" );
            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Error, $"FindReplace failed: {ex.Message}" ),
                cancellationToken );
            return new ActionOperatorResult( Success: false, Exception: ex );
        }
    }

    /// <summary>
    /// Counts non-overlapping occurrences of <paramref name="find"/> in <paramref name="source"/>.
    /// </summary>
    private static int CountOccurrences( string source, string find, StringComparison comparison ) {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf( find, index, comparison )) >= 0) {
            count++;
            index += find.Length;
        }
        return count;
    }
}
