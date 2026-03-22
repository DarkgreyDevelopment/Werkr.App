using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Werkr.Core.Security;
using Werkr.Data;
using Werkr.Data.Encryption;
using Werkr.Data.Entities.Configuration;
using Werkr.Data.Entities.Registration;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Core.Encryption;

/// <summary>
/// Result of a key rotation status check.
/// </summary>
public sealed record RotationStatus(
    bool InProgress,
    int TotalRows,
    int ProcessedRows,
    double PercentComplete,
    string? CurrentKeyName,
    string? PreviousKeyName
);

/// <summary>
/// Service for rotating the field-level encryption key used by <see cref="FieldEncryptionProvider"/>.
/// Generates a new key, stores it in the OS secret store, re-encrypts existing data in batches,
/// and removes the old key after completion.
/// <para>
/// Re-encryption reads entity values through EF Core (value converters auto-decrypt with the old key)
/// and writes re-encrypted ciphertext using raw SQL to bypass converters on the write path.
/// </para>
/// </summary>
public sealed partial class FieldEncryptionKeyRotationService(
    ISecretStore secretStore,
    IServiceScopeFactory scopeFactory,
    ILogger<FieldEncryptionKeyRotationService> logger
) : IFieldEncryptionKeyRotationService {

    private volatile bool _inProgress;
    private int _totalRows;
    private int _processedRows;
    private string? _currentKeyName;
    private string? _previousKeyName;

    /// <inheritdoc/>
    public async Task<RotationStatus> StartRotationAsync( CancellationToken ct ) {
        if (_inProgress) {
            return GetStatus( );
        }

        _inProgress = true;
        _processedRows = 0;
        _totalRows = 0;

        try {
            int currentVersion = await GetCurrentKeyVersionAsync( );
            int newVersion = currentVersion + 1;

            _currentKeyName = $"{FieldEncryptionProvider.SecretStoreKey}-v{newVersion}";
            _previousKeyName = currentVersion == 0
                ? FieldEncryptionProvider.SecretStoreKey
                : $"{FieldEncryptionProvider.SecretStoreKey}-v{currentVersion}";

            string? existingKey = await secretStore.GetSecretAsync( _previousKeyName )
                ?? throw new InvalidOperationException( $"Current encryption key '{_previousKeyName}' not found in secret store." );

            string newKey = FieldEncryptionProvider.GenerateKey( );
            await secretStore.SetSecretAsync( _currentKeyName, newKey );

            FieldEncryptionProvider newProvider = new( newKey );

            await ReEncryptAllAsync( newProvider, ct );

            // Update the primary key reference after all data is re-encrypted
            await secretStore.SetSecretAsync( FieldEncryptionProvider.SecretStoreKey, newKey );

            if (_previousKeyName != FieldEncryptionProvider.SecretStoreKey) {
                await secretStore.DeleteSecretAsync( _previousKeyName );
            }

            LogRotationCompleted( logger, newVersion, _processedRows );
        } catch (Exception ex) {
            LogRotationFailed( logger, ex );
            throw;
        } finally {
            _inProgress = false;
        }

        return GetStatus( );
    }

    /// <inheritdoc/>
    public Task<RotationStatus> GetStatusAsync( CancellationToken ct ) =>
        Task.FromResult( GetStatus( ) );

    private RotationStatus GetStatus( ) => new(
        InProgress: _inProgress,
        TotalRows: _totalRows,
        ProcessedRows: _processedRows,
        PercentComplete: _totalRows > 0 ? Math.Round( (double)_processedRows / _totalRows * 100, 1 ) : 0,
        CurrentKeyName: _currentKeyName,
        PreviousKeyName: _previousKeyName
    );

    private async Task<int> GetCurrentKeyVersionAsync( ) {
        for (int v = 100; v >= 1; v--) {
            string? key = await secretStore.GetSecretAsync( $"{FieldEncryptionProvider.SecretStoreKey}-v{v}" );
            if (key is not null) {
                return v;
            }
        }
        return 0;
    }

    /// <summary>
    /// Re-encrypts all encrypted fields across all entity types.
    /// Reads through EF (converters auto-decrypt with old key → plaintext),
    /// encrypts with the new key, and writes via raw SQL (bypasses converters).
    /// </summary>
    private async Task ReEncryptAllAsync( FieldEncryptionProvider newProvider, CancellationToken ct ) {
        const int BatchSize = 100;

        using IServiceScope scope = scopeFactory.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

        int credCount = await db.Credentials.CountAsync( ct );
        int varCount = await db.WorkflowRunVariables.CountAsync( ct );
        int connCount = await db.RegisteredConnections.CountAsync( ct );
        int configCount = await db.ConfigurationEntries.CountAsync( ct );
        _totalRows = credCount + varCount + connCount + configCount;

        await ReEncryptCredentialsAsync( db, newProvider, BatchSize, ct );
        await ReEncryptWorkflowRunVariablesAsync( db, newProvider, BatchSize, ct );
        await ReEncryptConfigurationEntriesAsync( db, newProvider, BatchSize, ct );
        await ReEncryptRegisteredConnectionsAsync( db, newProvider, BatchSize, ct );
    }

    private async Task ReEncryptCredentialsAsync(
        WerkrDbContext db, FieldEncryptionProvider newProvider, int batchSize, CancellationToken ct
    ) {
        ColumnNames cols = GetColumnNames<Credential>( db,
            nameof( Credential.Id ), nameof( Credential.EncryptedValue ) );
        int processed = 0;

        while (true) {
            var batch = await db.Credentials
                .OrderBy( e => e.Id )
                .Skip( processed )
                .Take( batchSize )
                .Select( e => new { e.Id, e.EncryptedValue } )
                .ToListAsync( ct );

            if (batch.Count == 0) { break; }

            foreach (var item in batch) {
                // item.EncryptedValue is plaintext (auto-decrypted by converter)
                string newCipher = newProvider.Encrypt( item.EncryptedValue )!;
                await UpdateRawAsync( db, cols.Table, cols.Columns[1], newCipher, cols.Columns[0], item.Id, ct );
            }

            processed += batch.Count;
            _ = Interlocked.Add( ref _processedRows, batch.Count );
        }
    }

    private async Task ReEncryptWorkflowRunVariablesAsync(
        WerkrDbContext db, FieldEncryptionProvider newProvider, int batchSize, CancellationToken ct
    ) {
        ColumnNames cols = GetColumnNames<WorkflowRunVariable>( db,
            nameof( WorkflowRunVariable.Id ), nameof( WorkflowRunVariable.Value ) );
        int processed = 0;

        while (true) {
            var batch = await db.WorkflowRunVariables
                .OrderBy( e => e.Id )
                .Skip( processed )
                .Take( batchSize )
                .Select( e => new { e.Id, e.Value } )
                .ToListAsync( ct );

            if (batch.Count == 0) { break; }

            foreach (var item in batch) {
                string newCipher = newProvider.Encrypt( item.Value )!;
                await UpdateRawAsync( db, cols.Table, cols.Columns[1], newCipher, cols.Columns[0], item.Id, ct );
            }

            processed += batch.Count;
            _ = Interlocked.Add( ref _processedRows, batch.Count );
        }
    }

    private async Task ReEncryptConfigurationEntriesAsync(
        WerkrDbContext db, FieldEncryptionProvider newProvider, int batchSize, CancellationToken ct
    ) {
        ColumnNames cols = GetColumnNames<ConfigurationEntry>( db,
            nameof( ConfigurationEntry.Id ),
            nameof( ConfigurationEntry.Value ),
            nameof( ConfigurationEntry.DefaultValue ) );
        int processed = 0;

        while (true) {
            var batch = await db.ConfigurationEntries
                .OrderBy( e => e.Id )
                .Skip( processed )
                .Take( batchSize )
                .Select( e => new { e.Id, e.Value, e.DefaultValue } )
                .ToListAsync( ct );

            if (batch.Count == 0) { break; }

            foreach (var item in batch) {
                string newValue = newProvider.Encrypt( item.Value )!;
                string? newDefault = item.DefaultValue is not null ? newProvider.Encrypt( item.DefaultValue ) : null;

                string sql = $"""UPDATE "{cols.Table}" SET "{cols.Columns[1]}" = @p0, "{cols.Columns[2]}" = @p1 WHERE "{cols.Columns[0]}" = @p2""";
                _ = await db.Database.ExecuteSqlRawAsync( sql, [newValue, (object?)newDefault ?? DBNull.Value, item.Id], ct );
            }

            processed += batch.Count;
            _ = Interlocked.Add( ref _processedRows, batch.Count );
        }
    }

    private async Task ReEncryptRegisteredConnectionsAsync(
        WerkrDbContext db, FieldEncryptionProvider newProvider, int batchSize, CancellationToken ct
    ) {
        ColumnNames cols = GetColumnNames<RegisteredConnection>( db,
            nameof( RegisteredConnection.Id ),
            nameof( RegisteredConnection.OutboundApiKey ),
            nameof( RegisteredConnection.SharedKey ),
            nameof( RegisteredConnection.PreviousSharedKey ),
            nameof( RegisteredConnection.LocalPrivateKey ) );
        int processed = 0;

        while (true) {
            var batch = await db.RegisteredConnections
                .OrderBy( e => e.Id )
                .Skip( processed )
                .Take( batchSize )
                .Select( e => new {
                    e.Id,
                    e.OutboundApiKey,
                    e.SharedKey,
                    e.PreviousSharedKey,
                    e.LocalPrivateKey
                } )
                .ToListAsync( ct );

            if (batch.Count == 0) { break; }

            foreach (var conn in batch) {
                // OutboundApiKey: string → encrypted string
                string? newApiKey = conn.OutboundApiKey is not null
                    ? newProvider.Encrypt( conn.OutboundApiKey )
                    : null;

                // SharedKey: byte[] → encrypted base64 string
                string? newSharedKey = conn.SharedKey is not null
                    ? newProvider.EncryptBytes( conn.SharedKey )
                    : null;

                // PreviousSharedKey: byte[]? → encrypted base64 string (nullable)
                string? newPrevKey = conn.PreviousSharedKey is not null
                    ? newProvider.EncryptBytes( conn.PreviousSharedKey )
                    : null;

                // LocalPrivateKey: RSAParameters → JSON → encrypted string
                string? newPrivateKey = null;
                if (conn.LocalPrivateKey.D is not null) {
                    string json = JsonSerializer.Serialize( conn.LocalPrivateKey );
                    newPrivateKey = newProvider.Encrypt( json );
                }

                string sql = $"""
                    UPDATE "{cols.Table}"
                    SET "{cols.Columns[1]}" = @p0,
                        "{cols.Columns[2]}" = @p1,
                        "{cols.Columns[3]}" = @p2,
                        "{cols.Columns[4]}" = @p3
                    WHERE "{cols.Columns[0]}" = @p4
                    """;

                _ = await db.Database.ExecuteSqlRawAsync( sql, [
                    (object?) newApiKey ?? DBNull.Value,
                    (object?) newSharedKey ?? DBNull.Value,
                    (object?) newPrevKey ?? DBNull.Value,
                    (object?) newPrivateKey ?? DBNull.Value,
                    conn.Id
                ], ct );
            }

            processed += batch.Count;
            _ = Interlocked.Add( ref _processedRows, batch.Count );
        }
    }

    /// <summary>
    /// Executes a single-column UPDATE via raw SQL, bypassing EF value converters.
    /// </summary>
    private static async Task UpdateRawAsync<TId>(
        WerkrDbContext db, string table, string column, string newValue, string idColumn, TId id, CancellationToken ct
    ) {
        string sql = $"""UPDATE "{table}" SET "{column}" = @p0 WHERE "{idColumn}" = @p1""";
        _ = await db.Database.ExecuteSqlRawAsync( sql, [newValue, id!], ct );
    }

    /// <summary>
    /// Resolves actual database table and column names from EF Core model metadata.
    /// Handles provider-specific naming conventions (e.g. PostgreSQL snake_case).
    /// </summary>
    private static ColumnNames GetColumnNames<TEntity>( WerkrDbContext db, params string[] propertyNames ) {
        IEntityType entityType = db.Model.FindEntityType( typeof( TEntity ) )
            ?? throw new InvalidOperationException( $"Entity type {typeof( TEntity ).Name} not found in model." );

        string tableName = entityType.GetTableName( )
            ?? throw new InvalidOperationException( $"Table name not found for {typeof( TEntity ).Name}." );

        string[] columns = new string[propertyNames.Length];
        for (int i = 0; i < propertyNames.Length; i++) {
            IProperty prop = entityType.FindProperty( propertyNames[i] )
                ?? throw new InvalidOperationException( $"Property {propertyNames[i]} not found on {typeof( TEntity ).Name}." );

            StoreObjectIdentifier storeObject = StoreObjectIdentifier.Table( tableName );
            columns[i] = prop.GetColumnName( storeObject )
                ?? prop.GetColumnName( )
                ?? propertyNames[i];
        }

        return new ColumnNames( tableName, columns );
    }

    private readonly record struct ColumnNames( string Table, string[] Columns );

    [LoggerMessage( Level = LogLevel.Information, Message = "Key rotation to v{Version} completed. {RowCount} rows re-encrypted." )]
    private static partial void LogRotationCompleted( ILogger logger, int version, int rowCount );

    [LoggerMessage( Level = LogLevel.Error, Message = "Key rotation failed." )]
    private static partial void LogRotationFailed( ILogger logger, Exception ex );
}
