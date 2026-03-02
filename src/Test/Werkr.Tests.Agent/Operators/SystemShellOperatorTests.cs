using Microsoft.Extensions.Logging.Abstractions;

using Werkr.Agent.Operators;
using Werkr.Core.Communication;
using Werkr.Core.Operators;

namespace Werkr.Tests.Agent.Operators;

[TestClass]
public class SystemShellOperatorTests {
    private SystemShellOperator _operator = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void TestInit( ) {
        _operator = new SystemShellOperator( NullLogger<SystemShellOperator>.Instance );
    }

    [TestMethod]
    public void IsAvailable_ReturnsTrueOnSupportedPlatform( ) {
        // Windows, Linux, or macOS should report available
        bool expected = OperatingSystem.IsWindows( ) || OperatingSystem.IsLinux( ) || OperatingSystem.IsMacOS( );
        Assert.AreEqual( expected, _operator.IsAvailable );
    }

    [TestMethod]
    public async Task RunCommand_StdOut( ) {
        List<OperatorOutput> outputs = [];

        OperatorExecution execution = _operator.RunCommand( "echo hello", TestContext.CancellationToken );
        await foreach (OperatorOutput output in execution.Output.WithCancellation( TestContext.CancellationToken )) {
            outputs.Add( output );
        }

        Assert.IsNotEmpty( outputs, "Expected at least one output line." );
        Assert.Contains(
            o => o.LogLevel == "Information" && o.Message.Contains( "hello" ),
            outputs, "Expected Information-level output containing 'hello'." );
    }

    [TestMethod]
    public async Task RunCommand_NonZeroExitCode( ) {
        string command = OperatingSystem.IsWindows( )
            ? "cmd /c exit 1"
            : "exit 1";

        List<OperatorOutput> outputs = [];

        OperatorExecution execution = _operator.RunCommand( command, TestContext.CancellationToken );
        await foreach (OperatorOutput output in execution.Output.WithCancellation( TestContext.CancellationToken )) {
            outputs.Add( output );
        }

        Assert.Contains(
            o => o.LogLevel == "Error" && o.Message.Contains( "Exited with code" ),
            outputs, "Expected error output with exit code." );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task RunCommand_Cancellation( ) {
        string command = OperatingSystem.IsWindows( )
            ? "ping -n 300 127.0.0.1"
            : "sleep 300";

        using CancellationTokenSource cts = new( TimeSpan.FromSeconds( 1 ) );
        List<OperatorOutput> outputs = [];

        bool threwCancellation = false;

        try {
            OperatorExecution execution = _operator.RunCommand( command, cts.Token );
            await foreach (OperatorOutput output in execution.Output.WithCancellation( cts.Token )) {
                outputs.Add( output );
            }
        } catch (OperationCanceledException) {
            threwCancellation = true;
        }

        bool observedCancellationWarning = outputs.Any(
            o => o.LogLevel == "Warning" && o.Message.Contains( "Cancelled" ) );

        Assert.IsTrue( threwCancellation || observedCancellationWarning, "Expected cancellation to stop enumeration." );
    }

    [TestMethod]
    public async Task RunCommand_CrossPlatform_UsesCorrectShell( ) {
        // On Windows, "ver" produces Windows version output
        // On Linux/macOS, "uname" produces system name
        string command = OperatingSystem.IsWindows( ) ? "ver" : "uname";

        List<OperatorOutput> outputs = [];

        OperatorExecution execution = _operator.RunCommand( command, TestContext.CancellationToken );
        await foreach (OperatorOutput output in execution.Output.WithCancellation( TestContext.CancellationToken )) {
            outputs.Add( output );
        }

        Assert.Contains(
            o => o.LogLevel == "Information" && o.Message.Length > 0,
            outputs, "Expected at least one Information-level output line." );
    }

    [TestMethod]
    public async Task RunScript_FileNotFound( ) {
        List<OperatorOutput> outputs = [];

        OperatorExecution execution = _operator.RunScript( "C:\\nonexistent\\fake.bat", TestContext.CancellationToken );
        await foreach (OperatorOutput output in execution.Output.WithCancellation( TestContext.CancellationToken )) {
            outputs.Add( output );
        }

        Assert.Contains(
            o => o.LogLevel == "Error" && o.Message.Contains( "not found" ),
            outputs, "Expected error about file not found." );
    }

    [TestMethod]
    public async Task RunCommand_StdErr( ) {
        // Force stderr output on Windows
        string command = OperatingSystem.IsWindows( )
            ? "echo error_msg 1>&2"
            : "echo error_msg >&2";

        List<OperatorOutput> outputs = [];

        OperatorExecution execution = _operator.RunCommand( command, TestContext.CancellationToken );
        await foreach (OperatorOutput output in execution.Output.WithCancellation( TestContext.CancellationToken )) {
            outputs.Add( output );
        }

        Assert.Contains(
            o => o.LogLevel == "Error" && o.Message.Contains( "error_msg" ),
            outputs, "Expected Error-level output from stderr." );
    }
}
