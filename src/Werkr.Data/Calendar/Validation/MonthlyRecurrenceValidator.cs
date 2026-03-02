using System.ComponentModel.DataAnnotations;

using Werkr.Data.Entities.Schedule;

namespace Werkr.Data.Calendar.Validation;

/// <summary>
/// Validates a <see cref="MonthlyRecurrence"/> entity.
/// Invoked programmatically via <see cref="Validate"/> during schedule validation.
/// </summary>
public static class MonthlyRecurrenceValidator {
    /// <summary>
    /// Validates the <paramref name="recurrence"/> and returns a <see cref="ValidationResult"/>
    /// or <see cref="ValidationResult.Success"/> if valid.
    /// </summary>
    public static ValidationResult? Validate( MonthlyRecurrence recurrence ) {
        // MonthsOfYear must not be None
        if (recurrence.MonthsOfYear == 0) {
            return new ValidationResult( "MonthlyRecurrence.MonthsOfYear must not be None." );
        }

        bool hasDayNumbers = recurrence.DayNumbers is { Length: > 0 };
        bool hasWeekNumber = recurrence.WeekNumber is not null and not 0;
        bool hasDaysOfWeek = recurrence.DaysOfWeek is not null and not 0;

        // Must set either DayNumbers OR (WeekNumber + DaysOfWeek), but not both
        if (hasDayNumbers && (hasWeekNumber || hasDaysOfWeek)) {
            return new ValidationResult(
                "MonthlyRecurrence: DayNumbers cannot be combined with WeekNumber or DaysOfWeek. " +
                "Use day-of-month mode (DayNumbers) or week-and-day mode (WeekNumber + DaysOfWeek), not both." );
        }

        if (!hasDayNumbers && !hasWeekNumber && !hasDaysOfWeek) {
            return new ValidationResult(
                "MonthlyRecurrence: Either DayNumbers or (WeekNumber + DaysOfWeek) must be set." );
        }

        // WeekNumber and DaysOfWeek must be set together
        if (hasWeekNumber != hasDaysOfWeek) {
            return new ValidationResult(
                "MonthlyRecurrence: WeekNumber and DaysOfWeek must be set together (both or neither)." );
        }

        // Validate DayNumbers values
        if (hasDayNumbers) {
            foreach (int day in recurrence.DayNumbers!) {
                if (day == 0) {
                    return new ValidationResult( "MonthlyRecurrence.DayNumbers values must not be 0." );
                }
                if (day is < -31 or > 31) {
                    return new ValidationResult(
                        $"MonthlyRecurrence.DayNumbers values must be between -31 and 31. Found: {day}." );
                }
            }
        }

        return ValidationResult.Success;
    }
}
