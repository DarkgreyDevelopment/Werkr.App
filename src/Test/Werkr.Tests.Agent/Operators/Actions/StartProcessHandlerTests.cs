using System.Text.Json;
using System.Threading.Channels;
using Werkr.Agent.Operators.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Operators.Actions;

/// <summary>
/// Unit tests for the <see cref="StartProcessHandler"/> action handler.
/// Validates launching processes with echo commands (cross-platform),
/// handling of non-zero exit codes, fire-and-forget mode, bare-executable
/// path-validation bypass, timeout-based process termination, and
/// standard-output capture.
/// </summary>
[TestClass]
public class StartProcessHandlerTests {

    /// <summary>
    /// The handler instance under test.
    /// </summary>
    private StartProcessHandler _handler = null!;
    /// <summary>
    /// Unbounded channel used to capture <see cref="OperatorOutput"/> messages.
    /// </summary>
    private Channel<OperatorOutput> _channel = null!;

    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> for the current test run.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Creates the handler backed by <see cref="TestFilePathResolver.AllowAll"/> and an unbounded output channel.
    /// </summary>
    [TestInitialize]
    public void TestInit( ) {
        _handler = new StartProcessHandler(
            TestFilePathResolver.AllowAll,
            NullLogger<StartProcessHandler>.Instance
        );
        _channel = Channel.CreateUnbounded<OperatorOutput>( );
    }

    /// <summary>
    /// Serializes a value to a <see cref="JsonElement"/> using the shared test serializer.
    /// </summary>
    private static JsonElement Serialize<T>( T value ) =>
        TestActionDescriptor.Serialize( value );

    /// <summary>
    /// Verifies that starting a simple echo command (platform-appropriate)
    /// with <c>WaitForExit</c> returns a successful result.
    /// </summary>
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
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
    }

    /// <summary>
    /// Verifies that a process exiting with a non-zero exit code returns
    /// a failure result.
    /// </summary>
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
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
    }

    /// <summary>
    /// Verifies that when <c>WaitForExit</c> is <see langword="false"/> the handler
    /// returns immediately with a success result (fire-and-forget mode).
    /// </summary>
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
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
    }

    /// <summary>
    /// Verifies that launching a bare executable name (without a directory
    /// component) skips path-allowlist validation and still succeeds.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task StartProcess_BareExecutable_SkipsPathValidation( ) {
        // "dotnet" is a bare executable name — should not trigger path validation
        JsonElement parameters = Serialize( new StartProcessParameters {
            FileName = "dotnet",
            Arguments = "--version",
            WaitForExit = true
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
    }

    /// <summary>
    /// Verifies that when the process exceeds the <c>TimeoutMs</c> value
    /// it is killed and the handler returns a failure result.
    /// </summary>
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
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
    }

    /// <summary>
    /// Verifies that the handler captures the process's standard output and
    /// writes it through the <see cref="OperatorOutput"/> channel, where it
    /// can be found by searching for the expected text.
    /// </summary>
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
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );
        Assert.IsTrue( result.Success );
        _channel.Writer.Complete( );
        List<OperatorOutput> outputs = [];
        await foreach (OperatorOutput output in _channel.Reader.ReadAllAsync( TestContext.CancellationToken )) {
            outputs.Add( output );
        }

        Assert.Contains(
            o => o.Message.Contains( "test-output" ),
            outputs,
            "Expected output containing 'test-output'."
        );
    }
}
