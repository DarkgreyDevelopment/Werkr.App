using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Werkr.Common.Models;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;

namespace Werkr.Agent.Operators;

/// <summary>
/// Built-in action operator - dispatches <see cref="ActionDescriptor"/> requests
/// to the appropriate <see cref="IActionHandler"/> via a string-keyed registry.
/// Follows the same channel-based streaming pattern as <see cref="PwshOperator"/>
/// and <see cref="SystemShellOperator"/>.
/// </summary>
public sealed partial class ActionOperator : IActionOperator {

    /// <summary>
    /// The well-known action names that must have registered handlers at startup.
    /// Adding a new action: handler class + parameter record + one string here.
    /// </summary>
    internal static readonly string[] DefaultExpectedActions = [
        "CopyFile",
        "MoveFile",
        "RenameFile",
        "DeleteFile",
        "CreateFile",
        "CreateDirectory",
        "TestExists",
        "ClearContent",
        "WriteContent",
        "StartProcess",
        "StopProcess",
        "Delay",
        "GetFileInfo",
        "ReadContent",
        "ListDirectory",
        "FindReplace",
        "CompressArchive",
        "ExpandArchive",
        "WatchFile",
        "TransformJson",
        "HttpRequest",
        "DownloadFile",
        "TestConnection",
        "SendWebhook",
        "SendEmail",
        "UploadFile",
    ];

    /// <summary>
    /// Dictionary mapping action names (case-insensitive) to their corresponding handler instances.
    /// </summary>
    private readonly Dictionary<string, IActionHandler> _handlers;
    /// <summary>
    /// Options monitor providing the current <see cref="ActionOperatorConfiguration"/>
    /// including default timeout settings for action execution.
    /// </summary>
    private readonly IOptionsMonitor<ActionOperatorConfiguration> _options;
    /// <summary>
    /// Logger for recording action execution lifecycle events and errors.
    /// </summary>
    private readonly ILogger<ActionOperator> _logger;

    /// <summary>
    /// Creates a new <see cref="ActionOperator"/> and builds the handler registry.
    /// </summary>
    /// <param name="handlers">All registered action handlers from DI.</param>
    /// <param name="options">Configuration for action operator behavior (e.g. timeout).</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="expectedActions">
    /// Optional list of expected action names to validate at startup. When <see langword="null"/>,
    /// defaults to <see cref="DefaultExpectedActions"/>. Pass an empty collection to
    /// skip validation (useful in unit tests with partial handler sets).
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when duplicate action names are detected or expected actions are missing.
    /// </exception>
    public ActionOperator(
        IEnumerable<IActionHandler> handlers,
        IOptionsMonitor<ActionOperatorConfiguration> options,
        ILogger<ActionOperator> logger,
        IEnumerable<string>? expectedActions = null
    ) {
        _options = options;
        _logger = logger;
        _handlers = new( StringComparer.OrdinalIgnoreCase );

        foreach (IActionHandler handler in handlers) {
            if (!_handlers.TryAdd( handler.Action, handler )) {
                throw new InvalidOperationException(
                    $"Duplicate action handler registered for action '{handler.Action}'. " +
                    $"Existing: {_handlers[handler.Action].GetType( ).Name}, " +
                    $"Duplicate: {handler.GetType( ).Name}" );
            }
        }

        // Validate that all expected actions have registered handlers.
        IEnumerable<string> actionsToValidate = expectedActions ?? DefaultExpectedActions;
        List<string> missing = [];
        foreach (string action in actionsToValidate) {
            if (!_handlers.ContainsKey( action )) {
                missing.Add( action );
            }
        }

        if (missing.Count > 0) {
            throw new InvalidOperationException(
                $"Missing action handler registrations: [{string.Join( ", ", missing )}]. " +
                $"Ensure all IActionHandler implementations are registered via AddActionHandlers()." );
        }

        if (_logger.IsEnabled( LogLevel.Information )) {
            _logger.LogInformation( "ActionOperator initialized with {Count} handlers: [{Actions}]",
                _handlers.Count, string.Join( ", ", _handlers.Keys ) );
        }
    }

