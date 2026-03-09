using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Werkr.Agent.Operators;
using Werkr.Common.Models;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Operators;

/// <summary>
/// Unit tests for the <see cref="ActionOperator"/> class, which dispatches
/// <see cref="ActionDescriptor"/> requests to registered
/// <see cref="IActionHandler"/> implementations. Covers constructor validation
/// (duplicates, missing expected actions), successful and failed handler
/// dispatch, timeout enforcement, cancellation propagation, case-insensitive
/// action-name matching, and null-timeout behavior.
/// </summary>
[TestClass]
public class ActionOperatorTests {

    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> for the current test run.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Creates an <see cref="IOptionsMonitor{ActionOperatorConfiguration}"/>
    /// with the given timeout. Defaults to a one-hour timeout when none
    /// is supplied.
    /// </summary>
    private static IOptionsMonitor<ActionOperatorConfiguration> DefaultOptions( TimeSpan? timeout = null ) {
        ActionOperatorConfiguration config = new( ) {
            DefaultTimeout = timeout ?? TimeSpan.FromHours( 1 ),
        };
        return new TestOptionsMonitor<ActionOperatorConfiguration>( config );
    }

    /// <summary>
    /// Factory that constructs an <see cref="ActionOperator"/> with the given
    /// handlers, options, and optional expected-action names for validation.
    /// </summary>
    private static ActionOperator CreateOperator(
        IActionHandler[] handlers,
        IOptionsMonitor<ActionOperatorConfiguration>? options = null,
        IEnumerable<string>? expectedActions = null ) {
        return new ActionOperator(
            handlers,
            options ?? DefaultOptions( ),
            NullLogger<ActionOperator>.Instance,
            expectedActions ?? [] );
    }

    // ──────── Construction / Validation ────────

    /// <summary>
    /// Verifies that constructing an <see cref="ActionOperator"/> with
    /// uniquely-named handlers succeeds without throwing.
    /// </summary>
    [TestMethod]
    public void Constructor_ValidHandlers_Succeeds( ) {
        IActionHandler[] handlers = [new SuccessHandler( "A" ), new SuccessHandler( "B" )];

        ActionOperator op = CreateOperator( handlers );

        Assert.IsNotNull( op );
    }

    /// <summary>
    /// Verifies that registering two handlers with the same action name
    /// throws an <see cref="InvalidOperationException"/> during construction.
    /// </summary>
    [TestMethod]
    public void Constructor_DuplicateHandlers_ThrowsInvalidOperation( ) {
        IActionHandler[] handlers = [new SuccessHandler( "A" ), new SuccessHandler( "A" )];

        _ = Assert.ThrowsExactly<InvalidOperationException>(
            ( ) => CreateOperator( handlers ) );
    }

    /// <summary>
    /// Verifies that if an expected-actions list contains names not covered
    /// by the registered handlers,
    /// an <see cref="InvalidOperationException"/> is thrown.
    /// </summary>
    [TestMethod]
    public void Constructor_MissingExpectedActions_ThrowsInvalidOperation( ) {
        IActionHandler[] handlers = [new SuccessHandler( "A" )];

        _ = Assert.ThrowsExactly<InvalidOperationException>(
            ( ) => new ActionOperator(
                handlers,
                DefaultOptions( ),
                NullLogger<ActionOperator>.Instance,
                expectedActions: ["A", "B", "C"] ) );
    }

    /// <summary>
    /// Verifies that passing an empty expected-actions list skips the
    /// validation check and allows the operator to be constructed
    /// successfully.
    /// </summary>
    [TestMethod]
    public void Constructor_EmptyExpectedActions_SkipsValidation( ) {
        IActionHandler[] handlers = [new SuccessHandler( "OnlyOne" )];

        // Should not throw even though only 1 handler and no DefaultExpectedActions
        ActionOperator op = CreateOperator( handlers, expectedActions: [] );

        Assert.IsNotNull( op );
    }

    /// <summary>
    /// Verifies that passing <see langword="null"/> for the expected-actions
    /// parameter causes the operator to use a default list, which triggers
    /// an <see cref="InvalidOperationException"/> when handlers are
    /// insufficient.
    /// </summary>
    [TestMethod]
    public void Constructor_NullExpectedActions_UsesDefaultList( ) {
        // With null expectedActions, it uses DefaultExpectedActions which requires 19 handlers
        IActionHandler[] handlers = [new SuccessHandler( "A" )];

        _ = Assert.ThrowsExactly<InvalidOperationException>(
            ( ) => new ActionOperator(
                handlers,
                DefaultOptions( ),
                NullLogger<ActionOperator>.Instance,
                expectedActions: null ) );
    }

