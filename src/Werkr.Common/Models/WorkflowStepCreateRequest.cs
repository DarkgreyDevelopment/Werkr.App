namespace Werkr.Common.Models;

/// <summary>Request DTO for creating a workflow step.</summary>
public sealed record WorkflowStepCreateRequest(
    long TaskId,
    int Order = 0,
    string ControlStatement = "Sequential",
    string? ConditionExpression = null,
    int MaxIterations = 100,
    Guid? AgentConnectionIdOverride = null,
    string DependencyMode = "All" );
