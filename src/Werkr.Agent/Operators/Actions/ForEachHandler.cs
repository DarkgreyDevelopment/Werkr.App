using System.Text.Json;
using System.Threading.Channels;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>ForEach</c> action — accepts a JSON object as input, selects a named
/// array property via <see cref="ForEachParameters.ArrayPropertyName"/>, validates that
/// every element is a scalar (string, number, <c>true</c>, <c>false</c>, or <c>null</c>),
/// and writes the <b>last element</b> of the array as the output variable value.
/// Empty arrays produce a <see langword="null"/> output (success).
/// </summary>
/// <remarks>Creates a new <see cref="ForEachHandler"/>.</remarks>
public sealed partial class ForEachHandler( ILogger<ForEachHandler> logger ) : IActionHandler {

    private readonly ILogger<ForEachHandler> _logger = logger;

    /// <inheritdoc/>
    public string Action => "ForEach";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        string? inputVariableValue = null,
        CancellationToken cancellationToken = default
    ) {
        try {
            ForEachParameters p = parameters.Deserialize<ForEachParameters>( ActionJson.SerializerOptions )
                ?? throw new ArgumentException( "Failed to deserialize ForEach parameters." );

            if (string.IsNullOrWhiteSpace( p.ArrayPropertyName )) {
                throw new ArgumentException( "ArrayPropertyName is required and must not be empty." );
            }

            // Validate input variable is present
            if (string.IsNullOrWhiteSpace( inputVariableValue )) {
                throw new ArgumentException(
                    "ForEach action requires an input variable containing a JSON object, but none was provided." );
            }

            // Parse input as JSON
            JsonDocument inputDoc;
            try {
                inputDoc = JsonDocument.Parse( inputVariableValue );
            } catch (JsonException ex) {
                throw new ArgumentException(
                    $"ForEach input is not valid JSON: {ex.Message}", ex );
            }

            using (inputDoc) {
                JsonElement root = inputDoc.RootElement;

                // Input must be a JSON object
                if (root.ValueKind != JsonValueKind.Object) {
                    throw new ArgumentException(
                        $"ForEach input must be a JSON object, but received {root.ValueKind}." );
                }

                // Named property must exist
                if (!root.TryGetProperty( p.ArrayPropertyName, out JsonElement arrayElement )) {
                    throw new ArgumentException(
                        $"Property '{p.ArrayPropertyName}' was not found in the input JSON object." );
                }

                // Named property must be an array
                if (arrayElement.ValueKind != JsonValueKind.Array) {
                    throw new ArgumentException(
                        $"Property '{p.ArrayPropertyName}' must be a JSON array, but was {arrayElement.ValueKind}." );
                }

                int length = arrayElement.GetArrayLength();

                // Validate every element is a scalar
                int index = 0;
                foreach (JsonElement element in arrayElement.EnumerateArray( )) {
                    if (!IsScalar( element )) {
                        throw new ArgumentException(
                            $"Element at index {index} in '{p.ArrayPropertyName}' is a {element.ValueKind}, " +
                            $"but ForEach only supports scalar values (string, number, true, false, null)." );
                    }
                    index++;
                }

                // Empty array: success with null output
                if (length == 0) {
                    await output.WriteAsync(
                        OperatorOutput.Create( LogLevel.Information,
                            $"ForEach: array '{p.ArrayPropertyName}' is empty — no elements to emit." ),
                        cancellationToken );

                    return new ActionOperatorResult( Success: true, OutputVariableValue: null );
                }

                // Get the last element and serialize it as a JSON scalar string
                JsonElement lastElement = arrayElement[length - 1];
                string lastElementValue = lastElement.GetRawText();

                await output.WriteAsync(
                    OperatorOutput.Create( LogLevel.Information,
                        $"ForEach: processed {length} element(s) from '{p.ArrayPropertyName}', emitting last element." ),
                    cancellationToken );

                return new ActionOperatorResult( Success: true, OutputVariableValue: lastElementValue );
            }
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            LogActionFailed( _logger, ex );
            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Error, $"ForEach failed: {ex.Message}" ),
                cancellationToken );
            return new ActionOperatorResult( Success: false, Exception: ex );
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> if the element is a scalar JSON value
    /// (string, number, <c>true</c>, <c>false</c>, or <c>null</c>).
    /// </summary>
    private static bool IsScalar( JsonElement element ) {
        return element.ValueKind is JsonValueKind.String
            or JsonValueKind.Number
            or JsonValueKind.True
            or JsonValueKind.False
            or JsonValueKind.Null;
    }

    [LoggerMessage( Level = LogLevel.Error, Message = "ForEach action failed" )]
    private static partial void LogActionFailed( ILogger logger, Exception ex );
}
