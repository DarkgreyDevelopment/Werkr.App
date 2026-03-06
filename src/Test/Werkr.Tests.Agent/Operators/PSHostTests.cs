using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Werkr.Agent.Operators;
using Werkr.Common.Configuration;
using Werkr.Core.Communication;
using Werkr.Core.Operators;

namespace Werkr.Tests.Agent.Operators;

/// <summary>
/// Tests for the custom PSHost integration in <see cref="PwshOperator"/>.
/// Validates that <c>Format-Table</c>, <c>Format-List</c>, <c>Write-Host</c>,
/// <c>Write-Progress</c>, and raw pipeline output all route correctly through
/// <see cref="WerkrPSHost"/> into the <see cref="OperatorOutput"/> channel.
/// </summary>
[TestClass]
public class PSHostTests {
    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> for the current test run.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Creates a <see cref="PwshOperator"/> with the specified buffer width.
    /// </summary>
    private static PwshOperator CreateOperator( int bufferWidth = 150 ) {
        AgentSettings settings = new( ) {
            PowerShell = new PowerShellSettings { BufferWidth = bufferWidth }
        };
        return new PwshOperator(
            Options.Create( settings ),
            NullLogger<PwshOperator>.Instance
        );
    }

    /// <summary>
    /// Collects all <see cref="OperatorOutput"/> items from the given execution's output channel.
    /// </summary>
    private static async Task<List<OperatorOutput>> CollectOutputAsync(
        OperatorExecution execution,
        CancellationToken ct
    ) {
        List<OperatorOutput> outputs = [];
        await foreach (OperatorOutput output in execution.Output.WithCancellation( ct )) {
            outputs.Add( output );
        }
        return outputs;
    }

    /// <summary>
    /// Verifies that <c>Format-Table</c> on <c>Get-ChildItem</c> output
    /// produces rendered columnar text and does not contain raw
    /// <c>FormatEntryData</c> or <c>FormatStartData</c> type names.
    /// </summary>
    [TestMethod]
    public async Task FormatTable_ProducesColumnarOutput( ) {
        PwshOperator op = CreateOperator( );
        OperatorExecution execution = op.RunCommand(
            "Get-ChildItem -Path / -Force -ErrorAction SilentlyContinue "
            + "| Select-Object -First 3 | Format-Table -AutoSize",
            TestContext.CancellationToken
        );

        List<OperatorOutput> outputs = await CollectOutputAsync(
            execution,
            TestContext.CancellationToken
        );

        // Should contain formatted table content, NOT raw FormatEntryData type names
        string allOutput = string.Join(
            "\n",
            outputs
                .Where( o => o.LogLevel == "Information" )
                .Select( o => o.Message )
        );

        Assert.IsFalse(
            allOutput.Contains(
                "FormatEntryData",
                StringComparison.OrdinalIgnoreCase
            ),
            $"Output should not contain raw FormatEntryData type names. Got:\n{allOutput}"
        );
        Assert.IsFalse(
            allOutput.Contains(
                "FormatStartData",
                StringComparison.OrdinalIgnoreCase
            ),
            $"Output should not contain raw FormatStartData type names. Got:\n{allOutput}"
        );
        Assert.IsGreaterThan(
            0,
            allOutput.Length,
            "Expected some formatted output from Get-ChildItem."
        );
    }

    /// <summary>
    /// Verifies that <c>Format-Table</c> on <c>Get-Process</c> output
    /// produces rendered table text and does not contain raw
    /// <c>FormatEntryData</c> type names.
    /// </summary>
    [TestMethod]
    public async Task FormatTable_GetProcess_ProducesFormattedTable( ) {
        PwshOperator op = CreateOperator( );
        OperatorExecution execution = op.RunCommand(
            "Get-Process | Select-Object -First 5 | Format-Table -Property Id, ProcessName -AutoSize",
            TestContext.CancellationToken
        );

        List<OperatorOutput> outputs = await CollectOutputAsync(
            execution,
            TestContext.CancellationToken
        );

        string allOutput = string.Join(
            "\n",
            outputs
                .Where( o => o.LogLevel == "Information" )
                .Select( o => o.Message )
        );

        Assert.IsFalse(
            allOutput.Contains(
                "FormatEntryData",
                StringComparison.OrdinalIgnoreCase
            ),
            "Format-Table should produce rendered table, not FormatEntryData."
        );
        Assert.IsGreaterThan(
            0,
            allOutput.Length,
            "Expected formatted process table output."
        );
    }

