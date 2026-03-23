namespace Werkr.Data.Entities.Workflows;

/// <summary>
/// Identifies the origin of a workflow run variable value.
/// </summary>
public enum VariableSource {

    /// <summary>Seeded from the design-time default value on the <see cref="WorkflowVariable"/>.</summary>
    Default = 0,

    /// <summary>Provided as a trigger parameter when the workflow was executed.</summary>
    ManualInput = 1,

    /// <summary>Produced by a workflow step during execution.</summary>
    StepOutput = 2,

    /// <summary>Manually edited for re-execution (future use).</summary>
    ReExecutionEdit = 3,

    /// <summary>Injected by a trigger (file monitor, webhook, etc.).</summary>
    TriggerContext = 4,
}
