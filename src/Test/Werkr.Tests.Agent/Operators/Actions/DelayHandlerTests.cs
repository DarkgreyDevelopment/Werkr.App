using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Werkr.Agent.Operators.Actions;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Operators.Actions;

/// <summary>
/// Unit tests for the <see cref="DelayHandler"/> action handler.
/// Validates delay execution, cancellation behavior, and reason output.
/// </summary>
[TestClass]
public class DelayHandlerTests {

    /// <summary>
    /// The handler instance under test.
    /// </summary>
    private DelayHandler _handler = null!;
    /// <summary>
    /// Fake time provider for deterministic delay testing.
    /// </summary>
    private FakeTimeProvider _timeProvider = null!;
    /// <summary>
    /// Unbounded channel used to capture <see cref="OperatorOutput"/> messages.
    /// </summary>
    private Channel<OperatorOutput> _channel = null!;

    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> for the current test run.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Creates the handler with a fake time provider and an unbounded output channel.
    /// </summary>
    [TestInitialize]
    public void TestInit( ) {
        _timeProvider = new FakeTimeProvider( );
        _handler = new DelayHandler(
            NullLogger<DelayHandler>.Instance,
            _timeProvider
        );
        _channel = Channel.CreateUnbounded<OperatorOutput>( );
    }

    /// <summary>
    /// Serializes a value to a <see cref="JsonElement"/> using the shared test serializer.
    /// </summary>
    private static JsonElement Serialize<T>( T value ) =>
        TestActionDescriptor.Serialize( value );

    /// <summary>
    /// Verifies that a delay of zero seconds completes immediately.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Delay_ZeroSeconds_CompletesImmediately( ) {
        JsonElement parameters = Serialize( new DelayParameters { Seconds = 0 } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
    }

    /// <summary>
    /// Verifies that advancing the fake clock completes a non-zero delay.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Delay_PositiveSeconds_CompletesAfterAdvance( ) {
        JsonElement parameters = Serialize( new DelayParameters { Seconds = 10 } );

        Task<ActionOperatorResult> task = _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        _timeProvider.Advance( TimeSpan.FromSeconds( 10 ) );

        ActionOperatorResult result = await task;

        Assert.IsTrue( result.Success );
    }

    /// <summary>
    /// Verifies that a reason message appears in the output channel.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Delay_WithReason_WritesReasonToOutput( ) {
        JsonElement parameters = Serialize( new DelayParameters { Seconds = 0, Reason = "waiting for deploy" } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );

        _channel.Writer.Complete( );
        List<OperatorOutput> messages = [ ];
        await foreach (OperatorOutput msg in _channel.Reader.ReadAllAsync( TestContext.CancellationToken )) {
            messages.Add( msg );
        }

        Assert.IsTrue(
            messages.Exists( m => m.Message.Contains( "waiting for deploy" ) ),
            "Expected reason text in output."
        );
    }

    /// <summary>
    /// Verifies that cancellation during a delay propagates as OperationCanceledException.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Delay_Cancelled_ThrowsOperationCanceled( ) {
        JsonElement parameters = Serialize( new DelayParameters { Seconds = 300 } );
        using CancellationTokenSource cts = new( );

        Task<ActionOperatorResult> task = _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cts.Token
        );

        await cts.CancelAsync( );

        await Assert.ThrowsExactlyAsync<TaskCanceledException>( ( ) => task );
    }

    /// <summary>
    /// Verifies that a negative Seconds value produces a failure result.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task Delay_NegativeSeconds_ReturnsFailure( ) {
        JsonElement parameters = Serialize( new DelayParameters { Seconds = -1 } );

        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
    }
}
