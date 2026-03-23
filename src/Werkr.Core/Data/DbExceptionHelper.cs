using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Werkr.Core.Data;

/// <summary>
/// Provider-agnostic helper for classifying <see cref="DbUpdateException"/> causes.
/// Uses reflection to avoid a hard dependency on Npgsql.
/// </summary>
public static class DbExceptionHelper {
    /// <summary>
    /// Returns <c>true</c> when the inner exception indicates a unique-constraint violation
    /// on PostgreSQL (SqlState 23505) or SQLite ("UNIQUE constraint failed").
    /// </summary>
    public static bool IsUniqueConstraintViolation( DbUpdateException ex ) {
        Exception? inner = ex.InnerException;
        if (inner is null) {
            return false;
        }

        // PostgreSQL — inner is Npgsql.PostgresException with SqlState == "23505"
        PropertyInfo? sqlStateProp = inner.GetType( ).GetProperty( "SqlState" );
        if (sqlStateProp is not null) {
            string? sqlState = sqlStateProp.GetValue( inner ) as string;
            return string.Equals( sqlState, "23505", StringComparison.Ordinal );
        }

        // SQLite — inner message contains the constraint-violation text
        return inner.Message.Contains( "UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase );
    }
}
