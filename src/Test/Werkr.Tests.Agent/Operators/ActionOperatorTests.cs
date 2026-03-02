using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Werkr.Agent.Operators;
using Werkr.Common.Models;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Operators;

[TestClass]
public class ActionOperatorTests {

    public TestContext TestContext { get; set; } = null!;

    private static IOptionsMonitor<ActionOperatorConfiguration> DefaultOptions( TimeSpan? timeout = null ) {
        ActionOperatorConfiguration config = new( ) {
            DefaultTimeout = timeout ?? TimeSpan.FromHours( 1 ),
        };
        return new TestOptionsMonitor<ActionOperatorConfiguration>( config );
    }

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

    [TestMethod]
    public void Constructor_ValidHandlers_Succeeds( ) {
        IActionHandler[] handlers = [new SuccessHandler( "A" ), new SuccessHandler( "B" )];

        ActionOperator op = CreateOperator( handlers );

        Assert.IsNotNull( op );
    }

    [TestMethod]
    public void Constructor_DuplicateHandlers_ThrowsInvalidOperation( ) {
        IActionHandler[] handlers = [new SuccessHandler( "A" ), new SuccessHandler( "A" )];

        _ = Assert.ThrowsExactly<InvalidOperationException>(
            ( ) => CreateOperator( handlers ) );
    }

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

    [TestMethod]
    public void Constructor_EmptyExpectedActions_SkipsValidation( ) {
        IActionHandler[] handlers = [new SuccessHandler( "OnlyOne" )];

        // Should not throw even though only 1 handler and no DefaultExpectedActions
        ActionOperator op = CreateOperator( handlers, expectedActions: [] );

        Assert.IsNotNull( op );
    }

    [TestMethod]
    public void Constructor_NullExpectedActions_UsesDefaultList( ) {
        // With null expectedActions, it uses DefaultExpectedActions which requires 11 handlers
        IActionHandler[] handlers = [new SuccessHandler( "A" )];

        _ = Assert.ThrowsExactly<InvalidOperationException>(
            ( ) => new ActionOperator(
                handlers,
                DefaultOptions( ),
                NullLogger<ActionOperator>.Instance,
                expectedActions: null ) );
    }

    // ──────── Execute — Success ────────

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Execute_SuccessHandler_ReturnsSuccess( ) {
        SuccessHandler handler = new( "TestAction" );
        ActionOperator op = CreateOperator( [handler] );

        ActionDescriptor descriptor = TestActionDescriptor.Create( "TestAction" );
        OperatorExecution execution = op.Execute( descriptor, TestContext.CancellationToken );

        List<OperatorOutput> outputs = [];
        await foreach (OperatorOutput output in execution.Output.WithCancellation( TestContext.CancellationToken )) {
            outputs.Add( output );
        }

        IOperatorResult result = await execution.Result;

        Assert.IsTrue( result is ActionOperatorResult { Success: true } );
        Assert.IsNotEmpty( outputs );
    }

    // ──────── Execute — Failure ────────

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Execute_FailHandler_ReturnsFailure( ) {
        FailHandler handler = new( "FailAction" );
        ActionOperator op = CreateOperator( [handler] );

        ActionDescriptor descriptor = TestActionDescriptor.Create( "FailAction" );
        OperatorExecution execution = op.Execute( descriptor, TestContext.CancellationToken );

        await foreach (OperatorOutput _ in execution.Output.WithCancellation( TestContext.CancellationToken )) { }

        IOperatorResult result = await execution.Result;

        Assert.IsTrue( result is ActionOperatorResult { Success: false } );
    }

    // ──────── Execute — Exception ────────

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Execute_ThrowHandler_ReturnsFailureWithException( ) {
        ThrowHandler handler = new( "ThrowAction" );
        ActionOperator op = CreateOperator( [handler] );

        ActionDescriptor descriptor = TestActionDescriptor.Create( "ThrowAction" );
        OperatorExecution execution = op.Execute( descriptor, TestContext.CancellationToken );

        await foreach (OperatorOutput _ in execution.Output.WithCancellation( TestContext.CancellationToken )) { }

        IOperatorResult result = await execution.Result;

        Assert.IsTrue( result is ActionOperatorResult { Success: false } );
        ActionOperatorResult actionResult = (ActionOperatorResult) result;
        Assert.IsNotNull( actionResult.Exception );
        _ = Assert.IsInstanceOfType<InvalidOperationException>( actionResult.Exception );
    }

