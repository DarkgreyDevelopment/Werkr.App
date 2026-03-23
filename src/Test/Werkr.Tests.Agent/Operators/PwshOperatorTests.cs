using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Werkr.Agent.Operators;
using Werkr.Common.Configuration;
using Werkr.Core.Communication;
using Werkr.Core.Operators;

namespace Werkr.Tests.Agent.Operators;

/// <summary>
/// Unit tests for the <see cref="PwshOperator"/> shell operator. Validates
/// that the operator reports availability, executes PowerShell commands with
/// output and error stream capture, handles multiple simultaneous streams,
/// respects cancellation, and reports script-not-found errors.
/// </summary>
[TestClass]
public class PwshOperatorTests {
    /// <summary>
    /// The operator instance under test, created fresh for each test.
    /// </summary>
    private PwshOperator _operator = null!;

    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> for the current test run.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Creates a new <see cref="PwshOperator"/> with default <see cref="AgentSettings"/>.
    /// </summary>
    [TestInitialize]
    public void TestInit( ) {
        _operator = new PwshOperator(
            Options.Create( new AgentSettings( ) ),
            NullLogger<PwshOperator>.Instance
        );
    }

    /// <summary>
    /// Verifies that <see cref="PwshOperator.IsAvailable"/> returns <see langword="true"/> for the PowerShell operator.
    /// </summary>
    [TestMethod]
    public void IsAvailable_ReturnsTrue( ) {
        Assert.IsTrue( _operator.IsAvailable );
    }

    /// <summary>
    /// Verifies that <see cref="PwshOperator.RunCommand"/> with a simple
    /// <c>Write-Output</c> produces at least one Information-level output
    /// containing the expected text.
    /// </summary>
    [TestMethod]
    public async Task RunCommand_ProducesOutput( ) {
        List<OperatorOutput> outputs = [];

        OperatorExecution execution = _operator.RunCommand(
            "Write-Output 'hello'",
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
    /// Verifies that <c>Write-Error</c> output is captured at the Error log level.
    /// </summary>
    [TestMethod]
    public async Task RunCommand_ErrorStream( ) {
        List<OperatorOutput> outputs = [];

        OperatorExecution execution = _operator.RunCommand(
            "Write-Error 'fail'",
            cancellationToken: TestContext.CancellationToken
        );
        await foreach (OperatorOutput output in execution.Output.WithCancellation( TestContext.CancellationToken )) {
            outputs.Add( output );
        }

        Assert.Contains(
            o => o.LogLevel == "Error" && o.Message.Contains( "fail" ),
            outputs,
            "Expected Error-level output containing 'fail'."
        );
    }

    /// <summary>
    /// Verifies that a command emitting to Output, Warning, and Error
    /// streams simultaneously produces output at the corresponding
    /// log levels.
    /// </summary>
    [TestMethod]
    public async Task RunCommand_MultipleStreams( ) {
        List<OperatorOutput> outputs = [];

        OperatorExecution execution = _operator.RunCommand(
            "Write-Output 'out'; Write-Warning 'warn'; Write-Error 'err'",
            cancellationToken: TestContext.CancellationToken
        );
        await foreach (OperatorOutput output in execution.Output.WithCancellation( TestContext.CancellationToken )) {
            outputs.Add( output );
        }

        Assert.Contains(
            o => o.LogLevel == "Information" && o.Message.Contains( "out" ),
            outputs,
            "Expected Information-level output."
        );
        Assert.Contains(
            o => o.LogLevel == "Warning" && o.Message.Contains( "warn" ),
            outputs,
            "Expected Warning-level output."
        );
        Assert.Contains(
            o => o.LogLevel == "Error" && o.Message.Contains( "err" ),
            outputs,
            "Expected Error-level output."
        );
    }

    /// <summary>
    /// Verifies that cancelling the token during a long-running
    /// <c>Start-Sleep</c> command stops enumeration, either via an
    /// <see cref="OperationCanceledException"/> or a cancellation warning
    /// in the output stream.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task RunCommand_Cancellation( ) {
        using CancellationTokenSource cts = new( TimeSpan.FromSeconds( 1 ) );
        List<OperatorOutput> outputs = [];

        bool threwCancellation = false;

        try {
            OperatorExecution execution = _operator.RunCommand(
                "Start-Sleep 300",
                cancellationToken: cts.Token
            );
            await foreach (OperatorOutput output in execution.Output.WithCancellation( cts.Token )) {
                outputs.Add( output );
            }
        } catch (OperationCanceledException) {
            threwCancellation = true;
        }

        // The operator writes a "Cancelled" warning, but the channel reader is enumerated with the same
        // cancellation token and may throw before the warning is observed. Either outcome is acceptable.
        bool observedCancellationWarning = outputs.Any(
            o => o.LogLevel == "Warning" && o.Message.Contains( "Cancelled" ) );

        Assert.IsTrue(
            threwCancellation || observedCancellationWarning,
            "Expected cancellation to stop enumeration."
        );
    }

    /// <summary>
    /// Verifies that <see cref="PwshOperator.RunScript"/> with a non-existent
    /// script file path produces an Error-level output containing
    /// "not found".
    /// </summary>
    [TestMethod]
    public async Task RunScript_FileNotFound( ) {
        List<OperatorOutput> outputs = [];

        OperatorExecution execution = _operator.RunScript(
            "C:\\nonexistent\\fake.ps1",
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
}
