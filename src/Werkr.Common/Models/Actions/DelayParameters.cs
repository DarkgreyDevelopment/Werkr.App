namespace Werkr.Common.Models.Actions;

/// <summary>Parameters for the Delay action.</summary>
public sealed record DelayParameters {
    /// <summary>Number of seconds to pause workflow execution.</summary>
    public required double Seconds { get; init; }

    /// <summary>Optional reason describing why the delay is needed.</summary>
    public string? Reason { get; init; }
}
