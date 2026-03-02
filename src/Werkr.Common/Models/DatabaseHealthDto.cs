namespace Werkr.Common.Models;

/// <summary>Database diagnostics model.</summary>
public sealed record DatabaseHealthDto(
    string ContextName,
    string ProviderName,
    bool IsConnected,
    int AppliedMigrationCount,
    int PendingMigrationCount,
    List<string> PendingMigrations );
