using System.Text.Json;
using System.Text.Json.Serialization;

namespace Werkr.Data.Entities.Workflows;

/// <summary>
/// Strongly-typed snapshot of a workflow's definition at a point in time.
/// Serialized as JSON into <see cref="WorkflowVersion.Definition"/>.
/// </summary>
public sealed record WorkflowDefinitionSnapshot(
    string Name,
    string Description,
    bool Enabled,
    string[]? TargetTags,
    string? Annotations,
    WorkflowStepSnapshot[]? Steps,
    WorkflowEdgeSnapshot[]? Edges,
    WorkflowVariableSnapshot[]? Variables
) {
    private static readonly JsonSerializerOptions s_jsonOptions = new( ) {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter( ) },
    };

    /// <summary>
    /// Creates a snapshot from a live <see cref="Workflow"/> entity.
    /// The workflow must have Steps (with Dependencies) and Variables loaded.
    /// </summary>
    public static WorkflowDefinitionSnapshot FromWorkflow( Workflow workflow ) {
        WorkflowStepSnapshot[]? steps = workflow.Steps.Count > 0
            ? workflow.Steps.Select( s => new WorkflowStepSnapshot(
                StepId: s.Id,
                TaskId: s.TaskId,
                TaskVersionId: s.TaskVersionId,
                Order: s.Order,
                ControlStatement: s.ControlStatement.ToString( ),
                ConditionExpression: s.ConditionExpression,
                MaxIterations: s.MaxIterations,
                AgentConnectionIdOverride: s.AgentConnectionIdOverride,
                DependencyMode: s.DependencyMode.ToString( ),
                InputVariableName: s.InputVariableName,
                OutputVariableName: s.OutputVariableName,
                IsComposite: s.IsComposite,
                CompositeType: s.CompositeType.ToString( ),
                ChildWorkflowId: s.ChildWorkflowId,
                IterationVariableName: s.IterationVariableName,
                CollectionVariableName: s.CollectionVariableName
            ) ).ToArray( )
            : null;

        WorkflowEdgeSnapshot[]? edges = workflow.Steps
            .SelectMany( s => s.Dependencies.Select( d => new WorkflowEdgeSnapshot(
                StepId: d.StepId,
                DependsOnStepId: d.DependsOnStepId
            ) ) )
            .ToArray( );
        if( edges.Length == 0 ) {
            edges = null;
        }

        WorkflowVariableSnapshot[]? variables = workflow.Variables.Count > 0
            ? workflow.Variables.Select( v => new WorkflowVariableSnapshot(
                Name: v.Name,
                Description: v.Description,
                DefaultValue: v.DefaultValue,
                DataType: v.DataType,
                IsRequired: v.IsRequired,
                LogRedaction: v.LogRedaction
            ) ).ToArray( )
            : null;

        return new(
            Name: workflow.Name,
            Description: workflow.Description,
            Enabled: workflow.Enabled,
            TargetTags: workflow.TargetTags,
            Annotations: workflow.Annotations,
            Steps: steps,
            Edges: edges,
            Variables: variables
        );
    }

    /// <summary>Serializes this snapshot to a camelCase JSON string.</summary>
    public string ToJson( ) => JsonSerializer.Serialize( this, s_jsonOptions );

    /// <summary>Deserializes a JSON string back into a <see cref="WorkflowDefinitionSnapshot"/>.</summary>
    public static WorkflowDefinitionSnapshot? FromJson( string json ) =>
        JsonSerializer.Deserialize<WorkflowDefinitionSnapshot>( json, s_jsonOptions );
}

/// <summary>Snapshot of a single workflow step at version-capture time.</summary>
public sealed record WorkflowStepSnapshot(
    long StepId,
    long? TaskId,
    long? TaskVersionId,
    int Order,
    string ControlStatement,
    string? ConditionExpression,
    int MaxIterations,
    Guid? AgentConnectionIdOverride,
    string DependencyMode,
    string? InputVariableName,
    string? OutputVariableName,
    bool IsComposite,
    string CompositeType,
    long? ChildWorkflowId,
    string? IterationVariableName,
    string? CollectionVariableName
);

/// <summary>Snapshot of a dependency edge between two workflow steps.</summary>
public sealed record WorkflowEdgeSnapshot(
    long StepId,
    long DependsOnStepId
);

/// <summary>Snapshot of a design-time workflow variable definition.</summary>
public sealed record WorkflowVariableSnapshot(
    string Name,
    string? Description,
    string? DefaultValue,
    string? DataType,
    bool IsRequired,
    bool LogRedaction
);