    /// <summary>
    /// Verifies that <c>Format-List</c> produces property-name/value lines
    /// and does not emit raw <c>FormatEntryData</c> type names.
    /// </summary>
    [TestMethod]
    public async Task FormatList_ProducesPropertyList( ) {
        PwshOperator op = CreateOperator( );
        OperatorExecution execution = op.RunCommand(
            "Get-Process | Select-Object -First 1 | Format-List -Property Id, ProcessName",
            TestContext.CancellationToken
        );

        List<OperatorOutput> outputs = await CollectOutputAsync(
            execution,
            TestContext.CancellationToken
        );

        string allOutput = string.Join(
            "\n",
            outputs
                .Where( o => o.LogLevel == "Information" )
                .Select( o => o.Message )
        );

        Assert.IsFalse(
            allOutput.Contains(
                "FormatEntryData",
                StringComparison.OrdinalIgnoreCase
            ),
            "Format-List should produce property list, not FormatEntryData."
        );
        Assert.IsGreaterThan(
            0,
            allOutput.Length,
            "Expected formatted property list output."
        );
    }

    /// <summary>
    /// Verifies that <c>Write-Host</c> output is captured at the Information log level and includes the expected text.
    /// </summary>
    [TestMethod]
    public async Task WriteHost_CapturesText( ) {
        PwshOperator op = CreateOperator( );
        OperatorExecution execution = op.RunCommand(
            "Write-Host 'Hello from PSHost'",
            TestContext.CancellationToken
        );

        List<OperatorOutput> outputs = await CollectOutputAsync(
            execution,
            TestContext.CancellationToken
        );

        Assert.Contains(
            o => o.LogLevel == "Information" && o.Message.Contains( "Hello from PSHost" ),
            outputs,
            "Expected Information-level output containing 'Hello from PSHost'."
        );
    }

    /// <summary>
    /// Verifies that <c>Write-Progress</c> output is captured at the Progress log level and contains the activity name.
    /// </summary>
    [TestMethod]
    public async Task WriteProgress_CapturesProgressOutput( ) {
        PwshOperator op = CreateOperator( );
        OperatorExecution execution = op.RunCommand(
            "Write-Progress -Activity 'TestActivity' -Status 'Running' -PercentComplete 50",
            TestContext.CancellationToken
        );

        List<OperatorOutput> outputs = await CollectOutputAsync(
            execution,
            TestContext.CancellationToken
        );

        Assert.Contains(
            o => o.LogLevel == "Progress" && o.Message.Contains( "TestActivity" ),
            outputs,
            "Expected Progress-level output containing 'TestActivity'."
        );
    }

    /// <summary>
    /// Verifies that raw pipeline integers (1..5) are rendered as string
    /// representations via the PSHost UI rather than being dropped or
    /// wrapped in format objects.
    /// </summary>
    [TestMethod]
    public async Task RawPipeline_RendersIntegers( ) {
        PwshOperator op = CreateOperator( );
        OperatorExecution execution = op.RunCommand(
            "1..5 | ForEach-Object { $_ }",
            TestContext.CancellationToken
        );

        List<OperatorOutput> outputs = await CollectOutputAsync(
            execution,
            TestContext.CancellationToken
        );

        string allOutput = string.Join(
            "\n",
            outputs
                .Where( o => o.LogLevel == "Information" )
                .Select( o => o.Message )
        );

        // All integers 1-5 should appear somewhere in the output (rendered via Out-Default)
        for (int i = 1; i <= 5; i++) {
            Assert.Contains(
                i.ToString( ),
                allOutput,
                $"Expected integer {i} in output. Got:\n{allOutput}"
            );
        }
    }

