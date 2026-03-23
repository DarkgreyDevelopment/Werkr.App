using System.Text.Json;
using System.Text.Json.Serialization;

namespace Werkr.Data.Entities.Triggers;

/// <summary>
/// Strongly-typed snapshot of a trigger's definition at a point in time.
/// Serialized as JSON into <see cref="TriggerVersion.Definition"/>.
/// </summary>
public sealed record TriggerDefinitionSnapshot(
    long WorkflowId,
    string WatchDirectory,
    string FilePattern,
    string EventTypes,
    int DebounceMs,
    bool Enabled,
    string? TargetTags,
    string VersionBindingMode,
    long? PinnedWorkflowVersionId
) {
    private static readonly JsonSerializerOptions s_jsonOptions = new( ) {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter( ) },
    };

    /// <summary>
    /// Creates a snapshot from a live <see cref="FileMonitorTrigger"/> entity.
    /// </summary>
    public static TriggerDefinitionSnapshot FromTrigger( FileMonitorTrigger trigger ) =>
        new(
            WorkflowId: trigger.WorkflowId,
            WatchDirectory: trigger.WatchDirectory,
            FilePattern: trigger.FilePattern,
            EventTypes: trigger.EventTypes,
            DebounceMs: trigger.DebounceMs,
            Enabled: trigger.Enabled,
            TargetTags: trigger.TargetTags,
            VersionBindingMode: trigger.VersionBindingMode.ToString( ),
            PinnedWorkflowVersionId: trigger.PinnedWorkflowVersionId
        );

    /// <summary>Serializes this snapshot to a camelCase JSON string.</summary>
    public string ToJson( ) => JsonSerializer.Serialize( this, s_jsonOptions );

    /// <summary>Deserializes a JSON string back into a <see cref="TriggerDefinitionSnapshot"/>.</summary>
    public static TriggerDefinitionSnapshot? FromJson( string json ) =>
        JsonSerializer.Deserialize<TriggerDefinitionSnapshot>( json, s_jsonOptions );
}
