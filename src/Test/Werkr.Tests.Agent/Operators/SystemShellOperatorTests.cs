using Werkr.Agent.Operators;
using Werkr.Core.Communication;
using Werkr.Core.Operators;

namespace Werkr.Tests.Agent.Operators;

/// <summary>
/// Unit tests for the <see cref="SystemShellOperator"/> shell operator.
/// Validates platform availability, standard output and standard error
/// capture, non-zero exit-code handling, cancellation behavior,
/// cross-platform shell selection, and script-not-found error reporting.
/// </summary>
[TestClass]
public class SystemShellOperatorTests {
    /// <summary>
    /// The operator instance under test, created fresh for each test.
    /// </summary>
    private SystemShellOperator _operator = null!;

    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> for the current test run.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Creates a new <see cref="SystemShellOperator"/> with a null logger.
    /// </summary>
    [TestInitialize]
    public void TestInit( ) {
        _operator = new SystemShellOperator( NullLogger<SystemShellOperator>.Instance );
    }

    /// <summary>
    /// Verifies that <see cref="SystemShellOperator.IsAvailable"/> returns
    /// <see langword="true"/> on supported platforms (Windows, Linux, macOS)
    /// and <see langword="false"/> otherwise.
    /// </summary>
    [TestMethod]
    public void IsAvailable_ReturnsTrueOnSupportedPlatform( ) {
        // Windows, Linux, or macOS should report available
        bool expected = OperatingSystem.IsWindows( ) || OperatingSystem.IsLinux( ) || OperatingSystem.IsMacOS( );
        Assert.AreEqual(
            expected,
            _operator.IsAvailable
        );
    }

    /// <summary>
    /// Verifies that running an echo command produces at least one
    /// Information-level output line containing the expected text.
    /// </summary>
    [TestMethod]
    public async Task RunCommand_StdOut( ) {
        List<OperatorOutput> outputs = [];

        OperatorExecution execution = _operator.RunCommand(
            "echo hello",
            cancellationToken: TestContext.CancellationToken
        );
        await foreach (OperatorOutput output in execution.Output.WithCancellation( TestContext.CancellationToken )) {
            outputs.Add( output );
        }

        Assert.IsNotEmpty(
            outputs,
            "Expected at least one output line."
        );
        Assert.Contains(
            o => o.LogLevel == "Information" && o.Message.Contains( "hello" ),
            outputs,
            "Expected Information-level output containing 'hello'."
        );
    }

    /// <summary>
    /// Verifies that a command exiting with a non-zero exit code produces
    /// an Error-level output containing "Exited with code".
    /// </summary>
    [TestMethod]
    public async Task RunCommand_NonZeroExitCode( ) {
        string command = OperatingSystem.IsWindows( )
            ? "cmd /c exit 1"
            : "exit 1";

        List<OperatorOutput> outputs = [];

        OperatorExecution execution = _operator.RunCommand(
            command,
            cancellationToken: TestContext.CancellationToken
        );
        await foreach (OperatorOutput output in execution.Output.WithCancellation( TestContext.CancellationToken )) {
            outputs.Add( output );
        }

        Assert.Contains(
            o => o.LogLevel == "Error" && o.Message.Contains( "Exited with code" ),
            outputs,
            "Expected error output with exit code."
        );
    }

    /// <summary>
    /// Verifies that cancelling the token during a long-running command stops
    /// enumeration, either via an <see cref="OperationCanceledException"/> or
    /// a cancellation warning in the output stream.
    /// </summary>
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
            OperatorExecution execution = _operator.RunCommand(
                command,
                cancellationToken: cts.Token
            );
            await foreach (OperatorOutput output in execution.Output.WithCancellation( cts.Token )) {
                outputs.Add( output );
            }
        } catch (OperationCanceledException) {
            threwCancellation = true;
        }

        bool observedCancellationWarning = outputs.Any(
            o => o.LogLevel == "Warning" && o.Message.Contains( "Cancelled" ) );

        Assert.IsTrue(
            threwCancellation || observedCancellationWarning,
            "Expected cancellation to stop enumeration."
        );
    }

    /// <summary>
    /// Verifies that the operator selects the correct platform-specific shell
    /// (cmd.exe on Windows, /bin/sh on Linux/macOS) and produces at least one
    /// Information-level output line.
    /// </summary>
    [TestMethod]
    public async Task RunCommand_CrossPlatform_UsesCorrectShell( ) {
        // On Windows, "ver" produces Windows version output
        // On Linux/macOS, "uname" produces system name
        string command = OperatingSystem.IsWindows( ) ? "ver" : "uname";

        List<OperatorOutput> outputs = [];

        OperatorExecution execution = _operator.RunCommand(
            command,
            cancellationToken: TestContext.CancellationToken
        );
        await foreach (OperatorOutput output in execution.Output.WithCancellation( TestContext.CancellationToken )) {
            outputs.Add( output );
        }

        Assert.Contains(
            o => o.LogLevel == "Information" && o.Message.Length > 0,
            outputs,
            "Expected at least one Information-level output line."
        );
    }

    /// <summary>
    /// Verifies that <see cref="SystemShellOperator.RunScript"/> with a
    /// non-existent script file path produces an Error-level output
    /// containing "not found".
    /// </summary>
    [TestMethod]
    public async Task RunScript_FileNotFound( ) {
        List<OperatorOutput> outputs = [];

        OperatorExecution execution = _operator.RunScript(
            "C:\\nonexistent\\fake.bat",
            cancellationToken: TestContext.CancellationToken
        );
        await foreach (OperatorOutput output in execution.Output.WithCancellation( TestContext.CancellationToken )) {
            outputs.Add( output );
        }

        Assert.Contains(
            o => o.LogLevel == "Error" && o.Message.Contains( "not found" ),
            outputs,
            "Expected error about file not found."
        );
    }

    /// <summary>
    /// Verifies that standard error output is captured at the Error log level.
    /// </summary>
    [TestMethod]
    public async Task RunCommand_StdErr( ) {
        // Force stderr output on Windows
        string command = OperatingSystem.IsWindows( )
            ? "echo error_msg 1>&2"
            : "echo error_msg >&2";

        List<OperatorOutput> outputs = [];

        OperatorExecution execution = _operator.RunCommand(
            command,
            cancellationToken: TestContext.CancellationToken
        );
        await foreach (OperatorOutput output in execution.Output.WithCancellation( TestContext.CancellationToken )) {
            outputs.Add( output );
        }

        Assert.Contains(
            o => o.LogLevel == "Error" && o.Message.Contains( "error_msg" ),
            outputs,
            "Expected Error-level output from stderr."
        );
    }
}
