using Microsoft.Extensions.Logging.Abstractions;
using Werkr.Core.Communication;

namespace Werkr.Tests.Data.Unit.Communication;

/// <summary>
/// Unit tests for the <see cref="WorkflowEventBroadcaster"/> class, validating subscription management, event
/// fan-out delivery, and thread-safe subscriber count tracking.
/// </summary>
[TestClass]
public class WorkflowEventBroadcasterTests {
    /// <summary>
    /// The <see cref="WorkflowEventBroadcaster"/> instance under test.
    /// </summary>
    private WorkflowEventBroadcaster _broadcaster = null!;

    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> providing per-test cancellation tokens and metadata.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Creates a fresh <see cref="WorkflowEventBroadcaster"/> for each test.
    /// </summary>
    [TestInitialize]
    public void TestInit( ) {
        _broadcaster = new WorkflowEventBroadcaster(
            NullLogger<WorkflowEventBroadcaster>.Instance
        );
    }

    /// <summary>
    /// Verifies that <see cref="WorkflowEventBroadcaster.Subscribe"/> returns a non-null subscription
    /// with a readable channel.
    /// </summary>
    [TestMethod]
    public void Subscribe_ReturnsSubscriptionWithReader( ) {
        using WorkflowEventSubscription sub = _broadcaster.Subscribe( );

        Assert.IsNotNull( sub );
        Assert.IsNotNull( sub.Reader );
    }

    /// <summary>
    /// Verifies that published events are delivered to a single subscriber.
    /// </summary>
    [TestMethod]
    public async Task Publish_DeliversEventToSubscriber( ) {
        CancellationToken ct = TestContext.CancellationToken;
        using WorkflowEventSubscription sub = _broadcaster.Subscribe( );

        Guid runId = Guid.NewGuid( );
        StepStartedEvent evt = new( runId, 1, "Step1", 10, DateTime.UtcNow );
        _broadcaster.Publish( evt );

        bool available = await sub.Reader.WaitToReadAsync( ct );

        Assert.IsTrue( available );
        Assert.IsTrue( sub.Reader.TryRead( out WorkflowEvent? received ) );
        Assert.AreEqual( runId, received!.WorkflowRunId );
    }

    /// <summary>
    /// Verifies that published events fan out to all active subscribers.
    /// </summary>
    [TestMethod]
    public async Task Publish_FansOutToMultipleSubscribers( ) {
        CancellationToken ct = TestContext.CancellationToken;
        using WorkflowEventSubscription sub1 = _broadcaster.Subscribe( );
        using WorkflowEventSubscription sub2 = _broadcaster.Subscribe( );

        Guid runId = Guid.NewGuid( );
        StepStartedEvent evt = new( runId, 1, "Step1", 10, DateTime.UtcNow );
        _broadcaster.Publish( evt );

        Assert.IsTrue( await sub1.Reader.WaitToReadAsync( ct ) );
        Assert.IsTrue( sub1.Reader.TryRead( out WorkflowEvent? received1 ) );
        Assert.AreEqual( runId, received1!.WorkflowRunId );

        Assert.IsTrue( await sub2.Reader.WaitToReadAsync( ct ) );
        Assert.IsTrue( sub2.Reader.TryRead( out WorkflowEvent? received2 ) );
        Assert.AreEqual( runId, received2!.WorkflowRunId );
    }

    /// <summary>
    /// Verifies that disposing a subscription removes it from the broadcaster so
    /// subsequent publishes are not delivered to the disposed subscriber.
    /// </summary>
    [TestMethod]
    public void Dispose_RemovesSubscriber( ) {
        WorkflowEventSubscription sub = _broadcaster.Subscribe( );
        sub.Dispose( );

        Guid runId = Guid.NewGuid( );
        StepStartedEvent evt = new( runId, 1, "Step1", 10, DateTime.UtcNow );
        _broadcaster.Publish( evt );

        // Channel was completed on unsubscribe; TryRead should return false.
        Assert.IsFalse( sub.Reader.TryRead( out _ ) );
    }

    /// <summary>
    /// Verifies that concurrent subscribe and unsubscribe operations do not corrupt
    /// the subscriber list.
    /// </summary>
    [TestMethod]
    public async Task ConcurrentSubscribeUnsubscribe_DoesNotCorruptState( ) {
        CancellationToken ct = TestContext.CancellationToken;
        const int Iterations = 100;
        List<Task> tasks = [];

        for (int i = 0; i < Iterations; i++) {
            tasks.Add( Task.Run( ( ) => {
                WorkflowEventSubscription sub = _broadcaster.Subscribe( );
                sub.Dispose( );
            }, ct ) );
        }

        await Task.WhenAll( tasks );

        // After all subscribe/unsubscribe pairs complete, a new publish should succeed
        // without throwing (no corrupted list).
        using WorkflowEventSubscription final = _broadcaster.Subscribe( );
        Guid runId = Guid.NewGuid( );
        StepStartedEvent evt = new( runId, 1, "Step1", 10, DateTime.UtcNow );
        _broadcaster.Publish( evt );

        Assert.IsTrue( await final.Reader.WaitToReadAsync( ct ) );
        Assert.IsTrue( final.Reader.TryRead( out WorkflowEvent? received ) );
        Assert.AreEqual( runId, received!.WorkflowRunId );
    }

    /// <summary>
    /// Verifies that publishing with no subscribers does not throw.
    /// </summary>
    [TestMethod]
    public void Publish_WithNoSubscribers_DoesNotThrow( ) {
        Guid runId = Guid.NewGuid( );
        StepStartedEvent evt = new( runId, 1, "Step1", 10, DateTime.UtcNow );

        _broadcaster.Publish( evt );
    }

    /// <summary>
    /// Verifies that disposing a subscription twice does not throw.
    /// </summary>
    [TestMethod]
    public void Dispose_CalledTwice_DoesNotThrow( ) {
        WorkflowEventSubscription sub = _broadcaster.Subscribe( );
        sub.Dispose( );
        sub.Dispose( );
    }
}
