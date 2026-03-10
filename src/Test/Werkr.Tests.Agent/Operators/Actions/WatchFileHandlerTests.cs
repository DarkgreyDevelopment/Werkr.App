using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Werkr.Agent.Operators.Actions;
using Werkr.Common.Models;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Operators.Actions;

/// <summary>
/// Unit tests for the <see cref="WatchFileHandler"/> action handler.
/// Validates FSW-mode detection, polling-mode detection, stability checks,
/// timeout behavior, and cancellation.
/// </summary>
[TestClass]
public class WatchFileHandlerTests {

    /// <summary>
    /// Temporary directory created for each test and cleaned up afterward.
    /// </summary>
    private string _tempDir = null!;
    /// <summary>
    /// Unbounded channel used to capture <see cref="OperatorOutput"/> messages.
    /// </summary>
    private Channel<OperatorOutput> _channel = null!;

    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> for the current test run.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Creates a unique temporary directory and an unbounded output channel.
    /// </summary>
    [TestInitialize]
    public void TestInit( ) {
        _tempDir = Path.Combine(
            Path.GetTempPath( ),
            $"werkr-test-{Guid.NewGuid( )}"
        );
        _ = Directory.CreateDirectory( _tempDir );
        _channel = Channel.CreateUnbounded<OperatorOutput>( );
    }

    /// <summary>
    /// Deletes the temporary directory and all its contents.
    /// </summary>
    [TestCleanup]
    public void TestCleanup( ) {
        if (Directory.Exists( _tempDir )) {
            Directory.Delete(
                _tempDir,
                recursive: true
            );
        }
    }

    /// <summary>
    /// Serializes a value to a <see cref="JsonElement"/> using the shared test serializer.
    /// </summary>
    private static JsonElement Serialize<T>( T value ) =>
        TestActionDescriptor.Serialize( value );

