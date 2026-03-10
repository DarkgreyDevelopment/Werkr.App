using System.Text.Json;
using System.Threading.Channels;
using Werkr.Common.Models;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Core.Security;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>WatchFile</c> action - monitors a directory for a file matching
/// a glob pattern. Uses <see cref="FileSystemWatcher"/> by default or configurable
/// polling fallback. Includes a stability-window check to confirm the file has
/// finished writing before reporting success.
/// </summary>
public sealed class WatchFileHandler : IActionHandler {

    /// <summary>
    /// Resolves and validates file paths against the agent's allowed-path allowlist.
    /// </summary>
    private readonly IFilePathResolver _resolver;
    /// <summary>
    /// Logger for recording execution errors for this handler.
    /// </summary>
    private readonly ILogger<WatchFileHandler> _logger;
    /// <summary>
    /// Time provider for testable time-dependent logic.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a new <see cref="WatchFileHandler"/>.</summary>
    public WatchFileHandler(
        IFilePathResolver resolver,
        ILogger<WatchFileHandler> logger,
        TimeProvider timeProvider
    ) {
        _resolver = resolver;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public string Action => "WatchFile";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        string? inputVariableValue = null,
        CancellationToken cancellationToken = default
    ) {
        try {
            WatchFileParameters p = parameters.Deserialize<WatchFileParameters>( ActionJson.SerializerOptions )
                ?? throw new ArgumentException( "Failed to deserialize WatchFile parameters." );

            string fullDir = _resolver.ResolveSinglePath( p.Directory );

            if (!Directory.Exists( fullDir )) {
                throw new DirectoryNotFoundException( $"Directory not found: '{fullDir}'" );
            }

            await output.WriteAsync(
                OperatorOutput.Create(
                    LogLevel.Information,
                    $"WatchFile: watching '{fullDir}' for pattern '{p.Pattern}' (timeout: {p.TimeoutSeconds}s, mode: {p.Mode})" ),
                cancellationToken );

            using CancellationTokenSource timeoutCts = new( TimeSpan.FromSeconds( p.TimeoutSeconds ), _timeProvider );
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token );

            string? detectedFile;
            try {
                detectedFile = p.UsePolling
                    ? await PollForFileAsync( fullDir, p, linked.Token )
                    : await WatchForFileAsync( fullDir, p, linked.Token );
            } catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
                // Timeout expired, not user cancellation
                if (p.Mode == WatchFileMode.ExitQuietly) {
                    await output.WriteAsync(
                        OperatorOutput.Create(
                            LogLevel.Information,
                            $"WatchFile: timeout expired. No matching file detected (ExitQuietly mode)." ),
                        cancellationToken );
                    return new ActionOperatorResult( Success: true );
                }

                throw new TimeoutException(
                    $"WatchFile: timeout expired after {p.TimeoutSeconds} second(s) without detecting a matching file." );
            }

            // Stability check
            if (detectedFile is not null) {
                await output.WriteAsync(
                    OperatorOutput.Create(
                        LogLevel.Information,
                        $"WatchFile: file detected: '{detectedFile}'. Starting stability check ({p.StabilitySeconds}s)..." ),
                    cancellationToken );

                bool stable = await CheckStabilityAsync( detectedFile, p, linked.Token );
                if (!stable) {
                    throw new TimeoutException(
                        $"WatchFile: file '{detectedFile}' did not stabilize within the timeout period." );
                }

                await output.WriteAsync(
                    OperatorOutput.Create(
                        LogLevel.Information,
                        $"WatchFile: file '{detectedFile}' is stable." ),
                    cancellationToken );
            }

