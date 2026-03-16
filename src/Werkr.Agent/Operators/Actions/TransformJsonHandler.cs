using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Core.Security;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>TransformJson</c> action — applies an ordered sequence of JSON
/// manipulation operations (Extract, Set, Delete, Merge) to an input document.
/// Supports dual-mode input: reads from a file (<see cref="TransformJsonParameters.InputPath"/>)
/// or from the workflow variable (<c>inputVariableValue</c>). File input takes precedence.
/// Uses JSON Pointer (RFC 6901) path syntax with an optional <c>$.</c> convenience prefix.
/// </summary>
/// <remarks>Creates a new <see cref="TransformJsonHandler"/>.</remarks>
public sealed partial class TransformJsonHandler( IFilePathResolver resolver, ILogger<TransformJsonHandler> logger ) : IActionHandler {

    /// <summary>
    /// Resolves and validates file paths against the agent's allowed-path allowlist.
    /// </summary>
    private readonly IFilePathResolver _resolver = resolver;
    /// <summary>
    /// Logger for recording execution errors for this handler.
    /// </summary>
    private readonly ILogger<TransformJsonHandler> _logger = logger;

    /// <inheritdoc/>
    public string Action => "TransformJson";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        string? inputVariableValue = null,
        CancellationToken cancellationToken = default
    ) {
        try {
            TransformJsonParameters p = parameters.Deserialize<TransformJsonParameters>( ActionJson.SerializerOptions )
                ?? throw new ArgumentException( "Failed to deserialize TransformJson parameters." );

            if (p.Operations is null || p.Operations.Length == 0) {
                throw new ArgumentException( "TransformJson requires at least one operation." );
            }

            // 1. Resolve input: InputPath file → inputVariableValue → error
            string inputJson;
            if (!string.IsNullOrWhiteSpace( p.InputPath )) {
                string fullInputPath = _resolver.ResolveSinglePath( p.InputPath );
                if (!File.Exists( fullInputPath )) {
                    throw new FileNotFoundException( $"Input file not found: '{fullInputPath}'" );
                }
                inputJson = await File.ReadAllTextAsync( fullInputPath, cancellationToken );
            } else {
                inputJson = !string.IsNullOrWhiteSpace( inputVariableValue )
                    ? inputVariableValue
                    : throw new ArgumentException(
                    "TransformJson requires either an InputPath parameter or an input variable value, but neither was provided." );
            }

            // 2. Parse input as JsonNode
            JsonNode? document;
            try {
                document = JsonNode.Parse( inputJson );
            } catch (JsonException ex) {
                throw new ArgumentException( $"TransformJson input is not valid JSON: {ex.Message}", ex );
            }

            // 3. Apply each operation in sequence
            for (int i = 0; i < p.Operations.Length; i++) {
                JsonTransformOperation op = p.Operations[i];
                string[] segments = ParsePointer( op.Path );

                document = op.Type switch {
                    JsonTransformType.Extract => ApplyExtract( document, segments ),
                    JsonTransformType.Set => ApplySet( document, segments, op.Value, i ),
                    JsonTransformType.Delete => ApplyDelete( document, segments ),
                    JsonTransformType.Merge => ApplyMerge( document, segments, op.Value, i ),
                    _ => throw new ArgumentException( $"Operation[{i}]: unknown transform type '{op.Type}'." ),
                };
            }

            // 4. Serialize result
            string resultJson = document?.ToJsonString( ) ?? "null";

            // 5. Write to OutputPath if specified
            if (!string.IsNullOrWhiteSpace( p.OutputPath )) {
                string fullOutputPath = _resolver.ResolveSinglePath( p.OutputPath );
                string? dir = Path.GetDirectoryName( fullOutputPath );
                if (!string.IsNullOrEmpty( dir ) && !Directory.Exists( dir )) {
                    _ = Directory.CreateDirectory( dir );
                }
                await File.WriteAllTextAsync( fullOutputPath, resultJson, cancellationToken );
            }

            await output.WriteAsync(
                OperatorOutput.Create(
                    LogLevel.Information,
                    $"TransformJson: applied {p.Operations.Length} operation(s) successfully." ),
                cancellationToken );

            // 6. Always populate OutputVariableValue
            return new ActionOperatorResult( Success: true, OutputVariableValue: resultJson );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            LogActionFailed( _logger, ex );
            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Error, $"TransformJson failed: {ex.Message}" ),
                cancellationToken );
            return new ActionOperatorResult( Success: false, Exception: ex );
        }
    }

    // ── JSON Pointer Parsing ─────────────────────────────────────────────────────

    /// <summary>
    /// Parses a JSON Pointer (RFC 6901) or convenience <c>$.</c>-prefixed path into
    /// an array of unescaped property/index segments.
    /// </summary>
    internal static string[] ParsePointer( string path ) {
        if (string.IsNullOrEmpty( path )) {
            return [];
        }

        // Normalize $.foo.bar → /foo/bar
        if (path.StartsWith( "$.", StringComparison.Ordinal )) {
            path = "/" + path[2..].Replace( '.', '/' );
        } else if (path == "$") {
            return [];
        }

        // RFC 6901: must start with '/'
        if (!path.StartsWith( '/' )) {
            throw new ArgumentException( $"Invalid JSON Pointer: '{path}' — must start with '/' or '$.'." );
        }

        // Split and unescape: ~1 → /, ~0 → ~
        string[] raw = path[1..].Split( '/' );
        string[] segments = new string[raw.Length];
        for (int i = 0; i < raw.Length; i++) {
            segments[i] = raw[i].Replace( "~1", "/" ).Replace( "~0", "~" );
        }
        return segments;
    }

    // ── Navigation ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Navigates to the node at the given segments path, returning the node.
    /// Throws if any intermediate segment is missing or not traversable.
    /// </summary>
    private static JsonNode? NavigateTo( JsonNode? root, string[] segments ) {
        JsonNode? current = root;
        for (int i = 0; i < segments.Length; i++) {
            if (current is null) {
                string pointer = "/" + string.Join( "/", segments[..(i + 1)] );
                throw new ArgumentException( $"Cannot navigate to '{pointer}' — parent is null." );
            }
            current = GetChild( current, segments[i], segments, i );
        }
        return current;
    }

    /// <summary>
    /// Navigates to the parent node and returns both the parent and the final segment name.
    /// For a root path (empty segments), returns <c>(null, null)</c>.
    /// </summary>
    private static (JsonNode? Parent, string? FinalSegment) NavigateToParent( JsonNode? root, string[] segments ) {
        if (segments.Length == 0) {
            return (null, null);
        }
        string[] parentSegments = segments[..^1];
        JsonNode? parent = parentSegments.Length == 0 ? root : NavigateTo( root, parentSegments );
        return (parent, segments[^1]);
    }

    private static JsonNode? GetChild( JsonNode current, string segment, string[] allSegments, int depth ) {
        if (current is JsonObject obj) {
            return obj.TryGetPropertyValue( segment, out JsonNode? child ) ? child : null;
        }
        if (current is JsonArray arr) {
            if (int.TryParse( segment, out int index ) && index >= 0 && index < arr.Count) {
                return arr[index];
            }
            string pointer = "/" + string.Join( "/", allSegments[..(depth + 1)] );
            throw new ArgumentException( $"Cannot navigate to '{pointer}' — array index '{segment}' is out of range." );
        }
        string ptr = "/" + string.Join( "/", allSegments[..(depth + 1)] );
        throw new ArgumentException( $"Cannot navigate to '{ptr}' — node is {current.GetValueKind( )}, not an object or array." );
    }

    // ── Operations ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the value at the pointer path. The extracted value becomes the entire output.
    /// </summary>
    private static JsonNode? ApplyExtract( JsonNode? document, string[] segments ) {
        if (segments.Length == 0) {
            return document;
        }
        JsonNode? target = NavigateTo( document, segments );
        if (target is null) {
            return null;
        }
        // DeepClone detaches from parent
        return target.DeepClone( );
    }

    /// <summary>
    /// Sets or replaces the value at the pointer path. Creates intermediate objects if needed.
    /// </summary>
    private static JsonNode? ApplySet( JsonNode? document, string[] segments, string? value, int opIndex ) {
        if (value is null) {
            throw new ArgumentException( $"Operation[{opIndex}] (Set): Value is required." );
        }

        JsonNode? newValue;
        try {
            newValue = JsonNode.Parse( value );
        } catch (JsonException ex) {
            throw new ArgumentException( $"Operation[{opIndex}] (Set): Value is not valid JSON — {ex.Message}", ex );
        }

        // Set at root
        if (segments.Length == 0) {
            return newValue;
        }

        // Ensure the document is not null
        document ??= new JsonObject( );

        // Navigate to parent, creating intermediate objects as needed
        JsonNode current = document;
        for (int i = 0; i < segments.Length - 1; i++) {
            if (current is JsonObject parentObj) {
                if (!parentObj.TryGetPropertyValue( segments[i], out JsonNode? child ) || child is null) {
                    JsonObject intermediate = [];
                    parentObj[segments[i]] = intermediate;
                    current = intermediate;
                } else {
                    current = child;
                }
            } else {
                current = current is JsonArray parentArr
                    ? int.TryParse( segments[i], out int idx ) && idx >= 0 && idx < parentArr.Count
                    ? parentArr[idx]
                        ?? throw new ArgumentException( $"Operation[{opIndex}] (Set): array element at index {idx} is null." )
                    : throw new ArgumentException( $"Operation[{opIndex}] (Set): array index '{segments[i]}' is out of range." )
                    : throw new ArgumentException(
                    $"Operation[{opIndex}] (Set): cannot navigate through {current.GetValueKind( )} at segment '{segments[i]}'." );
            }
        }

        string finalSegment = segments[^1];
        if (current is JsonObject targetObj) {
            targetObj[finalSegment] = newValue;
        } else if (current is JsonArray targetArr) {
            if (int.TryParse( finalSegment, out int idx ) && idx >= 0 && idx < targetArr.Count) {
                targetArr[idx] = newValue;
            } else {
                throw new ArgumentException( $"Operation[{opIndex}] (Set): array index '{finalSegment}' is out of range." );
            }
        } else {
            throw new ArgumentException(
                $"Operation[{opIndex}] (Set): target is {current.GetValueKind( )}, not an object or array." );
        }

        return document;
    }

    /// <summary>
    /// Removes the property or element at the pointer path.
    /// Succeeds silently if the path does not exist.
    /// </summary>
    private static JsonNode? ApplyDelete( JsonNode? document, string[] segments ) {
        if (segments.Length == 0) {
            return null;
        }

        if (document is null) {
            return null;
        }

        (JsonNode? parent, string? finalSegment) = NavigateToParent( document, segments );
        if (parent is null || finalSegment is null) {
            return null;
        }

        if (parent is JsonObject parentObj) {
            _ = parentObj.Remove( finalSegment );
        } else if (parent is JsonArray parentArr) {
            if (int.TryParse( finalSegment, out int idx ) && idx >= 0 && idx < parentArr.Count) {
                parentArr.RemoveAt( idx );
            }
            // Out of range: silent success (path doesn't effectively exist)
        }
        // Other node types: silent success

        return document;
    }

    /// <summary>
    /// Deep-merges a JSON object value into the object at the pointer path.
    /// </summary>
    private static JsonNode? ApplyMerge( JsonNode? document, string[] segments, string? value, int opIndex ) {
        if (value is null) {
            throw new ArgumentException( $"Operation[{opIndex}] (Merge): Value is required." );
        }

        JsonNode? mergeValue;
        try {
            mergeValue = JsonNode.Parse( value );
        } catch (JsonException ex) {
            throw new ArgumentException( $"Operation[{opIndex}] (Merge): Value is not valid JSON — {ex.Message}", ex );
        }

        if (mergeValue is not JsonObject mergeObj) {
            throw new ArgumentException( $"Operation[{opIndex}] (Merge): Value must be a JSON object." );
        }

        // Navigate to target
        JsonNode? target = segments.Length == 0 ? document : NavigateTo( document, segments );
        if (target is not JsonObject targetObj) {
            throw new ArgumentException(
                $"Operation[{opIndex}] (Merge): target at '/{string.Join( "/", segments )}' is not a JSON object." );
        }

        DeepMerge( targetObj, mergeObj );
        return document;
    }

    /// <summary>
    /// Recursively merges <paramref name="source"/> into <paramref name="target"/>.
    /// Object properties are merged recursively; non-object values are replaced.
    /// </summary>
    private static void DeepMerge( JsonObject target, JsonObject source ) {
        foreach (KeyValuePair<string, JsonNode?> kvp in source) {
            if (kvp.Value is null) {
                target[kvp.Key] = null;
                continue;
            }

            JsonNode sourceValue = kvp.Value.DeepClone( );

            if (sourceValue is JsonObject sourceObj
                && target.TryGetPropertyValue( kvp.Key, out JsonNode? existingNode )
                && existingNode is JsonObject existingObj) {
                // Both are objects — recurse
                DeepMerge( existingObj, sourceObj );
            } else {
                target[kvp.Key] = sourceValue;
            }
        }
    }

    [LoggerMessage( Level = LogLevel.Error, Message = "Action failed" )]
    private static partial void LogActionFailed( ILogger logger, Exception ex );
}
