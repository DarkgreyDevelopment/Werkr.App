using System.ComponentModel.DataAnnotations;
using Werkr.Data.Calendar.Enums;
using Werkr.Data.Entities.Schedule;

namespace Werkr.Data.Calendar.Validation;

/// <summary>
/// Validates a <see cref="HolidayRule"/> entity.
/// Invoked programmatically via <see cref="Validate"/> during rule creation and updates.
/// </summary>
public static class HolidayRuleValidator {

    /// <summary>
    /// Validates the <paramref name="rule"/> and returns <see cref="ValidationResult.Success"/>
    /// if valid, or a <see cref="ValidationResult"/> describing the first violation.
    /// </summary>
    public static ValidationResult? Validate( HolidayRule rule ) {
        // Name is required
        if (string.IsNullOrWhiteSpace( rule.Name )) {
            return new ValidationResult( "Name is required." );
        }

        // Month is required for all rule types
        if (!rule.Month.HasValue) {
            return new ValidationResult( "Month is required." );
        }
        if (rule.Month.Value is < 1 or > 12) {
            return new ValidationResult( "Month must be between 1 and 12." );
        }

        // Rule-type-specific validation
        ValidationResult? typeResult = rule.RuleType switch {
            HolidayRuleType.FixedDate => ValidateFixedDate( rule ),
            HolidayRuleType.NthWeekdayOfMonth => ValidateNthWeekday( rule ),
            HolidayRuleType.LastWeekdayOfMonth => ValidateLastWeekday( rule ),
            _ => new ValidationResult( $"Unknown RuleType: {rule.RuleType}." ),
        };
        if (typeResult != ValidationResult.Success) {
            return typeResult;
        }

        // ObservanceRule: Non-None only valid for FixedDate
        if (rule.ObservanceRule != ObservanceRule.None && rule.RuleType != HolidayRuleType.FixedDate) {
            return new ValidationResult( "ObservanceRule must be None for pattern-based rules (NthWeekdayOfMonth, LastWeekdayOfMonth)." );
        }

        // Time window: all-or-nothing
        bool hasStart = rule.WindowStart.HasValue;
        bool hasEnd = rule.WindowEnd.HasValue;
        bool hasTz = !string.IsNullOrWhiteSpace( rule.WindowTimeZoneId );

        if (hasStart || hasEnd || hasTz) {
            if (!hasStart || !hasEnd || !hasTz) {
                return new ValidationResult( "WindowStart, WindowEnd, and WindowTimeZoneId must all be set or all be null." );
            }
            if (rule.WindowStart!.Value >= rule.WindowEnd!.Value) {
                return new ValidationResult( "WindowStart must be earlier than WindowEnd." );
            }
            try {
                _ = TimeZoneInfo.FindSystemTimeZoneById( rule.WindowTimeZoneId! );
            } catch (TimeZoneNotFoundException) {
                return new ValidationResult( $"WindowTimeZoneId '{rule.WindowTimeZoneId}' is not a valid timezone identifier." );
            }
        }

        // Year bounds
        return rule.YearStart.HasValue && rule.YearEnd.HasValue && rule.YearStart.Value > rule.YearEnd.Value
            ? new ValidationResult( "YearStart must be less than or equal to YearEnd." )
            : ValidationResult.Success;
    }

    private static ValidationResult? ValidateFixedDate( HolidayRule rule ) {
        if (!rule.Day.HasValue) {
            return new ValidationResult( "Day is required for FixedDate rules." );
        }
        if (rule.Day.Value is < 1 or > 31) {
            return new ValidationResult( "Day must be between 1 and 31." );
        }
        // Allow day 29 for Feb (leap years) — validated at computation time
        return rule.Month.HasValue && rule.Day.Value > 29 && rule.Month.Value == 2
            ? new ValidationResult( "February cannot have a day greater than 29." )
            : rule.WeekNumber.HasValue
            ? new ValidationResult( "WeekNumber must not be set for FixedDate rules." )
            : rule.DayOfWeek.HasValue ? new ValidationResult( "DayOfWeek must not be set for FixedDate rules." ) : ValidationResult.Success;
    }

    private static ValidationResult? ValidateNthWeekday( HolidayRule rule ) {
        return !rule.DayOfWeek.HasValue
            ? new ValidationResult( "DayOfWeek is required for NthWeekdayOfMonth rules." )
            : !rule.WeekNumber.HasValue
            ? new ValidationResult( "WeekNumber is required for NthWeekdayOfMonth rules." )
            : rule.WeekNumber.Value is < 1 or > 5
            ? new ValidationResult( "WeekNumber must be between 1 and 5." )
            : rule.Day.HasValue ? new ValidationResult( "Day must not be set for NthWeekdayOfMonth rules." ) : ValidationResult.Success;
    }

    private static ValidationResult? ValidateLastWeekday( HolidayRule rule ) {
        return !rule.DayOfWeek.HasValue
            ? new ValidationResult( "DayOfWeek is required for LastWeekdayOfMonth rules." )
            : rule.Day.HasValue
            ? new ValidationResult( "Day must not be set for LastWeekdayOfMonth rules." )
            : rule.WeekNumber.HasValue
            ? new ValidationResult( "WeekNumber must not be set for LastWeekdayOfMonth rules." )
            : ValidationResult.Success;
    }
}
