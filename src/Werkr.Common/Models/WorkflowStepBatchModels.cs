namespace Werkr.Common.Models;

/// <summary>
/// Atomic batch request for creating, updating, and deleting workflow steps and dependencies.
/// Processed within a single database transaction — all-or-nothing.
/// </summary>
/// <param name="Operations">Ordered list of step operations.</param>
public sealed record WorkflowStepBatchRequest(
    IReadOnlyList<StepBatchOperation> Operations
);

/// <summary>
/// A single step operation within a batch.
/// StepId uses sign convention: negative = temp ID (new step), positive = real ID (existing step).
/// </summary>
/// <param name="OperationType">"Add", "Update", or "Delete".</param>
/// <param name="StepId">Negative temp ID for adds; positive real ID for updates/deletes.</param>
/// <param name="TaskId">Required for "Add" operations.</param>
/// <param name="Order">Step order (topological tiebreaker).</param>
/// <param name="ControlStatement">Control flow type: "Default", "If", "ElseIf", "Else", "While", "Do".</param>
/// <param name="ConditionExpression">Condition for If/ElseIf/While/Do steps.</param>
/// <param name="MaxIterations">Loop guard for While/Do steps.</param>
/// <param name="DependencyMode">"All" or "Any".</param>
/// <param name="AgentConnectionIdOverride">Optional pin to specific agent.</param>
/// <param name="InputVariableName">Variable name to read from predecessor output.</param>
/// <param name="OutputVariableName">Variable name to write step output into.</param>
/// <param name="DependencyChanges">Optional per-step dependency mutations.</param>
public sealed record StepBatchOperation(
    string OperationType,
    long StepId,
    long? TaskId = null,
    int Order = 0,
    string ControlStatement = "Default",
    string? ConditionExpression = null,
    int MaxIterations = 100,
    string DependencyMode = "All",
    Guid? AgentConnectionIdOverride = null,
    string? InputVariableName = null,
    string? OutputVariableName = null,
    IReadOnlyList<DependencyBatchItem>? DependencyChanges = null
);

/// <summary>
/// A dependency change within a batch operation.
/// DependsOnStepId uses sign convention: negative = temp ID, positive = real ID (resolved by endpoint).
/// </summary>
/// <param name="OperationType">"Add" or "Delete".</param>
/// <param name="DependsOnStepId">Negative temp ID or positive real ID.</param>
public sealed record DependencyBatchItem(
    string OperationType,
    long DependsOnStepId
);

/// <summary>Response from a batch step operation.</summary>
/// <param name="Success">Whether the entire batch succeeded.</param>
/// <param name="IdMappings">Temp-to-real ID mappings for created steps.</param>
/// <param name="Errors">Validation or processing errors (empty on success).</param>
public sealed record WorkflowStepBatchResponse(
    bool Success,
    IReadOnlyList<StepIdMapping> IdMappings,
    IReadOnlyList<string> Errors
);

/// <summary>Maps a client-assigned temp ID to the server-generated real ID.</summary>
/// <param name="TempId">The negative temp ID used in the request.</param>
/// <param name="RealId">The positive real ID assigned by the database.</param>
public sealed record StepIdMapping(
    long TempId,
    long RealId
);
