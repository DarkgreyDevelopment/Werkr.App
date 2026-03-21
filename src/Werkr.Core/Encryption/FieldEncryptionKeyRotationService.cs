using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Werkr.Core.Security;
using Werkr.Data;
using Werkr.Data.Encryption;

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
            // Determine current key version
            int currentVersion = await GetCurrentKeyVersionAsync( );
            int newVersion = currentVersion + 1;

            _currentKeyName = $"{FieldEncryptionProvider.SecretStoreKey}-v{newVersion}";
            _previousKeyName = currentVersion == 0
                ? FieldEncryptionProvider.SecretStoreKey
                : $"{FieldEncryptionProvider.SecretStoreKey}-v{currentVersion}";

            // Read existing key
            string? existingKey = await secretStore.GetSecretAsync( _previousKeyName ) ?? throw new InvalidOperationException( $"Current encryption key '{_previousKeyName}' not found in secret store." );

            // Generate and store new key
            string newKey = FieldEncryptionProvider.GenerateKey( );
            await secretStore.SetSecretAsync( _currentKeyName, newKey );

            // Re-encrypt in background
            FieldEncryptionProvider oldProvider = new( existingKey );
            FieldEncryptionProvider newProvider = new( newKey );

            await ReEncryptAllAsync( oldProvider, newProvider, ct );

            // Update the primary key reference
            await secretStore.SetSecretAsync( FieldEncryptionProvider.SecretStoreKey, newKey );

            // Remove old versioned key (keep primary updated)
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
        // Check for versioned keys in descending order
        for (int v = 100; v >= 1; v--) {
            string? key = await secretStore.GetSecretAsync( $"{FieldEncryptionProvider.SecretStoreKey}-v{v}" );
            if (key is not null) {
                return v;
            }
        }
        return 0; // Only the unversioned base key exists
    }

    private async Task ReEncryptAllAsync(
        FieldEncryptionProvider oldProvider, FieldEncryptionProvider newProvider, CancellationToken ct
    ) {
        const int BatchSize = 100;

        using IServiceScope scope = scopeFactory.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

        // Count total rows that need re-encryption
        int credCount = await db.Credentials.CountAsync( ct );
        int varCount = await db.WorkflowRunVariables.CountAsync( ct );
        _totalRows = credCount + varCount;

        // Re-encrypt Credential.EncryptedValue
        await ReEncryptBatchedAsync(
            db, db.Credentials, e => e.EncryptedValue,
            ( e, v ) => e.EncryptedValue = v,
            oldProvider, newProvider, BatchSize, ct );

        // Re-encrypt WorkflowRunVariable.Value
        await ReEncryptBatchedAsync(
            db, db.WorkflowRunVariables, e => e.Value,
            ( e, v ) => e.Value = v,
            oldProvider, newProvider, BatchSize, ct );
    }

    private async Task ReEncryptBatchedAsync<TEntity>(
        WerkrDbContext db,
        DbSet<TEntity> dbSet,
        Func<TEntity, string> getEncrypted,
        Action<TEntity, string> setEncrypted,
        FieldEncryptionProvider oldProvider,
        FieldEncryptionProvider newProvider,
        int batchSize,
        CancellationToken ct
    ) where TEntity : class {
        int processed = 0;
        int total = await dbSet.CountAsync( ct );

        while (processed < total) {
            List<TEntity> batch = await dbSet
                .Skip( processed )
                .Take( batchSize )
                .ToListAsync( ct );

            if (batch.Count == 0) {
                break;
            }

            foreach (TEntity entity in batch) {
                string encryptedValue = getEncrypted( entity );
                // Decrypt with old key, re-encrypt with new key
                string? plaintext = oldProvider.Decrypt( encryptedValue );
                if (plaintext is not null) {
                    string? reEncrypted = newProvider.Encrypt( plaintext );
                    if (reEncrypted is not null) {
                        setEncrypted( entity, reEncrypted );
                    }
                }
            }

            _ = await db.SaveChangesAsync( ct );
            processed += batch.Count;
            _ = Interlocked.Add( ref _processedRows, batch.Count );
        }
    }

    [LoggerMessage( Level = LogLevel.Information, Message = "Key rotation to v{Version} completed. {RowCount} rows re-encrypted." )]
    private static partial void LogRotationCompleted( ILogger logger, int version, int rowCount );

    [LoggerMessage( Level = LogLevel.Error, Message = "Key rotation failed." )]
    private static partial void LogRotationFailed( ILogger logger, Exception ex );
}
