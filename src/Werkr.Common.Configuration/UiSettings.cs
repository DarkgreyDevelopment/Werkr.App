namespace Werkr.Common.Configuration;

/// <summary>
/// Configuration DTO for UI-specific settings such as polling intervals.
/// </summary>
public sealed class UiSettings {
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Ui";

    /// <summary>Default polling interval in seconds for dashboard and list views.</summary>
    public int PollingIntervalSeconds { get; set; } = 30;

    /// <summary>Default polling interval in seconds for workflow run detail views.</summary>
    public int RunDetailPollingIntervalSeconds { get; set; } = 15;
}
