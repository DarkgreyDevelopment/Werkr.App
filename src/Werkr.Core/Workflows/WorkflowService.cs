using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Werkr.Data;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Core.Workflows;

/// <summary>
/// Provides CRUD operations for <see cref="Workflow"/> entities including
/// step and dependency management, and DAG validation via Kahn's algorithm.
/// </summary>
/// <param name="dbContext">Database context.</param>
/// <param name="logger">Logger instance.</param>
public sealed class WorkflowService( WerkrDbContext dbContext, ILogger<WorkflowService> logger ) {

    /// <summary>Creates a new workflow.</summary>
    /// <param name="workflow">The workflow to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created workflow with generated Id.</returns>
    /// <exception cref="InvalidOperationException">Workflow name is not unique.</exception>
    public async Task<Workflow> CreateAsync( Workflow workflow, CancellationToken ct = default ) {
        ValidateWorkflow( workflow );

        bool nameExists = await dbContext.Workflows.AnyAsync( w => w.Name == workflow.Name, ct );
        if (nameExists) {
            throw new InvalidOperationException( $"A workflow with name '{workflow.Name}' already exists." );
        }

        _ = dbContext.Workflows.Add( workflow );
        _ = await dbContext.SaveChangesAsync( ct );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation( "Created workflow {WorkflowId} '{WorkflowName}'.",
                workflow.Id.ToString( ), workflow.Name );
        }