    /// <inheritdoc/>
    public OperatorExecution Execute( ActionDescriptor descriptor, string? inputVariableValue = null, CancellationToken cancellationToken = default ) {
        Channel<OperatorOutput> channel = Channel.CreateBounded<OperatorOutput>(
            new BoundedChannelOptions( 10_000 ) { FullMode = BoundedChannelFullMode.Wait, SingleWriter = false } );

        TaskCompletionSource<IOperatorResult> resultTcs = new( TaskCreationOptions.RunContinuationsAsynchronously );

        _ = ExecuteInternal( descriptor, channel.Writer, resultTcs, inputVariableValue, cancellationToken );

        return new OperatorExecution( channel.Reader.ReadAllAsync( cancellationToken ), resultTcs.Task );
    }

    /// <summary>
    /// Internal execution pipeline that resolves the handler, applies timeout logic,
    /// invokes the handler, and writes output and results to the channel and task completion source.
    /// </summary>
    private async Task ExecuteInternal(
        ActionDescriptor descriptor,
        ChannelWriter<OperatorOutput> writer,
        TaskCompletionSource<IOperatorResult> resultTcs,
        string? inputVariableValue,
        CancellationToken cancellationToken
    ) {

        CancellationTokenSource? timeoutCts = null;
        CancellationTokenSource? linkedCts = null;

        try {
            if (!_handlers.TryGetValue( descriptor.Action, out IActionHandler? handler )) {
                string message = $"No handler registered for action '{descriptor.Action}'. " +
                    $"Available actions: [{string.Join( ", ", _handlers.Keys )}]";
                _logger.LogError( "{Message}", message );
                await writer.WriteAsync(
                    OperatorOutput.Create( LogLevel.Error, message ), cancellationToken );
                resultTcs.SetResult( new ActionOperatorResult(
                    Success: false,
                    Exception: new InvalidOperationException( message ) ) );
                return;
            }

            if (_logger.IsEnabled( LogLevel.Information )) {
                _logger.LogInformation( "Executing action '{Action}' via handler {Handler}",
                    descriptor.Action, handler.GetType( ).Name );
            }

            await writer.WriteAsync(
                OperatorOutput.Create( LogLevel.Information, $"Starting action: {descriptor.Action}" ),
                cancellationToken );

            // Apply configurable timeout if set.
            TimeSpan? timeout = _options.CurrentValue.DefaultTimeout;
            CancellationToken handlerToken = cancellationToken;

            if (timeout.HasValue) {
                timeoutCts = new CancellationTokenSource( );
                timeoutCts.CancelAfter( timeout.Value );
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken, timeoutCts.Token );
                handlerToken = linkedCts.Token;
            }

            ActionOperatorResult result = await handler.ExecuteAsync(
                descriptor.Parameters, writer, inputVariableValue, handlerToken);

            if (_logger.IsEnabled( LogLevel.Information )) {
                _logger.LogInformation( "Action '{Action}' completed. Success: {Success}",
                    descriptor.Action, result.Success );
            }

            resultTcs.SetResult( result );
        } catch (OperationCanceledException ex) when (timeoutCts is not null && timeoutCts.IsCancellationRequested) {
            // Timeout — distinguish from caller cancellation.
            TimeSpan timeout = _options.CurrentValue.DefaultTimeout!.Value;
            _logger.LogWarning( "Action '{Action}' timed out after {Timeout}", descriptor.Action, timeout );
            try {
                await writer.WriteAsync(
                    OperatorOutput.Create( LogLevel.Warning,
                        $"Action '{descriptor.Action}' timed out after {timeout}." ),
                    CancellationToken.None );
            } catch {
                // Channel may be completed; swallow
            }
            resultTcs.SetResult( new ActionOperatorResult( Success: false, Exception: ex ) );
        } catch (OperationCanceledException ex) {
            _logger.LogWarning( "Action '{Action}' was cancelled", descriptor.Action );
            resultTcs.SetResult( new ActionOperatorResult( Success: false, Exception: ex ) );
        } catch (Exception ex) {
            _logger.LogError( ex, "Action '{Action}' failed with unhandled exception", descriptor.Action );
            try {
                await writer.WriteAsync(
                    OperatorOutput.Create( LogLevel.Error, $"Action failed: {ex.Message}" ),
                    cancellationToken );
            } catch {
                // Channel may be completed; swallow
            }
            resultTcs.SetResult( new ActionOperatorResult( Success: false, Exception: ex ) );
        } finally {
            _ = writer.TryComplete( );
            timeoutCts?.Dispose( );
            linkedCts?.Dispose( );
        }
    }
}
