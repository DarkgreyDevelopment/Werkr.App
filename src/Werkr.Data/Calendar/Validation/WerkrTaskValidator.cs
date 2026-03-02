using System.ComponentModel.DataAnnotations;

using Werkr.Data.Entities.Tasks;

namespace Werkr.Data.Calendar.Validation;

/// <summary>
/// Validates a <see cref="WerkrTask"/> entity.
/// Invoked programmatically via <see cref="Validate"/> during task creation and updates.
/// </summary>
public static class WerkrTaskValidator {
    /// <summary>
    /// Validates the <paramref name="task"/> and returns a <see cref="ValidationResult"/>
    /// or <see cref="ValidationResult.Success"/> if valid.
    /// </summary>
    public static ValidationResult? Validate( WerkrTask task ) {
        // Content must not be empty or whitespace
        if (string.IsNullOrWhiteSpace( task.Content )) {
            return new ValidationResult( "WerkrTask.Content must not be empty or whitespace." );
        }

        // ActionType must be a defined enum value
        if (!Enum.IsDefined( task.ActionType )) {
            return new ValidationResult(
                $"WerkrTask.ActionType must be a valid TaskActionType value. Found: {(int)task.ActionType}." );
        }

        // A task with neither ScheduleId nor WorkflowId is valid (ad-hoc-only task — Decision #43)

        return ValidationResult.Success;
    }
}
