using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Werkr.Common.Models;
using Werkr.Common.Models.Audit;
using Werkr.Core.Audit;
using Werkr.Data;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Core.Workflows;

/// <summary>
/// Provides CRUD operations for <see cref="Workflow"/> entities including
/// step and dependency management, and DAG validation via Kahn's algorithm.
/// </summary>
/// <param name="dbContext">Database context.</param>
/// <param name="versionService">Workflow versioning service.</param>
/// <param name="auditService">Audit event service.</param>
/// <param name="logger">Logger instance.</param>
public sealed partial class WorkflowService(
    WerkrDbContext dbContext,
    WorkflowVersionService versionService,
    IAuditService auditService,
    ILogger<WorkflowService> logger
) {

    /// <summary>Creates a new workflow.</summary>
    /// <param name="workflow">The workflow to create.</param>
    /// <param name="userId">The user who created the workflow, or null for system operations.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created workflow with generated Id.</returns>
    /// <exception cref="InvalidOperationException">Workflow name is not unique.</exception>
    public async Task<Workflow> CreateAsync(
        Workflow workflow,
        string? userId = null,
        CancellationToken ct = default
    ) {
        ValidateWorkflow( workflow );

        bool nameExists = await dbContext.Workflows.AnyAsync(
            w => w.Name == workflow.Name,
            ct
        );
        if (nameExists) {
            throw new InvalidOperationException( $"A workflow with name '{workflow.Name}' already exists." );
        }

        _ = dbContext.Workflows.Add( workflow );
        _ = await dbContext.SaveChangesAsync( ct );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation( "Created workflow {WorkflowId} '{WorkflowName}'.",
                workflow.Id.ToString( ),
                workflow.Name
            );
        }

        // Load steps + deps + vars for versioning snapshot (empty for new workflow)
        await dbContext.Entry( workflow ).Collection( w => w.Steps ).LoadAsync( ct );
        foreach (WorkflowStep step in workflow.Steps) {
            await dbContext.Entry( step ).Collection( s => s.Dependencies ).LoadAsync( ct );
        }
        await dbContext.Entry( workflow ).Collection( w => w.Variables ).LoadAsync( ct );

        _ = await versionService.CreateVersionAsync( workflow, userId, "Initial version", ct );

        return workflow;
    }

    /// <summary>Updates an existing workflow.</summary>
    /// <param name="workflow">The workflow with updated values.</param>
    /// <param name="userId">The user who updated the workflow, or null for system operations.</param>
    /// <param name="changeDescription">Optional human-readable description of the change.</param>
    /// <param name="expectedVersionNumber">Optimistic concurrency check — if set, the current version number must match.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated workflow.</returns>
    /// <exception cref="KeyNotFoundException">Workflow not found.</exception>
    /// <exception cref="InvalidOperationException">Workflow name is not unique or version mismatch.</exception>
    public async Task<Workflow> UpdateAsync(
        Workflow workflow,
        string? userId = null,
        string? changeDescription = null,
        int? expectedVersionNumber = null,
        CancellationToken ct = default
    ) {
        ValidateWorkflow( workflow );

        Workflow existing = await dbContext.Workflows
            .Include( w => w.CurrentVersion )
            .Include( w => w.Steps )
                .ThenInclude( s => s.Dependencies )
            .Include( w => w.Variables )
            .FirstOrDefaultAsync(
                w => w.Id == workflow.Id,
                ct
            )
            ?? throw new KeyNotFoundException( $"Workflow with Id={workflow.Id} was not found." );

        // Optimistic concurrency check
        if (expectedVersionNumber.HasValue && existing.CurrentVersion is not null
            && existing.CurrentVersion.VersionNumber != expectedVersionNumber.Value) {
            throw new InvalidOperationException(
                $"Version conflict: expected version {expectedVersionNumber.Value} " +
                $"but current is {existing.CurrentVersion.VersionNumber}." );
        }

        bool nameConflict = await dbContext.Workflows.AnyAsync(
            w => w.Name == workflow.Name && w.Id != workflow.Id, ct );
        if (nameConflict) {
            throw new InvalidOperationException( $"A workflow with name '{workflow.Name}' already exists." );
        }

        existing.Name = workflow.Name;
        existing.Description = workflow.Description;
        existing.Enabled = workflow.Enabled;
        existing.TargetTags = workflow.TargetTags;

        if (workflow.Annotations is not null) {
            existing.Annotations = workflow.Annotations;
        }

        _ = await dbContext.SaveChangesAsync( ct );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation( "Updated workflow {WorkflowId} '{WorkflowName}'.",
                existing.Id.ToString( ),
                existing.Name
            );
        }

        _ = await versionService.CreateVersionAsync( existing, userId, changeDescription, ct );

        return existing;
    }

    /// <summary>Deletes a workflow by ID.</summary>
    /// <param name="workflowId">The workflow identifier.</param>
    /// <param name="userId">The user who deleted the workflow, or null for system operations.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="KeyNotFoundException">Workflow not found.</exception>
    /// <exception cref="InvalidOperationException">Workflow must be disabled and have no active runs.</exception>
    public async Task DeleteAsync(
        long workflowId,
        string? userId = null,
        CancellationToken ct = default
    ) {
        Workflow existing = await dbContext.Workflows.FirstOrDefaultAsync(
            w => w.Id == workflowId,
            ct
        )
            ?? throw new KeyNotFoundException( $"Workflow with Id={workflowId} was not found." );

        if (existing.Enabled) {
            throw new InvalidOperationException( "Workflow must be disabled before deletion." );
        }

        bool hasActiveRuns = await dbContext.WorkflowRuns.AnyAsync(
            r => r.WorkflowId == workflowId && r.Status == WorkflowRunStatus.Running, ct );
        if (hasActiveRuns) {
            throw new InvalidOperationException( "Cannot delete a workflow with active runs." );
        }

        // Clear circular FK before deletion to avoid cascade issues
        if (existing.CurrentVersionId.HasValue) {
            existing.CurrentVersionId = null;
            _ = await dbContext.SaveChangesAsync( ct );
        }

        _ = dbContext.Workflows.Remove( existing );
        _ = await dbContext.SaveChangesAsync( ct );

        await auditService.LogAsync( new AuditEntry(
            EventTypeId: AuditEventType.WorkflowDeleted.ToEventId( ),
            ActorId: userId,
            ActorType: userId is not null ? "User" : "system",
            EntityType: "Workflow",
            EntityId: workflowId.ToString( ),
            ActionPerformed: "Deleted",
            Details: new { WorkflowName = existing.Name }
        ), ct );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation( "Deleted workflow {WorkflowId} '{WorkflowName}'.",
                workflowId.ToString( ),
                existing.Name
            );
        }
    }

    /// <summary>Toggles the enabled state of a workflow.</summary>
    /// <param name="workflowId">The workflow identifier.</param>
    /// <param name="enabled">The new enabled state.</param>
    /// <param name="userId">The user who toggled the state, or null for system operations.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="KeyNotFoundException">Workflow not found.</exception>
    public async Task SetEnabledAsync(
        long workflowId,
        bool enabled,
        string? userId = null,
        CancellationToken ct = default
    ) {
        Workflow existing = await dbContext.Workflows
            .Include( w => w.Steps )
                .ThenInclude( s => s.Dependencies )
            .Include( w => w.Variables )
            .FirstOrDefaultAsync( w => w.Id == workflowId, ct )
            ?? throw new KeyNotFoundException( $"Workflow with Id={workflowId} was not found." );

        existing.Enabled = enabled;
        _ = await dbContext.SaveChangesAsync( ct );

        _ = await versionService.CreateVersionAsync(
            existing, userId, enabled ? "Enabled workflow" : "Disabled workflow", ct );

        await auditService.LogAsync( new AuditEntry(
            EventTypeId: (enabled ? AuditEventType.WorkflowEnabled : AuditEventType.WorkflowDisabled).ToEventId( ),
            ActorId: userId,
            ActorType: userId is not null ? "User" : "system",
            EntityType: "Workflow",
            EntityId: workflowId.ToString( ),
            ActionPerformed: enabled ? "Enabled" : "Disabled",
            Details: new { WorkflowName = existing.Name }
        ), ct );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation( "Workflow {WorkflowId} enabled={Enabled}.",
                workflowId.ToString( ),
                enabled.ToString( )
            );
        }
    }

    /// <summary>Retrieves a single workflow by ID with steps, dependencies, and tasks.</summary>
    /// <param name="workflowId">The workflow identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The workflow, or null if not found.</returns>
    public async Task<Workflow?> GetByIdAsync(
        long workflowId,
        CancellationToken ct = default
    ) =>
        await dbContext.Workflows
            .Include( w => w.Steps )
                .ThenInclude( s => s.Dependencies )
            .Include( w => w.Steps )
                .ThenInclude( s => s.Task )
                    .ThenInclude( t => t!.CurrentVersion )
            .Include( w => w.Steps )
                .ThenInclude( s => s.TaskVersion )
            .Include( w => w.WorkflowSchedules )
                .ThenInclude( ws => ws.Schedule )
            .AsNoTracking( )
            .FirstOrDefaultAsync(
                w => w.Id == workflowId,
                ct
            );

    /// <summary>Retrieves all workflows with steps.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only list of workflows.</returns>
    public async Task<IReadOnlyList<Workflow>> GetAllAsync( CancellationToken ct = default ) =>
        await dbContext.Workflows
            .Include( w => w.Steps )
                .ThenInclude( s => s.Dependencies )
            .Include( w => w.Steps )
                .ThenInclude( s => s.Task )
                    .ThenInclude( t => t!.CurrentVersion )
            .Include( w => w.Steps )
                .ThenInclude( s => s.TaskVersion )
            .Include( w => w.WorkflowSchedules )
                .ThenInclude( ws => ws.Schedule )
            .AsNoTracking( )
            .OrderBy( w => w.Name )
            .ToListAsync( ct );

    /// <summary>Adds a step to a workflow.</summary>
    /// <param name="workflowId">The workflow to add the step to.</param>
    /// <param name="step">The step to add.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="KeyNotFoundException">Workflow not found.</exception>
    public async Task<WorkflowStep> AddStepAsync(
        long workflowId,
        WorkflowStep step,
        CancellationToken ct = default
    ) {
        bool exists = await dbContext.Workflows.AnyAsync(
            w => w.Id == workflowId,
            ct
        );
        if (!exists) {
            throw new KeyNotFoundException( $"Workflow with Id={workflowId} was not found." );
        }

        step.WorkflowId = workflowId;
        _ = dbContext.WorkflowSteps.Add( step );
        _ = await dbContext.SaveChangesAsync( ct );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation( "Added step {StepId} to workflow {WorkflowId}.",
                step.Id.ToString( ),
                workflowId.ToString( )
            );
        }

        return step;
    }

    /// <summary>Removes a step from a workflow.</summary>
    /// <param name="stepId">The step identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="KeyNotFoundException">Step not found.</exception>
    public async Task RemoveStepAsync(
        long stepId,
        CancellationToken ct = default
    ) {
        WorkflowStep step = await dbContext.WorkflowSteps.FirstOrDefaultAsync(
            s => s.Id == stepId,
            ct
        )
            ?? throw new KeyNotFoundException( $"WorkflowStep with Id={stepId} was not found." );

        _ = dbContext.WorkflowSteps.Remove( step );
        _ = await dbContext.SaveChangesAsync( ct );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation( "Removed step {StepId} from workflow {WorkflowId}.",
                stepId.ToString( ),
                step.WorkflowId.ToString( )
            );
        }
    }

    /// <summary>Updates an existing workflow step.</summary>
    /// <param name="step">The step with updated values.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="KeyNotFoundException">Step not found.</exception>
    public async Task<WorkflowStep> UpdateStepAsync(
        WorkflowStep step,
        CancellationToken ct = default
    ) {
        WorkflowStep existing = await dbContext.WorkflowSteps.FirstOrDefaultAsync(
            s => s.Id == step.Id,
            ct
        )
            ?? throw new KeyNotFoundException( $"WorkflowStep with Id={step.Id} was not found." );

        existing.TaskId = step.TaskId;
        existing.Order = step.Order;
        existing.ControlStatement = step.ControlStatement;
        existing.ConditionExpression = step.ConditionExpression;
        existing.MaxIterations = step.MaxIterations;
        existing.AgentConnectionIdOverride = step.AgentConnectionIdOverride;
        existing.DependencyMode = step.DependencyMode;
        existing.InputVariableName = step.InputVariableName;
        existing.OutputVariableName = step.OutputVariableName;

        _ = await dbContext.SaveChangesAsync( ct );
        return existing;
    }

    /// <summary>Adds a dependency relationship between two steps.</summary>
    /// <param name="stepId">The dependent step.</param>
    /// <param name="dependsOnStepId">The predecessor step.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="KeyNotFoundException">Step not found.</exception>
    /// <exception cref="InvalidOperationException">Dependency already exists or self-reference.</exception>
    public async Task AddStepDependencyAsync(
        long stepId,
        long dependsOnStepId,
        CancellationToken ct = default
    ) {
        if (stepId == dependsOnStepId) {
            throw new InvalidOperationException( "A step cannot depend on itself." );
        }

        bool stepExists = await dbContext.WorkflowSteps.AnyAsync(
            s => s.Id == stepId,
            ct
        );
        if (!stepExists) {
            throw new KeyNotFoundException( $"WorkflowStep with Id={stepId} was not found." );
        }

        bool depExists = await dbContext.WorkflowSteps.AnyAsync(
            s => s.Id == dependsOnStepId,
            ct
        );
        if (!depExists) {
            throw new KeyNotFoundException( $"WorkflowStep with Id={dependsOnStepId} was not found." );
        }

        bool alreadyExists = await dbContext.WorkflowStepDependencies
            .AnyAsync(
                d => d.StepId == stepId && d.DependsOnStepId == dependsOnStepId,
                ct
            );
        if (alreadyExists) {
            throw new InvalidOperationException(
                $"Dependency from step {stepId} on step {dependsOnStepId} already exists." );
        }

        WorkflowStepDependency dep = new( ) {
            StepId = stepId,
            DependsOnStepId = dependsOnStepId,
        };
        _ = dbContext.WorkflowStepDependencies.Add( dep );
        _ = await dbContext.SaveChangesAsync( ct );

        if (logger.IsEnabled( LogLevel.Debug )) {
            logger.LogDebug( "Added dependency: step {StepId} depends on step {DepStepId}.",
                stepId.ToString( ),
                dependsOnStepId.ToString( )
            );
        }
    }

    /// <summary>Removes a dependency relationship between two steps.</summary>
    /// <param name="stepId">The dependent step.</param>
    /// <param name="dependsOnStepId">The predecessor step.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="KeyNotFoundException">Dependency not found.</exception>
    public async Task RemoveStepDependencyAsync(
        long stepId,
        long dependsOnStepId,
        CancellationToken ct = default
    ) {
        WorkflowStepDependency dep = await dbContext.WorkflowStepDependencies
            .FirstOrDefaultAsync(
                d => d.StepId == stepId && d.DependsOnStepId == dependsOnStepId,
                ct
            )
            ?? throw new KeyNotFoundException(
                $"Dependency from step {stepId} on step {dependsOnStepId} was not found." );

        _ = dbContext.WorkflowStepDependencies.Remove( dep );
        _ = await dbContext.SaveChangesAsync( ct );
    }

    /// <summary>
    /// Validates the workflow DAG: checks for cycles via Kahn's algorithm
    /// and validates control flow constraints.
    /// </summary>
    /// <param name="workflowId">The workflow identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Topologically sorted steps, ties broken by <see cref="WorkflowStep.Order"/>.</returns>
    /// <exception cref="KeyNotFoundException">Workflow not found.</exception>
    /// <exception cref="InvalidOperationException">Cycle detected or control flow validation failed.</exception>
    public async Task<IReadOnlyList<WorkflowStep>> ValidateDagAsync(
        long workflowId,
        CancellationToken ct = default
    ) {
        List<WorkflowStep> steps = await dbContext.WorkflowSteps
            .Include( s => s.Dependencies )
            .Where( s => s.WorkflowId == workflowId )
            .ToListAsync( ct );

        if (steps.Count == 0) {
            bool workflowExists = await dbContext.Workflows.AnyAsync(
                w => w.Id == workflowId,
                ct
            );
            return !workflowExists
                ? throw new KeyNotFoundException(
                    $"Workflow with Id={workflowId} was not found."
                )
                : [];
        }

        // Build adjacency list and in-degree map
        Dictionary<long, List<long>> adjacency = [];
        Dictionary<long, int> inDegree = [];
        Dictionary<long, WorkflowStep> stepMap = [];

        foreach (WorkflowStep step in steps) {
            stepMap[step.Id] = step;
            adjacency[step.Id] = [];
            inDegree[step.Id] = 0;
        }

        foreach (WorkflowStep step in steps) {
            foreach (WorkflowStepDependency dep in step.Dependencies) {
                if (adjacency.TryGetValue( dep.DependsOnStepId, out List<long>? value )) {
                    value.Add( step.Id );
                    inDegree[step.Id]++;
                }
            }
        }

        // Kahn's algorithm — use a priority queue for Order tiebreaking
        PriorityQueue<long, int> queue = new( );
        foreach (KeyValuePair<long, int> kvp in inDegree) {
            if (kvp.Value == 0) {
                queue.Enqueue(
                    kvp.Key,
                    stepMap[kvp.Key].Order
                );
            }
        }

        List<WorkflowStep> sorted = [];
        while (queue.Count > 0) {
            long current = queue.Dequeue( );
            sorted.Add( stepMap[current] );

            foreach (long dependent in adjacency[current]) {
                inDegree[dependent]--;
                if (inDegree[dependent] == 0) {
                    queue.Enqueue(
                        dependent,
                        stepMap[dependent].Order
                    );
                }
            }
        }

        if (sorted.Count != steps.Count) {
            throw new InvalidOperationException(
                $"Cycle detected in workflow {workflowId}: processed {sorted.Count} of {steps.Count} steps." );
        }

        // Validate control flow constraints
        ValidateControlFlow( sorted );

        return sorted;
    }

    /// <summary>
    /// Gets the topological levels for parallel execution.
    /// Steps at the same level have all dependencies satisfied and can run concurrently.
    /// </summary>
    /// <param name="workflowId">The workflow identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ordered list of levels, each containing steps that can execute in parallel.</returns>
    public async Task<IReadOnlyList<IReadOnlyList<WorkflowStep>>> GetTopologicalLevelsAsync(
        long workflowId, CancellationToken ct = default ) {

        List<WorkflowStep> steps = await dbContext.WorkflowSteps
            .Include( s => s.Dependencies )
            .Include( s => s.Task )
            .Where( s => s.WorkflowId == workflowId )
            .ToListAsync( ct );

        if (steps.Count == 0) {
            return [];
        }

        Dictionary<long, WorkflowStep> stepMap = steps.ToDictionary( s => s.Id );
        Dictionary<long, int> inDegree = steps.ToDictionary(
            s => s.Id,
            _ => 0
        );
        Dictionary<long, List<long>> adjacency = steps.ToDictionary(
            s => s.Id,
            _ => new List<long>( )
        );

        foreach (WorkflowStep step in steps) {
            foreach (WorkflowStepDependency dep in step.Dependencies) {
                if (adjacency.TryGetValue( dep.DependsOnStepId, out List<long>? value )) {
                    value.Add( step.Id );
                    inDegree[step.Id]++;
                }
            }
        }

        List<IReadOnlyList<WorkflowStep>> levels = [];
        List<long> currentLevel = [.. inDegree.Where( kvp => kvp.Value == 0 ).Select( kvp => kvp.Key )];

        while (currentLevel.Count > 0) {
            // Sort within level by Order for determinism
            List<WorkflowStep> levelSteps = [.. currentLevel
                .Select( id => stepMap[id] )
                .OrderBy( s => s.Order )];
            levels.Add( levelSteps );

            List<long> nextLevel = [];
            foreach (long id in currentLevel) {
                foreach (long dependent in adjacency[id]) {
                    inDegree[dependent]--;
                    if (inDegree[dependent] == 0) {
                        nextLevel.Add( dependent );
                    }
                }
            }
            currentLevel = nextLevel;
        }

        int totalProcessed = levels.Sum( l => l.Count );
        return totalProcessed != steps.Count
            ? throw new InvalidOperationException(
                $"Cycle detected in workflow {workflowId}: processed {totalProcessed} of {steps.Count} steps." )
            : (IReadOnlyList<IReadOnlyList<WorkflowStep>>)levels;
    }

    /// <summary>
    /// Atomically applies a batch of step add/update/delete operations and dependency changes
    /// within a single database transaction. Validates the resulting DAG before committing.
    /// </summary>
    /// <param name="workflowId">The workflow to apply changes to.</param>
    /// <param name="request">The batch request containing all operations.</param>
    /// <param name="userId">The user who triggered the batch, or null for system operations.</param>
    /// <param name="changeDescription">Optional human-readable description of the change.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Batch response with temp-to-real ID mappings and validation results.</returns>
    public async Task<WorkflowStepBatchResponse> BatchUpdateStepsAsync(
        long workflowId,
        WorkflowStepBatchRequest request,
        string? userId = null,
        string? changeDescription = null,
        CancellationToken ct = default
    ) {
        if (request.Operations.Count == 0) {
            return new WorkflowStepBatchResponse( true, [], [] );
        }

        // Verify workflow exists
        bool workflowExists = await dbContext.Workflows.AnyAsync(
            w => w.Id == workflowId,
            ct
        );
        if (!workflowExists) {
            return new WorkflowStepBatchResponse( false, [], [$"Workflow with Id={workflowId} was not found."] );
        }

        // Verify all positive StepIds belong to this workflow
        List<long> positiveStepIds = [.. request.Operations
            .Where( o => o.StepId > 0 )
            .Select( o => o.StepId )
            .Distinct( )];

        if (positiveStepIds.Count > 0) {
            List<long> existingIds = await dbContext.WorkflowSteps
                .Where( s => s.WorkflowId == workflowId && positiveStepIds.Contains( s.Id ) )
                .Select( s => s.Id )
                .ToListAsync( ct );

            List<long> invalid = [.. positiveStepIds.Except( existingIds )];
            if (invalid.Count > 0) {
                return new WorkflowStepBatchResponse( false, [],
                    [$"Step IDs do not belong to workflow {workflowId}: {string.Join( ", ", invalid )}"] );
            }
        }

        await using IDbContextTransaction tx = await dbContext.Database.BeginTransactionAsync( ct );
        try {
            Dictionary<long, long> tempToReal = [];
            List<StepIdMapping> mappings = [];

            // ── Phase 1: Process "Add" operations ──
            List<StepBatchOperation> adds = [.. request.Operations.Where( o => string.Equals( o.OperationType, "Add", StringComparison.OrdinalIgnoreCase ) )];

            foreach (StepBatchOperation add in adds) {
                if (!add.IsComposite && (add.TaskId is null || add.TaskId == 0)) {
                    await tx.RollbackAsync( ct );
                    return new WorkflowStepBatchResponse( false, [],
                        [$"Step {add.StepId} requires a task assignment before saving."] );
                }

                WorkflowStep step = new( ) {
                    WorkflowId = workflowId,
                    TaskId = add.IsComposite ? null : add.TaskId,
                    Order = add.Order,
                    ControlStatement = Enum.Parse<ControlStatement>( add.ControlStatement, ignoreCase: true ),
                    ConditionExpression = add.ConditionExpression,
                    MaxIterations = add.MaxIterations,
                    AgentConnectionIdOverride = add.AgentConnectionIdOverride,
                    DependencyMode = ParseDependencyMode( add.DependencyMode ),
                    InputVariableName = add.InputVariableName,
                    OutputVariableName = add.OutputVariableName,
                    IsComposite = add.IsComposite,
                    CompositeType = Enum.Parse<CompositeType>( add.CompositeType, ignoreCase: true ),
                    ChildWorkflowId = add.ChildWorkflowId,
                    IterationVariableName = add.IterationVariableName,
                    CollectionVariableName = add.CollectionVariableName,
                };

                _ = dbContext.WorkflowSteps.Add( step );
                _ = await dbContext.SaveChangesAsync( ct );

                // Auto-create child workflow for composite steps
                if (step.IsComposite && !step.ChildWorkflowId.HasValue) {
                    Workflow childWorkflow = new( ) {
                        IsChildWorkflow = true,
                        Name = "ForEach Inner",
                        ParentStepId = step.Id,
                        Enabled = true,
                    };
                    _ = dbContext.Workflows.Add( childWorkflow );
                    _ = await dbContext.SaveChangesAsync( ct );

                    step.ChildWorkflowId = childWorkflow.Id;
                    _ = await dbContext.SaveChangesAsync( ct );
                }

                tempToReal[add.StepId] = step.Id;
                mappings.Add( new StepIdMapping( add.StepId, step.Id ) );

                if (logger.IsEnabled( LogLevel.Debug )) {
                    logger.LogDebug( "Batch: created step {RealId} (temp {TempId}) in workflow {WorkflowId}.",
                        step.Id.ToString( ),
                        add.StepId.ToString( ),
                        workflowId.ToString( )
                    );
                }
            }

            // Helper to resolve temp IDs to real IDs
            long ResolveId( long id ) => id < 0 && tempToReal.TryGetValue( id, out long real ) ? real : id;

            // ── Phase 2: Process "Update" operations ──
            List<StepBatchOperation> updates = [.. request.Operations.Where( o => string.Equals( o.OperationType, "Update", StringComparison.OrdinalIgnoreCase ) )];

            foreach (StepBatchOperation update in updates) {
                long realId = ResolveId( update.StepId );
                WorkflowStep existing = await dbContext.WorkflowSteps.FirstOrDefaultAsync(
                    s => s.Id == realId,
                    ct
                ) ?? throw new KeyNotFoundException( $"Step {realId} not found during batch update." );

                if (update.TaskId is not null) {
                    existing.TaskId = update.TaskId.Value;
                }
                existing.Order = update.Order;
                existing.ControlStatement = Enum.Parse<ControlStatement>( update.ControlStatement, ignoreCase: true );
                existing.ConditionExpression = update.ConditionExpression;
                existing.MaxIterations = update.MaxIterations;
                existing.AgentConnectionIdOverride = update.AgentConnectionIdOverride;
                existing.DependencyMode = ParseDependencyMode( update.DependencyMode );
                existing.InputVariableName = update.InputVariableName;
                existing.OutputVariableName = update.OutputVariableName;
                existing.IsComposite = update.IsComposite;
                existing.CompositeType = Enum.Parse<CompositeType>( update.CompositeType, ignoreCase: true );
                existing.ChildWorkflowId = update.ChildWorkflowId;
                existing.IterationVariableName = update.IterationVariableName;
                existing.CollectionVariableName = update.CollectionVariableName;

                if (existing.IsComposite && existing.ChildWorkflowId is null) {
                    await tx.RollbackAsync( ct );
                    return new WorkflowStepBatchResponse( false, [],
                        [$"Step {realId}: composite steps require a non-null ChildWorkflowId. " +
                         "Provide ChildWorkflowId or set IsComposite to false."] );
                }
            }

            // ── Phase 3: Process dependency changes ──
            foreach (StepBatchOperation op in request.Operations) {
                if (op.DependencyChanges is null) {
                    continue;
                }

                long realStepId = ResolveId( op.StepId );

                foreach (DependencyBatchItem depChange in op.DependencyChanges) {
                    long realDepId = ResolveId( depChange.DependsOnStepId );

                    if (string.Equals( depChange.OperationType, "Add", StringComparison.OrdinalIgnoreCase )) {
                        if (realStepId == realDepId) {
                            continue;
                        }

                        bool alreadyExists = await dbContext.WorkflowStepDependencies
                            .AnyAsync( d => d.StepId == realStepId && d.DependsOnStepId == realDepId, ct );
                        if (alreadyExists) {
                            continue;
                        }

                        _ = dbContext.WorkflowStepDependencies.Add( new WorkflowStepDependency {
                            StepId = realStepId,
                            DependsOnStepId = realDepId,
                        } );
                    } else if (string.Equals( depChange.OperationType, "Delete", StringComparison.OrdinalIgnoreCase )) {
                        WorkflowStepDependency? dep = await dbContext.WorkflowStepDependencies
                            .FirstOrDefaultAsync( d => d.StepId == realStepId && d.DependsOnStepId == realDepId, ct );
                        if (dep is not null) {
                            _ = dbContext.WorkflowStepDependencies.Remove( dep );
                        }
                    }
                }
            }

            // ── Phase 4: Process "Delete" operations (after deps cleaned up) ──
            List<StepBatchOperation> deletes = [.. request.Operations.Where( o => string.Equals( o.OperationType, "Delete", StringComparison.OrdinalIgnoreCase ) )];

            foreach (StepBatchOperation delete in deletes) {
                long realId = ResolveId( delete.StepId );
                WorkflowStep? step = await dbContext.WorkflowSteps
                    .FirstOrDefaultAsync( s => s.Id == realId, ct );
                if (step is not null) {
                    // Cascade-delete child workflow for composite steps
                    if (step.ChildWorkflowId.HasValue) {
                        Workflow? childWf = await dbContext.Workflows
                            .Include( w => w.Steps )
                            .FirstOrDefaultAsync( w => w.Id == step.ChildWorkflowId.Value, ct );
                        if (childWf is not null) {
                            dbContext.WorkflowSteps.RemoveRange( childWf.Steps );
                            _ = dbContext.Workflows.Remove( childWf );
                        }
                    }

                    // Remove dependencies first
                    List<WorkflowStepDependency> deps = await dbContext.WorkflowStepDependencies
                        .Where( d => d.StepId == realId || d.DependsOnStepId == realId )
                        .ToListAsync( ct );
                    dbContext.WorkflowStepDependencies.RemoveRange( deps );
                    _ = dbContext.WorkflowSteps.Remove( step );
                }
            }

            _ = await dbContext.SaveChangesAsync( ct );

            // ── Phase 5: Validate resulting DAG ──
            try {
                _ = await ValidateDagAsync( workflowId, ct );
            } catch (InvalidOperationException ex) {
                await tx.RollbackAsync( ct );
                return new WorkflowStepBatchResponse( false, [], [ex.Message] );
            }

            await tx.CommitAsync( ct );

            // Create a version snapshot after the batch completes
            Workflow? wfForVersion = await dbContext.Workflows
                .Include( w => w.Steps )
                    .ThenInclude( s => s.Dependencies )
                .Include( w => w.Variables )
                .FirstOrDefaultAsync( w => w.Id == workflowId, ct );

            if (wfForVersion is not null) {
                _ = await versionService.CreateVersionAsync(
                    wfForVersion, userId, changeDescription ?? "Batch step update", ct );
            }

            if (logger.IsEnabled( LogLevel.Information )) {
                logger.LogInformation(
                    "Batch completed for workflow {WorkflowId}: {AddCount} adds, {UpdateCount} updates, {DeleteCount} deletes.",
                    workflowId.ToString( ),
                    adds.Count.ToString( ),
                    updates.Count.ToString( ),
                    deletes.Count.ToString( )
                );
            }

            return new WorkflowStepBatchResponse( true, mappings, [] );
        } catch (Exception ex) when (ex is not InvalidOperationException) {
            await tx.RollbackAsync( ct );
            return new WorkflowStepBatchResponse( false, [], [ex.Message] );
        }
    }

    /// <summary>Validates control flow constraints on a topologically sorted step list.</summary>
    private static void ValidateControlFlow( List<WorkflowStep> sorted ) {
        HashSet<long> processedIds = [];

        foreach (WorkflowStep step in sorted) {
            switch (step.ControlStatement) {
                case ControlStatement.If:
                case ControlStatement.ElseIf:
                case ControlStatement.While:
                    if (string.IsNullOrWhiteSpace( step.ConditionExpression )) {
                        throw new InvalidOperationException(
                            $"Step {step.Id} ({step.ControlStatement}) must have a ConditionExpression." );
                    }
                    break;
            }

            switch (step.ControlStatement) {
                case ControlStatement.Else:
                case ControlStatement.ElseIf: {
                        // Must depend on an If or ElseIf step
                        bool hasIfDep = step.Dependencies.Any( d =>
                        processedIds.Contains( d.DependsOnStepId ) &&
                        sorted.Any( s =>
                            s.Id == d.DependsOnStepId &&
                            s.ControlStatement is ControlStatement.If or ControlStatement.ElseIf ) );
                        if (!hasIfDep) {
                            throw new InvalidOperationException(
                                $"Step {step.Id} ({step.ControlStatement}) must depend on an If or ElseIf step." );
                        }
                        break;
                    }
            }

            switch (step.ControlStatement) {
                case ControlStatement.While:
                case ControlStatement.Do:
                    if (step.MaxIterations <= 0) {
                        throw new InvalidOperationException(
                            $"Step {step.Id} ({step.ControlStatement}) must have MaxIterations > 0." );
                    }
                    break;
            }

            _ = processedIds.Add( step.Id );
        }
    }

    /// <summary>Validates basic workflow properties.</summary>
    private static void ValidateWorkflow( Workflow workflow ) {
        if (string.IsNullOrWhiteSpace( workflow.Name )) {
            throw new System.ComponentModel.DataAnnotations.ValidationException( "Workflow name is required." );
        }
    }

    /// <summary>Parse dependency mode with legacy alias support ("All" → AllSuccess, "Any" → AnySuccess).</summary>
    private static DependencyMode ParseDependencyMode( string value ) {
        return string.IsNullOrWhiteSpace( value )
            ? default
            : string.Equals( value, "All", StringComparison.OrdinalIgnoreCase )
            ? DependencyMode.AllSuccess
            : string.Equals( value, "Any", StringComparison.OrdinalIgnoreCase )
            ? DependencyMode.AnySuccess
            : Enum.Parse<DependencyMode>( value, ignoreCase: true );
    }
}
