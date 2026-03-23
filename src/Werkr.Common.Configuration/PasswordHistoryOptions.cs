namespace Werkr.Common.Configuration;

/// <summary>
/// Configuration options for password history enforcement.
/// Bound to the <c>PasswordHistory</c> configuration section.
/// </summary>
public sealed class PasswordHistoryOptions {
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "PasswordHistory";

    /// <summary>
    /// Number of previous password hashes to retain per user.
    /// Users cannot reuse any of the last <see cref="HistoryCount"/> passwords.
    /// Default: 5.
    /// </summary>
    public int HistoryCount { get; set; } = 5;
}