            string? watchOutputJson = detectedFile is not null
                ? JsonSerializer.Serialize(detectedFile, ActionJson.SerializerOptions) : null;
            return new ActionOperatorResult( Success: true, OutputVariableValue: watchOutputJson );
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogError( ex, "WatchFile action failed" );
            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Error, $"WatchFile failed: {ex.Message}" ),
                cancellationToken );
            return new ActionOperatorResult( Success: false, Exception: ex );
        }
    }

    /// <summary>
    /// Uses <see cref="FileSystemWatcher"/> to detect a matching file.
    /// Checks for pre-existing files first, then waits for FSW events.
    /// </summary>
    private async Task<string?> WatchForFileAsync(
        string directory,
        WatchFileParameters p,
        CancellationToken cancellationToken
    ) {
        // Check for pre-existing matching files first
        string? existing = FindExistingMatch( directory, p.Pattern );
        if (existing is not null) {
            return existing;
        }

        TaskCompletionSource<string> tcs = new( TaskCreationOptions.RunContinuationsAsynchronously );

        using FileSystemWatcher watcher = new( directory, p.Pattern ) {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
            EnableRaisingEvents = true,
        };

        void OnFileEvent( object sender, FileSystemEventArgs e ) => _ = tcs.TrySetResult( e.FullPath );

        watcher.Created += OnFileEvent;
        watcher.Changed += OnFileEvent;

        await using CancellationTokenRegistration ctr = cancellationToken.Register(
            ( ) => tcs.TrySetCanceled( cancellationToken ) );

        return await tcs.Task;
    }

    /// <summary>
    /// Uses periodic polling to detect a matching file.
    /// </summary>
    private async Task<string?> PollForFileAsync(
        string directory,
        WatchFileParameters p,
        CancellationToken cancellationToken
    ) {
        TimeSpan interval = TimeSpan.FromMilliseconds( p.PollIntervalMs );

        while (!cancellationToken.IsCancellationRequested) {
            string? match = FindExistingMatch( directory, p.Pattern );
            if (match is not null) {
                return match;
            }

            await Task.Delay( interval, _timeProvider, cancellationToken );
        }

        cancellationToken.ThrowIfCancellationRequested( );
        return null;
    }

    /// <summary>
    /// Finds the first file in the directory matching the given glob pattern.
    /// </summary>
    private static string? FindExistingMatch( string directory, string pattern ) {
        foreach (string file in Directory.EnumerateFiles( directory, pattern )) {
            return file;
        }
        return null;
    }

    /// <summary>
    /// Waits until the detected file's size and last-write time remain unchanged
    /// for <see cref="WatchFileParameters.StabilitySeconds"/>.
    /// </summary>
    private async Task<bool> CheckStabilityAsync(
        string filePath,
        WatchFileParameters p,
        CancellationToken cancellationToken
    ) {
        TimeSpan interval = TimeSpan.FromMilliseconds( p.PollIntervalMs );
        TimeSpan stabilityWindow = TimeSpan.FromSeconds( p.StabilitySeconds );
        DateTimeOffset stableStartTime = _timeProvider.GetUtcNow( );

        long lastSize = -1;
        DateTimeOffset lastWriteTime = DateTimeOffset.MinValue;

        while (!cancellationToken.IsCancellationRequested) {
            if (!File.Exists( filePath )) {
                // File disappeared - reset stability window
                stableStartTime = _timeProvider.GetUtcNow( );
                await Task.Delay( interval, _timeProvider, cancellationToken );
                continue;
            }

            FileInfo fi = new( filePath );
            long currentSize = fi.Length;
            DateTimeOffset currentWriteTime = fi.LastWriteTimeUtc;

            if (currentSize != lastSize || currentWriteTime != lastWriteTime) {
                // File changed - reset stability window
                lastSize = currentSize;
                lastWriteTime = currentWriteTime;
                stableStartTime = _timeProvider.GetUtcNow( );
            }

            DateTimeOffset now = _timeProvider.GetUtcNow( );
            if (now - stableStartTime >= stabilityWindow) {
                return true;
            }

            await Task.Delay( interval, _timeProvider, cancellationToken );
        }

        cancellationToken.ThrowIfCancellationRequested( );
        return false;
    }
}
