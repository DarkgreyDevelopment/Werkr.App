namespace Werkr.Common.Configuration;

/// <summary>
/// Configuration DTO for PowerShell operator settings.
/// Nested under <c>Agent:PowerShell</c> in configuration.
/// </summary>
public sealed class PowerShellSettings {
    /// <summary>
    /// Buffer width in columns for the custom PSHost. Controls how
    /// <c>Format-Table</c>, <c>Format-List</c>, and other formatting
    /// cmdlets wrap output. Default is 150.
    /// </summary>
    public int BufferWidth { get; set; } = 150;
}
