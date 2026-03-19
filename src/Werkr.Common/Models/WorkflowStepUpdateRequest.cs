namespace Werkr.Common.Models;

/// <summary>Request DTO for updating a workflow step.</summary>
public sealed record WorkflowStepUpdateRequest(
    int Order = 0,
    string ControlStatement = "Default",
    string? ConditionExpression = null,
    int MaxIterations = 100,
    Guid? AgentConnectionIdOverride = null,
    string DependencyMode = "AllSuccess",
    string? InputVariableName = null,
    string? OutputVariableName = null,
    bool IsComposite = false,
    string CompositeType = "None",
    long? ChildWorkflowId = null,
    string? IterationVariableName = null,
    string? CollectionVariableName = null
);
