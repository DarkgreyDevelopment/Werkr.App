namespace Werkr.Core.Scheduling;

/// <summary>
/// Result of schedule occurrence calculation, including both kept and suppressed occurrences.
/// </summary>
/// <param name="Occurrences">The occurrences that passed the holiday filter (or all occurrences if no filter).</param>
/// <param name="Suppressed">Occurrences that were filtered out by the holiday calendar.</param>
public sealed record ScheduleOccurrenceResult(
    IReadOnlyList<DateTime> Occurrences,
    IReadOnlyList<SuppressedOccurrence> Suppressed
);
