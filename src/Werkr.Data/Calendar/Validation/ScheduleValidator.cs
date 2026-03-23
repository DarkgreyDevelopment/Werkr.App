using System.ComponentModel.DataAnnotations;
using Werkr.Data.Calendar.Models;

namespace Werkr.Data.Calendar.Validation;

/// <summary>
/// Validates a <see cref="Schedule"/> composite model.
/// Invoked programmatically via <see cref="Validate"/> during schedule creation and updates.
/// </summary>
public static class ScheduleValidator {

    /// <summary>
    /// Validates the <paramref name="schedule"/> composite model and returns a <see cref="ValidationResult"/>
    /// or <see cref="ValidationResult.Success"/> if valid.
    /// </summary>
    public static ValidationResult? Validate( Schedule schedule ) {
        // StartDateTime is required
        if (schedule.StartDateTime is null) {
            return new ValidationResult( "StartDateTime is required." );
        }

        // At most one recurrence type
        int recurrenceCount = 0;
        if (schedule.DailyRecurrence is not null) {
            recurrenceCount++;
        }

        if (schedule.WeeklyRecurrence is not null) {
            recurrenceCount++;
        }

        if (schedule.MonthlyRecurrence is not null) {
            recurrenceCount++;
        }

        if (recurrenceCount > 1) {
            return new ValidationResult( "At most one recurrence type may be set (daily, weekly, or monthly)." );
        }

        // Daily validation
        if (schedule.DailyRecurrence is not null) {
            if (schedule.DailyRecurrence.DayInterval < 1) {
                return new ValidationResult( "DailyRecurrence.DayInterval must be >= 1." );
            }
        }

        // Weekly validation
        if (schedule.WeeklyRecurrence is not null) {
            if (schedule.WeeklyRecurrence.WeekInterval < 1) {
                return new ValidationResult( "WeeklyRecurrence.WeekInterval must be >= 1." );
            }
            if (schedule.WeeklyRecurrence.DaysOfWeek == 0) {
                return new ValidationResult( "WeeklyRecurrence.DaysOfWeek must not be None." );
            }
        }

        // Monthly validation
        if (schedule.MonthlyRecurrence is not null) {
            ValidationResult? monthlyResult = MonthlyRecurrenceValidator.Validate( schedule.MonthlyRecurrence );
            if (monthlyResult != ValidationResult.Success) {
                return monthlyResult;
            }
        }

        // RepeatOptions validation
        if (schedule.RepeatOptions is not null) {
            if (schedule.RepeatOptions.RepeatIntervalMinutes < 1) {
                return new ValidationResult( "RepeatOptions.RepeatIntervalMinutes must be >= 1." );
            }
        }

        return ValidationResult.Success;
    }
}
