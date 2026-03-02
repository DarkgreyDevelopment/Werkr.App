using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Werkr.Agent.Operators;
using Werkr.Common.Configuration;
using Werkr.Core.Communication;
using Werkr.Core.Operators;

namespace Werkr.Tests.Agent.Operators;

[TestClass]
public class PwshOperatorTests {
    private PwshOperator _operator = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void TestInit( ) {
        _operator = new PwshOperator(
            Options.Create( new AgentSettings( ) ),
            NullLogger<PwshOperator>.Instance );
    }

    [TestMethod]
    public void IsAvailable_ReturnsTrue( ) {
        Assert.IsTrue( _operator.IsAvailable );
    }

    [TestMethod]
    public async Task RunCommand_ProducesOutput( ) {
        List<OperatorOutput> outputs = [];

        OperatorExecution execution = _operator.RunCommand( "Write-Output 'hello'", TestContext.CancellationToken );
        await foreach (OperatorOutput output in execution.Output.WithCancellation( TestContext.CancellationToken )) {
            outputs.Add( output );
        }

        Assert.IsNotEmpty( outputs, "Expected at least one output line." );
        Assert.Contains(
            o => o.LogLevel == "Information" && o.Message.Contains( "hello" ),
            outputs, "Expected Information-level output containing 'hello'." );
    }

    [TestMethod]
    public async Task RunCommand_ErrorStream( ) {
        List<OperatorOutput> outputs = [];

        OperatorExecution execution = _operator.RunCommand( "Write-Error 'fail'", TestContext.CancellationToken );
        await foreach (OperatorOutput output in execution.Output.WithCancellation( TestContext.CancellationToken )) {
            outputs.Add( output );
        }

        Assert.Contains(
            o => o.LogLevel == "Error" && o.Message.Contains( "fail" ),
            outputs, "Expected Error-level output containing 'fail'." );
    }

    [TestMethod]
    public async Task RunCommand_MultipleStreams( ) {
        List<OperatorOutput> outputs = [];

        OperatorExecution execution = _operator.RunCommand(
            "Write-Output 'out'; Write-Warning 'warn'; Write-Error 'err'", TestContext.CancellationToken );
        await foreach (OperatorOutput output in execution.Output.WithCancellation( TestContext.CancellationToken )) {
            outputs.Add( output );
        }

        Assert.Contains(
            o => o.LogLevel == "Information" && o.Message.Contains( "out" ),
            outputs, "Expected Information-level output." );
        Assert.Contains(
            o => o.LogLevel == "Warning" && o.Message.Contains( "warn" ),
            outputs, "Expected Warning-level output." );
        Assert.Contains(
            o => o.LogLevel == "Error" && o.Message.Contains( "err" ),
            outputs, "Expected Error-level output." );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task RunCommand_Cancellation( ) {
        using CancellationTokenSource cts = new( TimeSpan.FromSeconds( 1 ) );
        List<OperatorOutput> outputs = [];

        bool threwCancellation = false;

        try {
            OperatorExecution execution = _operator.RunCommand( "Start-Sleep 300", cts.Token );
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

        Assert.IsTrue( threwCancellation || observedCancellationWarning, "Expected cancellation to stop enumeration." );
    }

    [TestMethod]
    public async Task RunScript_FileNotFound( ) {
        List<OperatorOutput> outputs = [];

        OperatorExecution execution = _operator.RunScript( "C:\\nonexistent\\fake.ps1", TestContext.CancellationToken );
        await foreach (OperatorOutput output in execution.Output.WithCancellation( TestContext.CancellationToken )) {
            outputs.Add( output );
        }

        Assert.Contains(
            o => o.LogLevel == "Error" && o.Message.Contains( "not found" ),
            outputs, "Expected error about file not found." );
    }
}
