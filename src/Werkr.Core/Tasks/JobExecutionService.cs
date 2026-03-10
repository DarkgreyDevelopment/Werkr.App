using Werkr.Data;
using Werkr.Data.Entities.Tasks;

namespace Werkr.Core.Tasks;

/// <summary>
/// Provides job query and history operations.
/// <para>
/// Task execution is now handled exclusively by the agent-side
/// <c>ScheduleEvaluatorService</c> and <c>WorkflowExecutionService</c>.
/// This service retains only the read-side operations (job history,
/// recent jobs, output retrieval) consumed by the API endpoints.
/// </para>
/// </summary>
/// <param name="dbContext">Database context.</param>
/// <param name="outputWriter">Writes job output to disk.</param>
public sealed class JobExecutionService(
    WerkrDbContext dbContext,
    JobOutputWriter outputWriter
) {

    /// <summary>
    /// Retrieves job history for a task, ordered by most recent first.
    /// </summary>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="limit">Maximum number of jobs to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of jobs for the specified task.</returns>
    public async Task<IReadOnlyList<WerkrJob>> GetJobHistoryAsync(
        long taskId,
        int limit = 50,
        CancellationToken ct = default
    ) =>
        await dbContext.Jobs.AsNoTracking( )
            .Include( j => j.Task )
            .Include( j => j.AgentConnection )
            .Where( j => j.TaskId == taskId )
            .OrderByDescending( j => j.StartTime )
            .Take( limit )
            .ToListAsync( ct );

    /// <summary>
    /// Retrieves recent jobs across all tasks, ordered by most recent first.
    /// Supports optional filtering by success status and date range.
    /// </summary>
    /// <param name="success">Optional filter — <c>true</c> for successful, <c>false</c> for failed, <c>null</c> for
    /// all.</param>
    /// <param name="since">Optional start of date/time window (UTC).</param>
    /// <param name="until">Optional end of date/time window (UTC).</param>
    /// <param name="limit">Maximum number of jobs to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of recent jobs.</returns>
    public async Task<IReadOnlyList<WerkrJob>> GetRecentJobsAsync(
        bool? success = null,
        DateTime? since = null,
        DateTime? until = null,
        int limit = 50,
        CancellationToken ct = default
    ) {
        IQueryable<WerkrJob> query = dbContext.Jobs.AsNoTracking( )
            .Include( j => j.Task )
            .Include( j => j.AgentConnection );

        if (success.HasValue) {
            query = query.Where( j => j.Success == success.Value );
        }

        if (since.HasValue) {
            query = query.Where( j => j.StartTime >= since.Value );
        }

        if (until.HasValue) {
            query = query.Where( j => j.StartTime <= until.Value );
        }

        return await query
            .OrderByDescending( j => j.StartTime )
            .Take( limit )
            .ToListAsync( ct );
    }

    /// <summary>
    /// Retrieves a single job by ID.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The job, or null if not found.</returns>
    public async Task<WerkrJob?> GetJobAsync(
        Guid jobId,
        CancellationToken ct = default
    ) =>
        await dbContext.Jobs.AsNoTracking( ).FirstOrDefaultAsync(
            j => j.Id == jobId,
            ct
        );

    /// <summary>
    /// Retrieves the full output for a job from disk.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The full output text, or null if the file does not exist.</returns>
    public async Task<string?> GetJobOutputAsync(
        Guid jobId,
        CancellationToken ct = default
    ) =>
        await outputWriter.ReadFullOutputAsync(
            jobId,
            ct
        );
}