    // ──────── Execute — Success ────────

    /// <summary>
    /// Verifies that dispatching to a <see cref="SuccessHandler"/> returns
    /// a successful <see cref="ActionOperatorResult"/> and produces at least
    /// one output message.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Execute_SuccessHandler_ReturnsSuccess( ) {
        SuccessHandler handler = new( "TestAction" );
        ActionOperator op = CreateOperator( [handler] );

        ActionDescriptor descriptor = TestActionDescriptor.Create( "TestAction" );
        OperatorExecution execution = op.Execute(
            descriptor,
            TestContext.CancellationToken
        );

        List<OperatorOutput> outputs = [];
        await foreach (OperatorOutput output in execution.Output.WithCancellation( TestContext.CancellationToken )) {
            outputs.Add( output );
        }

        IOperatorResult result = await execution.Result;

        Assert.IsTrue( result is ActionOperatorResult { Success: true } );
        Assert.IsNotEmpty( outputs );
    }

    // ──────── Execute — Failure ────────

    /// <summary>
    /// Verifies that dispatching to a <see cref="FailHandler"/> returns
    /// an <see cref="ActionOperatorResult"/> with
    /// <see cref="ActionOperatorResult.Success"/> equal to
    /// <see langword="false"/>.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Execute_FailHandler_ReturnsFailure( ) {
        FailHandler handler = new( "FailAction" );
        ActionOperator op = CreateOperator( [handler] );

        ActionDescriptor descriptor = TestActionDescriptor.Create( "FailAction" );
        OperatorExecution execution = op.Execute(
            descriptor,
            TestContext.CancellationToken
        );

        await foreach (OperatorOutput _ in execution.Output.WithCancellation( TestContext.CancellationToken )) { }

        IOperatorResult result = await execution.Result;

        Assert.IsTrue( result is ActionOperatorResult { Success: false } );
    }

    // ──────── Execute — Exception ────────

    /// <summary>
    /// Verifies that dispatching to a <see cref="ThrowHandler"/> catches
    /// the exception and returns a failure result with the exception
    /// preserved in <see cref="ActionOperatorResult.Exception"/>.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Execute_ThrowHandler_ReturnsFailureWithException( ) {
        ThrowHandler handler = new( "ThrowAction" );
        ActionOperator op = CreateOperator( [handler] );

        ActionDescriptor descriptor = TestActionDescriptor.Create( "ThrowAction" );
        OperatorExecution execution = op.Execute(
            descriptor,
            TestContext.CancellationToken
        );

        await foreach (OperatorOutput _ in execution.Output.WithCancellation( TestContext.CancellationToken )) { }

        IOperatorResult result = await execution.Result;

        Assert.IsTrue( result is ActionOperatorResult { Success: false } );
        ActionOperatorResult actionResult = (ActionOperatorResult) result;
        Assert.IsNotNull( actionResult.Exception );
        _ = Assert.IsInstanceOfType<InvalidOperationException>( actionResult.Exception );
    }

    // ──────── Execute — Unknown Action ────────

    /// <summary>
    /// Verifies that requesting an action for which no handler is registered
    /// returns a failure result and emits an error output mentioning
    /// "No handler registered".
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Execute_UnknownAction_ReturnsFailure( ) {
        SuccessHandler handler = new( "KnownAction" );
        ActionOperator op = CreateOperator( [handler] );

        ActionDescriptor descriptor = TestActionDescriptor.Create( "UnknownAction" );
        OperatorExecution execution = op.Execute(
            descriptor,
            TestContext.CancellationToken
        );

        List<OperatorOutput> outputs = [];
        await foreach (OperatorOutput output in execution.Output.WithCancellation( TestContext.CancellationToken )) {
            outputs.Add( output );
        }

        IOperatorResult result = await execution.Result;

        Assert.IsTrue( result is ActionOperatorResult { Success: false } );
        Assert.Contains(
            o => o.LogLevel == "Error" && o.Message.Contains( "No handler registered" ),
            outputs,
            "Expected error message about missing handler."
        );
    }

    // ──────── Execute — Timeout ────────

    /// <summary>
    /// Verifies that a handler exceeding the configured default timeout is
    /// cancelled and a failure result containing a "timed out" warning
    /// is produced.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Execute_Timeout_ReturnsFailure( ) {
        SlowHandler handler = new( "SlowAction" );
        ActionOperator op = CreateOperator(
            [handler],
            options: DefaultOptions( timeout: TimeSpan.FromMilliseconds( 200 ) ) );

        ActionDescriptor descriptor = TestActionDescriptor.Create( "SlowAction" );
        OperatorExecution execution = op.Execute(
            descriptor,
            TestContext.CancellationToken
        );

        List<OperatorOutput> outputs = [];
        await foreach (OperatorOutput output in execution.Output.WithCancellation( TestContext.CancellationToken )) {
            outputs.Add( output );
        }

        IOperatorResult result = await execution.Result;

        Assert.IsTrue( result is ActionOperatorResult { Success: false } );
        Assert.Contains(
            o => o.LogLevel == "Warning" && o.Message.Contains( "timed out" ),
            outputs,
            "Expected timeout warning in output."
        );
    }

    // ──────── Execute — Cancellation ────────

    /// <summary>
    /// Verifies that externally cancelling the token passed to
    /// <see cref="ActionOperator.Execute"/> stops the slow handler and
    /// returns a failure result.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Execute_Cancellation_ReturnsFailure( ) {
        SlowHandler handler = new( "SlowAction" );
        ActionOperator op = CreateOperator(
            [handler],
            options: DefaultOptions( timeout: null ) );

        using CancellationTokenSource cts = new( TimeSpan.FromMilliseconds( 200 ) );

        ActionDescriptor descriptor = TestActionDescriptor.Create( "SlowAction" );
        OperatorExecution execution = op.Execute(
            descriptor,
            cts.Token
        );

        List<OperatorOutput> outputs = [];
        try {
            await foreach (OperatorOutput output in execution.Output.WithCancellation( cts.Token )) {
                outputs.Add( output );
            }
        } catch (OperationCanceledException) {
            // Expected
        }

        IOperatorResult result = await execution.Result;

        Assert.IsTrue( result is ActionOperatorResult { Success: false } );
    }

    // ──────── Execute — Case-insensitive dispatch ────────

    /// <summary>
    /// Verifies that action-name dispatch is case-insensitive - a handler
    /// registered as "TestAction" can be invoked with "testaction".
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Execute_CaseInsensitiveActionName_DispatchesCorrectly( ) {
        SuccessHandler handler = new( "TestAction" );
        ActionOperator op = CreateOperator( [handler] );

        // Use different casing than registered
        ActionDescriptor descriptor = TestActionDescriptor.Create( "testaction" );
        OperatorExecution execution = op.Execute(
            descriptor,
            TestContext.CancellationToken
        );

        await foreach (OperatorOutput _ in execution.Output.WithCancellation( TestContext.CancellationToken )) { }

        IOperatorResult result = await execution.Result;

        Assert.IsTrue( result is ActionOperatorResult { Success: true } );
    }

    // ──────── Execute — Null timeout means no timeout ────────

    /// <summary>
    /// Verifies that setting the default timeout to <see langword="null"/>
    /// means no timeout is applied, and a quick handler completes
    /// successfully.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Execute_NullTimeout_NoTimeoutApplied( ) {
        SuccessHandler handler = new( "TestAction" );
        ActionOperator op = CreateOperator(
            [handler],
            options: DefaultOptions( timeout: null ) );

        ActionDescriptor descriptor = TestActionDescriptor.Create( "TestAction" );
        OperatorExecution execution = op.Execute(
            descriptor,
            TestContext.CancellationToken
        );

        await foreach (OperatorOutput _ in execution.Output.WithCancellation( TestContext.CancellationToken )) { }

        IOperatorResult result = await execution.Result;

        Assert.IsTrue( result is ActionOperatorResult { Success: true } );
    }

    /// <summary>
    /// Simple <see cref="IOptionsMonitor{T}"/> implementation for tests.
    /// </summary>
    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T> {

        /// <summary>
        /// Initializes a new instance of the <see cref="TestOptionsMonitor{T}"/> class.
        /// </summary>
        public TestOptionsMonitor( T currentValue ) {
            CurrentValue = currentValue;
        }

        /// <summary>
        /// Gets the current options value.
        /// </summary>
        public T CurrentValue { get; }

        /// <summary>
        /// Returns the current value regardless of the supplied <paramref name="name"/>.
        /// </summary>
        public T Get( string? name ) => CurrentValue;

        /// <summary>
        /// No-op change listener registration. Returns <see langword="null"/>.
        /// </summary>
        public IDisposable? OnChange( Action<T, string?> listener ) => null;
    }
}
