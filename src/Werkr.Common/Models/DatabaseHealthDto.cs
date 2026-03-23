namespace Werkr.Common.Models;

/// <summary>
/// Diagnostics snapshot of database connectivity and migration status
/// returned by the health endpoint.
/// </summary>
public sealed record DatabaseHealthDto(
    string ContextName,
    string ProviderName,
    bool IsConnected,
    int AppliedMigrationCount,
    int PendingMigrationCount,
    List<string> PendingMigrations
);