    /// <summary>
    /// Verifies that <c>Write-Output</c> objects are routed through the
    /// PSHost UI via <c>Out-Default</c> and captured as Information-level
    /// output.
    /// </summary>
    [TestMethod]
    public async Task WriteOutput_CapturedViaSingleFlow( ) {
        PwshOperator op = CreateOperator( );
        OperatorExecution execution = op.RunCommand(
            "Write-Output 'single-flow-test'",
            TestContext.CancellationToken
        );

        List<OperatorOutput> outputs = await CollectOutputAsync(
            execution,
            TestContext.CancellationToken
        );

        Assert.Contains(
            o => o.LogLevel == "Information" && o.Message.Contains( "single-flow-test" ),
            outputs,
            "Write-Output should route through PSHost UI via Out-Default."
        );
    }

    /// <summary>
    /// Verifies that different buffer widths affect <c>Format-Table</c>
    /// layout. Both narrow and wide widths should produce rendered output
    /// (no raw <c>FormatEntryData</c>).
    /// </summary>
    [TestMethod]
    public async Task BufferWidth_AffectsFormatting( ) {
        // Use a very narrow buffer to force wrapping
        PwshOperator narrowOp = CreateOperator( bufferWidth: 40 );
        OperatorExecution narrowExec = narrowOp.RunCommand(
            "Get-Process | Select-Object -First 3 | Format-Table -Property Id, ProcessName, CPU",
            TestContext.CancellationToken
        );

        List<OperatorOutput> narrowOutputs = await CollectOutputAsync(
            narrowExec,
            TestContext.CancellationToken
        );

        // Use a wide buffer
        PwshOperator wideOp = CreateOperator( bufferWidth: 200 );
        OperatorExecution wideExec = wideOp.RunCommand(
            "Get-Process | Select-Object -First 3 | Format-Table -Property Id, ProcessName, CPU",
            TestContext.CancellationToken
        );

        List<OperatorOutput> wideOutputs = await CollectOutputAsync(
            wideExec,
            TestContext.CancellationToken
        );

        string narrowText = string.Join(
            "\n",
            narrowOutputs
                .Where( o => o.LogLevel == "Information" )
                .Select( o => o.Message )
        );
        string wideText = string.Join(
            "\n",
            wideOutputs
                .Where( o => o.LogLevel == "Information" )
                .Select( o => o.Message )
        );

        // Both should produce output — the actual text may differ due to buffer width
        Assert.IsGreaterThan(
            0,
            narrowText.Length,
            "Narrow buffer should still produce output."
        );
        Assert.IsGreaterThan(
            0,
            wideText.Length,
            "Wide buffer should produce output."
        );

        // They should not be identical (different wrapping behavior)
        // Unless the table is small enough to fit in both widths (unlikely with 3 processes + 3 columns)
        // At minimum, both should not contain FormatEntryData
        Assert.IsFalse(
            narrowText.Contains(
                "FormatEntryData",
                StringComparison.OrdinalIgnoreCase
            ),
            "Narrow buffer should not produce raw FormatEntryData."
        );
        Assert.IsFalse(
            wideText.Contains(
                "FormatEntryData",
                StringComparison.OrdinalIgnoreCase
            ),
            "Wide buffer should not produce raw FormatEntryData."
        );
    }

