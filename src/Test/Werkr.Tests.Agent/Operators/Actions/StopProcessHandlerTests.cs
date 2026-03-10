using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Werkr.Agent.Operators.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Operators.Actions;

/// <summary>
/// Unit tests for the <see cref="StopProcessHandler"/> action handler.
/// Validates failure when the target process name does not exist,
/// successful forceful kill of a running process by PID,
/// and failure when an invalid PID is supplied.
/// </summary>
[TestClass]
public class StopProcessHandlerTests {

    /// <summary>
    /// The handler instance under test.
    /// </summary>
    private StopProcessHandler _handler = null!;
    /// <summary>
    /// Unbounded channel used to capture <see cref="OperatorOutput"/> messages.
    /// </summary>
    private Channel<OperatorOutput> _channel = null!;

    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> for the current test run.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Creates the handler and an unbounded output channel.
    /// </summary>
    [TestInitialize]
    public void TestInit( ) {
        _handler = new StopProcessHandler( NullLogger<StopProcessHandler>.Instance );
        _channel = Channel.CreateUnbounded<OperatorOutput>( );
    }

    /// <summary>
    /// Serializes a value to a <see cref="JsonElement"/> using the shared test serializer.
    /// </summary>
    private static JsonElement Serialize<T>( T value ) =>
        TestActionDescriptor.Serialize( value );

    /// <summary>
    /// Verifies that stopping a process by a name that does not exist returns a failure result.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task StopProcess_NonexistentName_ReturnsFailure( ) {
        JsonElement parameters = Serialize( new StopProcessParameters {
            ProcessName = $"werkr-test-nonexistent-{Guid.NewGuid()}"
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
    }

    /// <summary>
    /// Verifies that stopping a running process by its PID with
    /// <c>Force</c> enabled succeeds, and the process exits within a
    /// reasonable timeframe. Launches a long-running subprocess that
    /// is killed by the handler.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task StopProcess_ByPid_StopsProcess( ) {
        // Start a long-running process we can kill
        ProcessStartInfo startInfo = OperatingSystem.IsWindows( )
            ? new ProcessStartInfo( "cmd.exe", "/c ping -n 300 127.0.0.1" ) {
                CreateNoWindow = true,
                UseShellExecute = false
            }
            : new ProcessStartInfo( "/bin/sh", "-c \"sleep 300\"" ) {
                CreateNoWindow = true,
                UseShellExecute = false
            };
        using Process process = Process.Start( startInfo )!;
        int pid = process.Id;

        try {
            JsonElement parameters = Serialize( new StopProcessParameters {
                ProcessName = process.ProcessName,
                ProcessId = pid,
                Force = true
            } );
            ActionOperatorResult result = await _handler.ExecuteAsync(
                parameters,
                _channel.Writer,
                cancellationToken: TestContext.CancellationToken
            );

            Assert.IsTrue( result.Success );
            // Give a moment for the process to actually exit
            _ = process.WaitForExit( 5_000 );
            Assert.IsTrue( process.HasExited );
        } finally {
            if (!process.HasExited) {
                process.Kill( entireProcessTree: true );
            }
        }
    }

    /// <summary>
    /// Verifies that supplying an invalid (non-existent) PID returns a failure result with a non-null exception.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task StopProcess_InvalidPid_ReturnsFailure( ) {
        JsonElement parameters = Serialize( new StopProcessParameters {
            ProcessName = "unused",
            ProcessId = int.MaxValue,
            Force = true
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
    }
}
