using Werkr.Common.Models;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Api.Models;

/// <summary>
/// Bidirectional mapping between Workflow DTOs and domain entities.
/// </summary>
internal static class WorkflowMapper {

    /// <summary>Maps a <see cref="WorkflowCreateRequest"/> to a <see cref="Workflow"/> entity.</summary>
    public static Workflow ToEntity( WorkflowCreateRequest request ) =>
        new( ) {
            Name = request.Name,
            Description = request.Description ?? string.Empty,
            Enabled = request.Enabled,
        };

    /// <summary>Maps a <see cref="WorkflowUpdateRequest"/> to a <see cref="Workflow"/> entity with a given ID.</summary>
    public static Workflow ToEntity( long id, WorkflowUpdateRequest request ) =>
        new( ) {
            Id = id,
            Name = request.Name,
            Description = request.Description ?? string.Empty,
            Enabled = request.Enabled,
        };

    /// <summary>Maps a <see cref="Workflow"/> entity to a <see cref="WorkflowDto"/>.</summary>
    public static WorkflowDto ToDto( Workflow workflow ) =>
        new(
            Id: workflow.Id,
            Name: workflow.Name,
            Description: workflow.Description,
            Enabled: workflow.Enabled,
            Steps: [.. workflow.Steps.Select( ToStepDto )] );

    /// <summary>Maps a <see cref="WorkflowStep"/> entity to a <see cref="WorkflowStepDto"/>.</summary>
    public static WorkflowStepDto ToStepDto( WorkflowStep step ) =>
        new(
            Id: step.Id,
            WorkflowId: step.WorkflowId,
            TaskId: step.TaskId,
            Order: step.Order,
            ControlStatement: step.ControlStatement.ToString( ),
            ConditionExpression: step.ConditionExpression,
            MaxIterations: step.MaxIterations,
            AgentConnectionIdOverride: step.AgentConnectionIdOverride,
            DependencyMode: step.DependencyMode.ToString( ),
            Dependencies: [.. step.Dependencies.Select( ToDepDto )] );

    /// <summary>Maps a <see cref="WorkflowStepDependency"/> to a <see cref="StepDependencyDto"/>.</summary>
    public static StepDependencyDto ToDepDto( WorkflowStepDependency dep ) =>
        new( StepId: dep.StepId, DependsOnStepId: dep.DependsOnStepId );

    /// <summary>Maps a <see cref="WorkflowStepCreateRequest"/> to a <see cref="WorkflowStep"/> entity.</summary>
    public static WorkflowStep ToStepEntity( long workflowId, WorkflowStepCreateRequest request ) =>
        new( ) {
            WorkflowId = workflowId,
            TaskId = request.TaskId,
            Order = request.Order,
            ControlStatement = Enum.Parse<ControlStatement>( request.ControlStatement, ignoreCase: true ),
            ConditionExpression = request.ConditionExpression,
            MaxIterations = request.MaxIterations,
            AgentConnectionIdOverride = request.AgentConnectionIdOverride,
            DependencyMode = Enum.Parse<DependencyMode>( request.DependencyMode, ignoreCase: true ),
        };

    /// <summary>Maps a <see cref="WorkflowRun"/> entity to a <see cref="WorkflowRunDto"/>.</summary>
    public static WorkflowRunDto ToRunDto( WorkflowRun run ) =>
        new(
            Id: run.Id,
            WorkflowId: run.WorkflowId,
            StartTime: run.StartTime,
            EndTime: run.EndTime,
            Status: run.Status.ToString( ) );

    /// <summary>Maps a <see cref="WorkflowRun"/> entity (with Jobs) to a <see cref="WorkflowRunDetailDto"/>.</summary>
    public static WorkflowRunDetailDto ToRunDetailDto( WorkflowRun run ) =>
        new(
            Id: run.Id,
            WorkflowId: run.WorkflowId,
            StartTime: run.StartTime,
            EndTime: run.EndTime,
            Status: run.Status.ToString( ),
            Jobs: [.. run.Jobs.Select( TaskMapper.ToJobDto )] );
}