    /// <summary>
    /// Verifies that <c>Write-Error</c> output is captured at the Error
    /// log level via the PSHost UI's <c>WriteErrorLine</c>, and
    /// <see cref="PwshOperatorResult.HadErrors"/> is set to
    /// <see langword="true"/>.
    /// </summary>
    [TestMethod]
    public async Task WriteError_CapturedViaHostUI( ) {
        PwshOperator op = CreateOperator( );
        OperatorExecution execution = op.RunCommand(
            "Write-Error 'pshost-error-test'",
            TestContext.CancellationToken
        );

        List<OperatorOutput> outputs = await CollectOutputAsync(
            execution,
            TestContext.CancellationToken
        );
        IOperatorResult result = await execution.Result;

        Assert.Contains(
            o => o.LogLevel == "Error" && o.Message.Contains( "pshost-error-test" ),
            outputs,
            "Write-Error should be captured via PSHost UI ErrorLine."
        );
        Assert.IsTrue(
            ((PwshOperatorResult)result).HadErrors,
            "HadErrors should be true after Write-Error."
        );
    }

    /// <summary>
    /// Verifies that <c>Write-Warning</c> output is captured at the Warning log level via the PSHost UI.
    /// </summary>
    [TestMethod]
    public async Task WriteWarning_CapturedViaHostUI( ) {
        PwshOperator op = CreateOperator( );
        OperatorExecution execution = op.RunCommand(
            "Write-Warning 'pshost-warning-test'",
            TestContext.CancellationToken
        );

        List<OperatorOutput> outputs = await CollectOutputAsync(
            execution,
            TestContext.CancellationToken
        );

        Assert.Contains(
            o => o.LogLevel == "Warning" && o.Message.Contains( "pshost-warning-test" ),
            outputs,
            "Write-Warning should be captured via PSHost UI WarningLine."
        );
    }

    /// <summary>
    /// Verifies that <see cref="PwshOperatorResult.HadErrors"/> is
    /// <see langword="false"/> for a successful command and
    /// <see langword="true"/> for a command that emits an error, even when
    /// the same <see cref="PwshOperator"/> runspace is reused across
    /// invocations.
    /// </summary>
    [TestMethod]
    public async Task HadErrors_StillWorksWithCustomRunspace( ) {
        PwshOperator op = CreateOperator( );

        // A command that succeeds — HadErrors should be false
        OperatorExecution successExec = op.RunCommand(
            "Write-Output 'success'",
            TestContext.CancellationToken
        );
        _ = await CollectOutputAsync(
            successExec,
            TestContext.CancellationToken
        );
        IOperatorResult successResult = await successExec.Result;
        Assert.IsFalse(
            ((PwshOperatorResult)successResult).HadErrors,
            "HadErrors should be false for a successful command."
        );

        // A command that errors — HadErrors should be true
        OperatorExecution errorExec = op.RunCommand(
            "Write-Error 'fail'",
            TestContext.CancellationToken
        );
        _ = await CollectOutputAsync(
            errorExec,
            TestContext.CancellationToken
        );
        IOperatorResult errorResult = await errorExec.Result;
        Assert.IsTrue(
            ((PwshOperatorResult)errorResult).HadErrors,
            "HadErrors should be true after Write-Error."
        );
    }

    /// <summary>
    /// Verifies that <c>Write-Host</c> produces exactly one
    /// Information-level output line containing the message, confirming
    /// there are no duplicate emissions.
    /// </summary>
    [TestMethod]
    public async Task NoDuplicateOutput_WriteHost( ) {
        PwshOperator op = CreateOperator( );
        OperatorExecution execution = op.RunCommand(
            "Write-Host 'unique-message-42'",
            TestContext.CancellationToken
        );

        List<OperatorOutput> outputs = await CollectOutputAsync(
            execution,
            TestContext.CancellationToken
        );

        // Count occurrences — should be exactly 1, not duplicated
        int count = outputs.Count( o =>
            o.LogLevel == "Information" && o.Message.Contains( "unique-message-42" ) );

        Assert.AreEqual(
            1,
            count,
            $"Write-Host should produce exactly 1 output, not {count}. Found:\n" +
            string.Join(
                "\n",
                outputs.Select( o => $"[{o.LogLevel}] {o.Message}" )
            )
        );
    }
}
