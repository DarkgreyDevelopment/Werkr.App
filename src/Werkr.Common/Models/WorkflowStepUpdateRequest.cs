namespace Werkr.Common.Models;

/// <summary>Request DTO for updating a workflow step.</summary>
public sealed record WorkflowStepUpdateRequest(
    int Order = 0,
    string ControlStatement = "Sequential",
    string? ConditionExpression = null,
    int MaxIterations = 100,
    Guid? AgentConnectionIdOverride = null,
    string DependencyMode = "All" );
