using System.Text.Json;
using System.Text.Json.Serialization;

namespace Werkr.Data.Entities.Tasks;

/// <summary>
/// Strongly-typed snapshot of a task's definition at a point in time.
/// Serialized as JSON into <see cref="TaskVersion.Definition"/>.
/// Excludes operational fields (SyncIntervalMinutes, IsEphemeral) that are not part of the task definition.
/// </summary>
public sealed record TaskDefinitionSnapshot(
    string Name,
    string Description,
    string ActionType,
    string Content,
    string[]? Arguments,
    string[] TargetTags,
    bool Enabled,
    long? TimeoutMinutes,
    string? SuccessCriteria,
    string? ActionSubType,
    string? ActionParameters,
    long? WorkflowId
) {
    private static readonly JsonSerializerOptions s_jsonOptions = new( ) {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter( ) },
    };

    /// <summary>
    /// Creates a snapshot from a live <see cref="WerkrTask"/> entity.
    /// </summary>
    public static TaskDefinitionSnapshot FromTask( WerkrTask task ) =>
        new(
            Name: task.Name,
            Description: task.Description,
            ActionType: task.ActionType.ToString( ),
            Content: task.Content,
            Arguments: task.Arguments,
            TargetTags: task.TargetTags,
            Enabled: task.Enabled,
            TimeoutMinutes: task.TimeoutMinutes,
            SuccessCriteria: task.SuccessCriteria,
            ActionSubType: task.ActionSubType,
            ActionParameters: task.ActionParameters,
            WorkflowId: task.WorkflowId
        );

    /// <summary>Serializes this snapshot to a camelCase JSON string.</summary>
    public string ToJson( ) => JsonSerializer.Serialize( this, s_jsonOptions );

    /// <summary>Deserializes a JSON string back into a <see cref="TaskDefinitionSnapshot"/>.</summary>
    public static TaskDefinitionSnapshot? FromJson( string json ) =>
        JsonSerializer.Deserialize<TaskDefinitionSnapshot>( json, s_jsonOptions );
}
