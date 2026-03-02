namespace Werkr.Common;

/// <summary>Database provider type.</summary>
public enum DatabaseProvider {
    /// <summary>PostgreSQL database.</summary>
    Postgres,

    /// <summary>SQLite database (typically for agents or local development).</summary>
    SQLite,
}