        return workflow;
    }

    /// <summary>Updates an existing workflow.</summary>
    /// <param name="workflow">The workflow with updated values.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated workflow.</returns>
    /// <exception cref="KeyNotFoundException">Workflow not found.</exception>
    /// <exception cref="InvalidOperationException">Workflow name is not unique.</exception>
    public async Task<Workflow> UpdateAsync( Workflow workflow, CancellationToken ct = default ) {
        ValidateWorkflow( workflow );

        Workflow existing = await dbContext.Workflows.FirstOrDefaultAsync( w => w.Id == workflow.Id, ct )
            ?? throw new KeyNotFoundException( $"Workflow with Id={workflow.Id} was not found." );

        bool nameConflict = await dbContext.Workflows.AnyAsync(
            w => w.Name == workflow.Name && w.Id != workflow.Id, ct );
        if (nameConflict) {
            throw new InvalidOperationException( $"A workflow with name '{workflow.Name}' already exists." );
        }

        existing.Name = workflow.Name;
        existing.Description = workflow.Description;
        existing.Enabled = workflow.Enabled;
        existing.ScheduleId = workflow.ScheduleId;

        _ = await dbContext.SaveChangesAsync( ct );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation( "Updated workflow {WorkflowId} '{WorkflowName}'.",
                existing.Id.ToString( ), existing.Name );
        }

        return existing;
    }

    /// <summary>Deletes a workflow by ID.</summary>
    /// <param name="workflowId">The workflow identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="KeyNotFoundException">Workflow not found.</exception>
    public async Task DeleteAsync( long workflowId, CancellationToken ct = default ) {
        Workflow existing = await dbContext.Workflows.FirstOrDefaultAsync( w => w.Id == workflowId, ct )
            ?? throw new KeyNotFoundException( $"Workflow with Id={workflowId} was not found." );

        _ = dbContext.Workflows.Remove( existing );
        _ = await dbContext.SaveChangesAsync( ct );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation( "Deleted workflow {WorkflowId} '{WorkflowName}'.",
                workflowId.ToString( ), existing.Name );
        }
    }

    /// <summary>Retrieves a single workflow by ID with steps, dependencies, and tasks.</summary>
    /// <param name="workflowId">The workflow identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The workflow, or null if not found.</returns>
    public async Task<Workflow?> GetByIdAsync( long workflowId, CancellationToken ct = default ) =>
        await dbContext.Workflows
            .Include( w => w.Steps )
                .ThenInclude( s => s.Dependencies )
            .Include( w => w.Steps )
                .ThenInclude( s => s.Task )
            .Include( w => w.Schedule )
            .AsNoTracking( )
            .FirstOrDefaultAsync( w => w.Id == workflowId, ct );

    /// <summary>Retrieves all workflows with steps.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only list of workflows.</returns>
    public async Task<IReadOnlyList<Workflow>> GetAllAsync( CancellationToken ct = default ) =>
        await dbContext.Workflows
            .Include( w => w.Steps )
                .ThenInclude( s => s.Dependencies )
            .Include( w => w.Steps )
                .ThenInclude( s => s.Task )
            .Include( w => w.Schedule )
            .AsNoTracking( )
            .OrderBy( w => w.Name )
            .ToListAsync( ct );

    /// <summary>Adds a step to a workflow.</summary>
    /// <param name="workflowId">The workflow to add the step to.</param>
    /// <param name="step">The step to add.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="KeyNotFoundException">Workflow not found.</exception>
    public async Task<WorkflowStep> AddStepAsync( long workflowId, WorkflowStep step, CancellationToken ct = default ) {
        bool exists = await dbContext.Workflows.AnyAsync( w => w.Id == workflowId, ct );
        if (!exists) {
            throw new KeyNotFoundException( $"Workflow with Id={workflowId} was not found." );
        }

        step.WorkflowId = workflowId;
        _ = dbContext.WorkflowSteps.Add( step );
        _ = await dbContext.SaveChangesAsync( ct );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation( "Added step {StepId} to workflow {WorkflowId}.",
                step.Id.ToString( ), workflowId.ToString( ) );
        }

        return step;
    }

    /// <summary>Removes a step from a workflow.</summary>
    /// <param name="stepId">The step identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="KeyNotFoundException">Step not found.</exception>
    public async Task RemoveStepAsync( long stepId, CancellationToken ct = default ) {
        WorkflowStep step = await dbContext.WorkflowSteps.FirstOrDefaultAsync( s => s.Id == stepId, ct )
            ?? throw new KeyNotFoundException( $"WorkflowStep with Id={stepId} was not found." );

        _ = dbContext.WorkflowSteps.Remove( step );
        _ = await dbContext.SaveChangesAsync( ct );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation( "Removed step {StepId} from workflow {WorkflowId}.",
                stepId.ToString( ), step.WorkflowId.ToString( ) );
        }
    }

    /// <summary>Updates an existing workflow step.</summary>
    /// <param name="step">The step with updated values.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="KeyNotFoundException">Step not found.</exception>
    public async Task<WorkflowStep> UpdateStepAsync( WorkflowStep step, CancellationToken ct = default ) {
        WorkflowStep existing = await dbContext.WorkflowSteps.FirstOrDefaultAsync( s => s.Id == step.Id, ct )
            ?? throw new KeyNotFoundException( $"WorkflowStep with Id={step.Id} was not found." );

        existing.TaskId = step.TaskId;
        existing.Order = step.Order;
        existing.ControlStatement = step.ControlStatement;
        existing.ConditionExpression = step.ConditionExpression;
        existing.MaxIterations = step.MaxIterations;
        existing.AgentConnectionIdOverride = step.AgentConnectionIdOverride;
        existing.DependencyMode = step.DependencyMode;

        _ = await dbContext.SaveChangesAsync( ct );
        return existing;
    }

    /// <summary>Adds a dependency relationship between two steps.</summary>
    /// <param name="stepId">The dependent step.</param>
    /// <param name="dependsOnStepId">The predecessor step.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="KeyNotFoundException">Step not found.</exception>
    /// <exception cref="InvalidOperationException">Dependency already exists or self-reference.</exception>
    public async Task AddStepDependencyAsync( long stepId, long dependsOnStepId, CancellationToken ct = default ) {
        if (stepId == dependsOnStepId) {
            throw new InvalidOperationException( "A step cannot depend on itself." );
        }

        bool stepExists = await dbContext.WorkflowSteps.AnyAsync( s => s.Id == stepId, ct );
        if (!stepExists) {
            throw new KeyNotFoundException( $"WorkflowStep with Id={stepId} was not found." );
        }

        bool depExists = await dbContext.WorkflowSteps.AnyAsync( s => s.Id == dependsOnStepId, ct );
        if (!depExists) {
            throw new KeyNotFoundException( $"WorkflowStep with Id={dependsOnStepId} was not found." );
        }

        bool alreadyExists = await dbContext.WorkflowStepDependencies
            .AnyAsync( d => d.StepId == stepId && d.DependsOnStepId == dependsOnStepId, ct );
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
                stepId.ToString( ), dependsOnStepId.ToString( ) );
        }
    }

    /// <summary>Removes a dependency relationship between two steps.</summary>
    /// <param name="stepId">The dependent step.</param>
    /// <param name="dependsOnStepId">The predecessor step.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="KeyNotFoundException">Dependency not found.</exception>
    public async Task RemoveStepDependencyAsync( long stepId, long dependsOnStepId, CancellationToken ct = default ) {
        WorkflowStepDependency dep = await dbContext.WorkflowStepDependencies
            .FirstOrDefaultAsync( d => d.StepId == stepId && d.DependsOnStepId == dependsOnStepId, ct )
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
    public async Task<IReadOnlyList<WorkflowStep>> ValidateDagAsync( long workflowId, CancellationToken ct = default ) {
        List<WorkflowStep> steps = await dbContext.WorkflowSteps
            .Include( s => s.Dependencies )
            .Where( s => s.WorkflowId == workflowId )
            .ToListAsync( ct );

        if (steps.Count == 0) {
            bool workflowExists = await dbContext.Workflows.AnyAsync( w => w.Id == workflowId, ct );
            return !workflowExists ? throw new KeyNotFoundException( $"Workflow with Id={workflowId} was not found." ) : [];
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
                if (adjacency.ContainsKey( dep.DependsOnStepId )) {
                    adjacency[dep.DependsOnStepId].Add( step.Id );
                    inDegree[step.Id]++;
                }
            }
        }

        // Kahn's algorithm — use a priority queue for Order tiebreaking
        PriorityQueue<long, int> queue = new( );
        foreach (KeyValuePair<long, int> kvp in inDegree) {
            if (kvp.Value == 0) {
                queue.Enqueue( kvp.Key, stepMap[kvp.Key].Order );
            }
        }

        List<WorkflowStep> sorted = [];
        while (queue.Count > 0) {
            long current = queue.Dequeue( );
            sorted.Add( stepMap[current] );

            foreach (long dependent in adjacency[current]) {
                inDegree[dependent]--;
                if (inDegree[dependent] == 0) {
                    queue.Enqueue( dependent, stepMap[dependent].Order );
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
        Dictionary<long, int> inDegree = steps.ToDictionary( s => s.Id, _ => 0 );
        Dictionary<long, List<long>> adjacency = steps.ToDictionary( s => s.Id, _ => new List<long>( ) );

        foreach (WorkflowStep step in steps) {
            foreach (WorkflowStepDependency dep in step.Dependencies) {
                if (adjacency.ContainsKey( dep.DependsOnStepId )) {
                    adjacency[dep.DependsOnStepId].Add( step.Id );
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
}