    /// <summary>
    /// Verifies that a pre-existing matching file is detected immediately in FSW mode.
    /// </summary>
    [TestMethod]
    [Timeout( 30_000, CooperativeCancellation = true )]
    public async Task WatchFile_PreExistingFile_FswMode_Succeeds( ) {
        // Create file before starting the watch
        string filePath = Path.Combine( _tempDir, "data.csv" );
        await File.WriteAllTextAsync( filePath, "content", TestContext.CancellationToken );

        WatchFileHandler handler = new(
            TestFilePathResolver.AllowAll,
            NullLogger<WatchFileHandler>.Instance,
            TimeProvider.System
        );

        JsonElement parameters = Serialize( new WatchFileParameters {
            Directory = _tempDir,
            Pattern = "*.csv",
            StabilitySeconds = 1,
            TimeoutSeconds = 10,
            PollIntervalMs = 100
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
    }

    /// <summary>
    /// Verifies that a file created after watch starts is detected in FSW mode.
    /// </summary>
    [TestMethod]
    [Timeout( 30_000, CooperativeCancellation = true )]
    public async Task WatchFile_NewFile_FswMode_Succeeds( ) {
        WatchFileHandler handler = new(
            TestFilePathResolver.AllowAll,
            NullLogger<WatchFileHandler>.Instance,
            TimeProvider.System
        );

        JsonElement parameters = Serialize( new WatchFileParameters {
            Directory = _tempDir,
            Pattern = "*.csv",
            StabilitySeconds = 1,
            TimeoutSeconds = 10,
            PollIntervalMs = 100
        } );

        // Start watching, then create the file shortly after
        Task<ActionOperatorResult> watchTask = handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        await Task.Delay( 200, TestContext.CancellationToken );
        string filePath = Path.Combine( _tempDir, "report.csv" );
        await File.WriteAllTextAsync( filePath, "content", TestContext.CancellationToken );

        ActionOperatorResult result = await watchTask;

        Assert.IsTrue( result.Success );
    }

    /// <summary>
    /// Verifies that polling mode detects a pre-existing file.
    /// </summary>
    [TestMethod]
    [Timeout( 30_000, CooperativeCancellation = true )]
    public async Task WatchFile_PreExistingFile_PollingMode_Succeeds( ) {
        string filePath = Path.Combine( _tempDir, "data.csv" );
        await File.WriteAllTextAsync( filePath, "content", TestContext.CancellationToken );

        WatchFileHandler handler = new(
            TestFilePathResolver.AllowAll,
            NullLogger<WatchFileHandler>.Instance,
            TimeProvider.System
        );

        JsonElement parameters = Serialize( new WatchFileParameters {
            Directory = _tempDir,
            Pattern = "*.csv",
            StabilitySeconds = 1,
            TimeoutSeconds = 10,
            PollIntervalMs = 100,
            UsePolling = true
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
    }

    /// <summary>
    /// Verifies that FailOnTimeout returns failure when no file appears.
    /// </summary>
    [TestMethod]
    [Timeout( 30_000, CooperativeCancellation = true )]
    public async Task WatchFile_Timeout_FailOnTimeout_ReturnsFailure( ) {
        WatchFileHandler handler = new(
            TestFilePathResolver.AllowAll,
            NullLogger<WatchFileHandler>.Instance,
            TimeProvider.System
        );

        JsonElement parameters = Serialize( new WatchFileParameters {
            Directory = _tempDir,
            Pattern = "*.csv",
            StabilitySeconds = 1,
            TimeoutSeconds = 2,
            PollIntervalMs = 100,
            UsePolling = true,
            Mode = WatchFileMode.FailOnTimeout
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        _ = Assert.IsInstanceOfType<TimeoutException>( result.Exception );
    }

    /// <summary>
    /// Verifies that ExitQuietly returns success when no file appears within timeout.
    /// </summary>
    [TestMethod]
    [Timeout( 30_000, CooperativeCancellation = true )]
    public async Task WatchFile_Timeout_ExitQuietly_ReturnsSuccess( ) {
        WatchFileHandler handler = new(
            TestFilePathResolver.AllowAll,
            NullLogger<WatchFileHandler>.Instance,
            TimeProvider.System
        );

        JsonElement parameters = Serialize( new WatchFileParameters {
            Directory = _tempDir,
            Pattern = "*.csv",
            StabilitySeconds = 1,
            TimeoutSeconds = 2,
            PollIntervalMs = 100,
            UsePolling = true,
            Mode = WatchFileMode.ExitQuietly
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
    }

    /// <summary>
    /// Verifies that a non-matching pattern does not trigger detection.
    /// </summary>
    [TestMethod]
    [Timeout( 30_000, CooperativeCancellation = true )]
    public async Task WatchFile_NonMatchingPattern_TimesOut( ) {
        string filePath = Path.Combine( _tempDir, "data.txt" );
        await File.WriteAllTextAsync( filePath, "content", TestContext.CancellationToken );

        WatchFileHandler handler = new(
            TestFilePathResolver.AllowAll,
            NullLogger<WatchFileHandler>.Instance,
            TimeProvider.System
        );

        JsonElement parameters = Serialize( new WatchFileParameters {
            Directory = _tempDir,
            Pattern = "*.csv",  // won't match .txt file
            StabilitySeconds = 1,
            TimeoutSeconds = 2,
            PollIntervalMs = 100,
            UsePolling = true,
            Mode = WatchFileMode.FailOnTimeout
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
    }

    /// <summary>
    /// Verifies that cancellation during a watch propagates as OperationCanceledException.
    /// </summary>
    [TestMethod]
    [Timeout( 30_000, CooperativeCancellation = true )]
    public async Task WatchFile_Cancelled_ThrowsOperationCanceled( ) {
        WatchFileHandler handler = new(
            TestFilePathResolver.AllowAll,
            NullLogger<WatchFileHandler>.Instance,
            TimeProvider.System
        );

        JsonElement parameters = Serialize( new WatchFileParameters {
            Directory = _tempDir,
            Pattern = "*.csv",
            StabilitySeconds = 1,
            TimeoutSeconds = 300,
            PollIntervalMs = 100,
            UsePolling = true
        } );

        using CancellationTokenSource cts = new( );

        Task<ActionOperatorResult> task = handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: cts.Token
        );

        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );

        _ = await Assert.ThrowsExactlyAsync<TaskCanceledException>( ( ) => task );
    }

    /// <summary>
    /// Verifies that a denied directory path returns failure.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task WatchFile_DeniedPath_ReturnsFailure( ) {
        WatchFileHandler handler = new(
            TestFilePathResolver.DenyAll,
            NullLogger<WatchFileHandler>.Instance,
            TimeProvider.System
        );

        JsonElement parameters = Serialize( new WatchFileParameters {
            Directory = "/some/path",
            Pattern = "*.csv"
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        _ = Assert.IsInstanceOfType<UnauthorizedAccessException>( result.Exception );
    }

    /// <summary>
    /// Verifies that a non-existent directory returns failure.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task WatchFile_NonExistentDirectory_ReturnsFailure( ) {
        string missingDir = Path.Combine( _tempDir, "does-not-exist" );

        WatchFileHandler handler = new(
            TestFilePathResolver.AllowAll,
            NullLogger<WatchFileHandler>.Instance,
            TimeProvider.System
        );

        JsonElement parameters = Serialize( new WatchFileParameters {
            Directory = missingDir,
            Pattern = "*.csv"
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        _ = Assert.IsInstanceOfType<DirectoryNotFoundException>( result.Exception );
    }
}
