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
public class StartProcessHandlerTests {

    private StartProcessHandler _handler = null!;
    private Channel<OperatorOutput> _channel = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void TestInit( ) {
        _handler = new StartProcessHandler( TestFilePathResolver.AllowAll, NullLogger<StartProcessHandler>.Instance );
        _channel = Channel.CreateUnbounded<OperatorOutput>( );
    }

    private static JsonElement Serialize<T>( T value ) =>
        TestActionDescriptor.Serialize( value );

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task StartProcess_EchoCommand_Succeeds( ) {
        string fileName;
        string arguments;
        if (OperatingSystem.IsWindows( )) {
            fileName = "cmd.exe";
            arguments = "/c echo hello";
        } else {
            fileName = "/bin/sh";
            arguments = "-c \"echo hello\"";
        }

        JsonElement parameters = Serialize( new StartProcessParameters {
            FileName = fileName,
            Arguments = arguments,
            WaitForExit = true
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task StartProcess_NonZeroExit_ReturnsFailure( ) {
        string fileName;
        string arguments;
        if (OperatingSystem.IsWindows( )) {
            fileName = "cmd.exe";
            arguments = "/c exit 1";
        } else {
            fileName = "/bin/sh";
            arguments = "-c \"exit 1\"";
        }

        JsonElement parameters = Serialize( new StartProcessParameters {
            FileName = fileName,
            Arguments = arguments,
            WaitForExit = true
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task StartProcess_FireAndForget_ReturnsImmediately( ) {
        string fileName;
        string arguments;
        if (OperatingSystem.IsWindows( )) {
            fileName = "cmd.exe";
            arguments = "/c echo fire-and-forget";
        } else {
            fileName = "/bin/sh";
            arguments = "-c \"echo fire-and-forget\"";
        }

        JsonElement parameters = Serialize( new StartProcessParameters {
            FileName = fileName,
            Arguments = arguments,
            WaitForExit = false
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task StartProcess_BareExecutable_SkipsPathValidation( ) {
        // "dotnet" is a bare executable name — should not trigger path validation
        JsonElement parameters = Serialize( new StartProcessParameters {
            FileName = "dotnet",
            Arguments = "--version",
            WaitForExit = true
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task StartProcess_Timeout_KillsProcess( ) {
        string fileName;
        string arguments;
        if (OperatingSystem.IsWindows( )) {
            fileName = "cmd.exe";
            arguments = "/c ping -n 300 127.0.0.1";
        } else {
            fileName = "/bin/sh";
            arguments = "-c \"sleep 300\"";
        }

        JsonElement parameters = Serialize( new StartProcessParameters {
            FileName = fileName,
            Arguments = arguments,
            WaitForExit = true,
            TimeoutMs = 500
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task StartProcess_CapturesOutput( ) {
        string fileName;
        string arguments;
        if (OperatingSystem.IsWindows( )) {
            fileName = "cmd.exe";
            arguments = "/c echo test-output";
        } else {
            fileName = "/bin/sh";
            arguments = "-c \"echo test-output\"";
        }

        JsonElement parameters = Serialize( new StartProcessParameters {
            FileName = fileName,
            Arguments = arguments,
            WaitForExit = true
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );

        // Drain the channel to check outputs
        _channel.Writer.Complete( );
        List<OperatorOutput> outputs = [];
        await foreach (OperatorOutput output in _channel.Reader.ReadAllAsync( TestContext.CancellationToken )) {
            outputs.Add( output );
        }

        Assert.Contains(
            o => o.Message.Contains( "test-output" ),
            outputs, "Expected output containing 'test-output'." );
    }
}
