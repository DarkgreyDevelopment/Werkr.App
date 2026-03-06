namespace Werkr.Common.Models;

/// <summary>Response DTO for a workflow step.</summary>
public sealed record WorkflowStepDto(
    long Id,
    long WorkflowId,
    long TaskId,
    int Order,
    string ControlStatement,
    string? ConditionExpression,
    int MaxIterations,
    Guid? AgentConnectionIdOverride,
    string DependencyMode,
    IReadOnlyList<StepDependencyDto> Dependencies
);
