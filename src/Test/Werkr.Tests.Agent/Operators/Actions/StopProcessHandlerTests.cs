using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;

using Werkr.Agent.Operators.Actions;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Operators.Actions;

[TestClass]
public class StopProcessHandlerTests {

    private StopProcessHandler _handler = null!;
    private Channel<OperatorOutput> _channel = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void TestInit( ) {
        _handler = new StopProcessHandler( NullLogger<StopProcessHandler>.Instance );
        _channel = Channel.CreateUnbounded<OperatorOutput>( );
    }

    private static JsonElement Serialize<T>( T value ) =>
        TestActionDescriptor.Serialize( value );

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task StopProcess_NonexistentName_ReturnsFailure( ) {
        JsonElement parameters = Serialize( new StopProcessParameters {
            ProcessName = $"werkr-test-nonexistent-{Guid.NewGuid()}"
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
    }

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
            ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

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

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task StopProcess_InvalidPid_ReturnsFailure( ) {
        JsonElement parameters = Serialize( new StopProcessParameters {
            ProcessName = "unused",
            ProcessId = int.MaxValue,
            Force = true
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
    }
}