    // ──────── Execute — Unknown Action ────────

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Execute_UnknownAction_ReturnsFailure( ) {
        SuccessHandler handler = new( "KnownAction" );
        ActionOperator op = CreateOperator( [handler] );

        ActionDescriptor descriptor = TestActionDescriptor.Create( "UnknownAction" );
        OperatorExecution execution = op.Execute( descriptor, TestContext.CancellationToken );

        List<OperatorOutput> outputs = [];
        await foreach (OperatorOutput output in execution.Output.WithCancellation( TestContext.CancellationToken )) {
            outputs.Add( output );
        }

        IOperatorResult result = await execution.Result;

        Assert.IsTrue( result is ActionOperatorResult { Success: false } );
        Assert.Contains(
            o => o.LogLevel == "Error" && o.Message.Contains( "No handler registered" ),
            outputs, "Expected error message about missing handler." );
    }

    // ──────── Execute — Timeout ────────

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Execute_Timeout_ReturnsFailure( ) {
        SlowHandler handler = new( "SlowAction" );
        ActionOperator op = CreateOperator(
            [handler],
            options: DefaultOptions( timeout: TimeSpan.FromMilliseconds( 200 ) ) );

        ActionDescriptor descriptor = TestActionDescriptor.Create( "SlowAction" );
        OperatorExecution execution = op.Execute( descriptor, TestContext.CancellationToken );

        List<OperatorOutput> outputs = [];
        await foreach (OperatorOutput output in execution.Output.WithCancellation( TestContext.CancellationToken )) {
            outputs.Add( output );
        }

        IOperatorResult result = await execution.Result;

        Assert.IsTrue( result is ActionOperatorResult { Success: false } );
        Assert.Contains(
            o => o.LogLevel == "Warning" && o.Message.Contains( "timed out" ),
            outputs, "Expected timeout warning in output." );
    }

    // ──────── Execute — Cancellation ────────

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Execute_Cancellation_ReturnsFailure( ) {
        SlowHandler handler = new( "SlowAction" );
        ActionOperator op = CreateOperator(
            [handler],
            options: DefaultOptions( timeout: null ) );

        using CancellationTokenSource cts = new( TimeSpan.FromMilliseconds( 200 ) );

        ActionDescriptor descriptor = TestActionDescriptor.Create( "SlowAction" );
        OperatorExecution execution = op.Execute( descriptor, cts.Token );

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

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Execute_CaseInsensitiveActionName_DispatchesCorrectly( ) {
        SuccessHandler handler = new( "TestAction" );
        ActionOperator op = CreateOperator( [handler] );

        // Use different casing than registered
        ActionDescriptor descriptor = TestActionDescriptor.Create( "testaction" );
        OperatorExecution execution = op.Execute( descriptor, TestContext.CancellationToken );

        await foreach (OperatorOutput _ in execution.Output.WithCancellation( TestContext.CancellationToken )) { }

        IOperatorResult result = await execution.Result;

        Assert.IsTrue( result is ActionOperatorResult { Success: true } );
    }

    // ──────── Execute — Null timeout means no timeout ────────

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Execute_NullTimeout_NoTimeoutApplied( ) {
        SuccessHandler handler = new( "TestAction" );
        ActionOperator op = CreateOperator(
            [handler],
            options: DefaultOptions( timeout: null ) );

        ActionDescriptor descriptor = TestActionDescriptor.Create( "TestAction" );
        OperatorExecution execution = op.Execute( descriptor, TestContext.CancellationToken );

        await foreach (OperatorOutput _ in execution.Output.WithCancellation( TestContext.CancellationToken )) { }

        IOperatorResult result = await execution.Result;

        Assert.IsTrue( result is ActionOperatorResult { Success: true } );
    }

    /// <summary>
    /// Simple <see cref="IOptionsMonitor{T}"/> implementation for tests.
    /// </summary>
    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T> {

        public TestOptionsMonitor( T currentValue ) {
            CurrentValue = currentValue;
        }

        public T CurrentValue { get; }

        public T Get( string? name ) => CurrentValue;

        public IDisposable? OnChange( Action<T, string?> listener ) => null;
    }
}
