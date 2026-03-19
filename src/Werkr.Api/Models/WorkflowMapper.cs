using System.Text.Json;
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
            TargetTags = request.TargetTags,
        };

    /// <summary>Maps a <see cref="WorkflowUpdateRequest"/> to a <see cref="Workflow"/> entity with a given ID.</summary>
    public static Workflow ToEntity( long id, WorkflowUpdateRequest request ) =>
        new( ) {
            Id = id,
            Name = request.Name,
            Description = request.Description ?? string.Empty,
            Enabled = request.Enabled,
            TargetTags = request.TargetTags,
            Annotations = request.Annotations is { Count: > 0 }
                ? JsonSerializer.Serialize( request.Annotations )
                : null,
        };

    /// <summary>Maps a <see cref="Workflow"/> entity to a <see cref="WorkflowDto"/>.</summary>
    public static WorkflowDto ToDto( Workflow workflow ) =>
        new(
            Id: workflow.Id,
            Name: workflow.Name,
            Description: workflow.Description,
            Enabled: workflow.Enabled,
            Steps: [.. workflow.Steps.Select( ToStepDto )],
            TargetTags: workflow.TargetTags,
            Annotations: DeserializeAnnotations( workflow.Annotations ),
            IsChildWorkflow: workflow.IsChildWorkflow,
            ParentStepId: workflow.ParentStepId );

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
            Dependencies: [.. step.Dependencies.Select( ToDepDto )],
            InputVariableName: step.InputVariableName,
            OutputVariableName: step.OutputVariableName,
            TaskName: step.Task?.Name,
            IsComposite: step.IsComposite,
            CompositeType: step.CompositeType.ToString( ),
            ChildWorkflowId: step.ChildWorkflowId,
            IterationVariableName: step.IterationVariableName,
            CollectionVariableName: step.CollectionVariableName );

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
            InputVariableName = request.InputVariableName,
            OutputVariableName = request.OutputVariableName,
            IsComposite = request.IsComposite,
            CompositeType = Enum.Parse<CompositeType>( request.CompositeType, ignoreCase: true ),
            ChildWorkflowId = request.ChildWorkflowId,
            IterationVariableName = request.IterationVariableName,
            CollectionVariableName = request.CollectionVariableName,
        };

    /// <summary>Maps a <see cref="WorkflowRun"/> entity to a <see cref="WorkflowRunDto"/>.</summary>
    public static WorkflowRunDto ToRunDto( WorkflowRun run ) =>
        new(
            Id: run.Id,
            WorkflowId: run.WorkflowId,
            StartTime: run.StartTime,
            EndTime: run.EndTime,
            Status: run.Status.ToString( ) );

    /// <summary>Maps a <see cref="WorkflowRun"/> entity (with Jobs and StepExecutions) to a <see cref="WorkflowRunDetailDto"/>.</summary>
    public static WorkflowRunDetailDto ToRunDetailDto( WorkflowRun run ) =>
        new(
            Id: run.Id,
            WorkflowId: run.WorkflowId,
            StartTime: run.StartTime,
            EndTime: run.EndTime,
            Status: run.Status.ToString( ),
            Jobs: [.. run.Jobs.Select( TaskMapper.ToJobDto )],
            StepExecutions: run.StepExecutions is not null
                ? [.. run.StepExecutions.Select( ToStepExecutionDto )]
                : [] );

    /// <summary>Maps a <see cref="WorkflowStepExecution"/> entity to a <see cref="StepExecutionDto"/>.</summary>
    public static StepExecutionDto ToStepExecutionDto( WorkflowStepExecution execution ) =>
        new(
            Id: execution.Id,
            WorkflowRunId: execution.WorkflowRunId,
            StepId: execution.StepId,
            Attempt: execution.Attempt,
            Status: execution.Status.ToString( ),
            StartTime: execution.StartTime,
            EndTime: execution.EndTime,
            JobId: execution.JobId,
            ErrorMessage: execution.ErrorMessage,
            SkipReason: execution.SkipReason );

    /// <summary>Maps a <see cref="WorkflowVariable"/> entity to a <see cref="WorkflowVariableDto"/>.</summary>
    public static WorkflowVariableDto ToVariableDto( WorkflowVariable variable ) =>
        new(
            Id: variable.Id,
            WorkflowId: variable.WorkflowId,
            Name: variable.Name,
            Description: variable.Description,
            DefaultValue: variable.DefaultValue,
            DataType: variable.DataType,
            IsRequired: variable.IsRequired,
            LogRedaction: variable.LogRedaction );

    /// <summary>Maps a <see cref="WorkflowRunVariable"/> entity to a <see cref="RunVariableCurrentDto"/>.</summary>
    public static RunVariableCurrentDto ToRunVariableCurrentDto( WorkflowRunVariable variable ) =>
        new(
            Name: variable.VariableName,
            Value: variable.Value,
            Version: variable.Version,
            Source: variable.Source.ToString( ),
            Created: variable.Created );

    /// <summary>Maps a <see cref="WorkflowRunVariable"/> entity to a <see cref="RunVariableVersionDto"/>.</summary>
    public static RunVariableVersionDto ToRunVariableVersionDto( WorkflowRunVariable variable ) =>
        new(
            Id: variable.Id,
            VariableName: variable.VariableName,
            Value: variable.Value,
            Version: variable.Version,
            ProducedByStepId: variable.ProducedByStepId,
            ProducedByJobId: variable.ProducedByJobId,
            Source: variable.Source.ToString( ),
            Created: variable.Created );

    private static List<AnnotationDto>? DeserializeAnnotations( string? json ) {
        if (string.IsNullOrWhiteSpace( json )) {
            return null;
        }

        try {
            return JsonSerializer.Deserialize<List<AnnotationDto>>( json );
        } catch (JsonException) {
            return null;
        }
    }
}
