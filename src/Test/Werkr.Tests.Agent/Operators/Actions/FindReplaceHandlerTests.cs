using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Werkr.Agent.Operators.Actions;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Operators.Actions;

/// <summary>
/// Unit tests for the <see cref="FindReplaceHandler"/> action handler.
/// Validates plain text replace, regex replace, case sensitivity, and edge cases.
/// </summary>
[TestClass]
public class FindReplaceHandlerTests {

    /// <summary>
    /// Temporary directory created for each test and cleaned up afterward.
    /// </summary>
    private string _tempDir = null!;
    /// <summary>
    /// The handler instance under test.
    /// </summary>
    private FindReplaceHandler _handler = null!;
    /// <summary>
    /// Unbounded channel used to capture <see cref="OperatorOutput"/> messages.
    /// </summary>
    private Channel<OperatorOutput> _channel = null!;

    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> for the current test run.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Creates a unique temporary directory, the handler, and an unbounded output channel.
    /// </summary>
    [TestInitialize]
    public void TestInit( ) {
        _tempDir = Path.Combine(
            Path.GetTempPath( ),
            $"werkr-test-{Guid.NewGuid( )}"
        );
        _ = Directory.CreateDirectory( _tempDir );
        _handler = new FindReplaceHandler(
            TestFilePathResolver.AllowAll,
            NullLogger<FindReplaceHandler>.Instance
        );
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
    /// Verifies that a plain text case-sensitive replacement works correctly.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task FindReplace_PlainText_ReplacesAll( ) {
        string filePath = Path.Combine( _tempDir, "test.txt" );
        await File.WriteAllTextAsync( filePath, "hello world hello", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new FindReplaceParameters {
            Path = filePath,
            Find = "hello",
            Replace = "bye"
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        string content = await File.ReadAllTextAsync( filePath, TestContext.CancellationToken );
        Assert.AreEqual( "bye world bye", content );
    }

    /// <summary>
    /// Verifies that case-insensitive plain text replacement works.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task FindReplace_CaseInsensitive_ReplacesAll( ) {
        string filePath = Path.Combine( _tempDir, "test.txt" );
        await File.WriteAllTextAsync( filePath, "Hello HELLO hello", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new FindReplaceParameters {
            Path = filePath,
            Find = "hello",
            Replace = "bye",
            CaseSensitive = false
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        string content = await File.ReadAllTextAsync( filePath, TestContext.CancellationToken );
        Assert.AreEqual( "bye bye bye", content );
    }

    /// <summary>
    /// Verifies that regex replacement with capture groups works.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task FindReplace_Regex_ReplacesWithGroups( ) {
        string filePath = Path.Combine( _tempDir, "test.txt" );
        await File.WriteAllTextAsync( filePath, "version=1.2.3", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new FindReplaceParameters {
            Path = filePath,
            Find = @"version=(\d+\.\d+\.\d+)",
            Replace = "version=9.9.9",
            IsRegex = true
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        string content = await File.ReadAllTextAsync( filePath, TestContext.CancellationToken );
        Assert.AreEqual( "version=9.9.9", content );
    }

    /// <summary>
    /// Verifies that no matches produces a success with 0 replacements.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task FindReplace_NoMatches_SucceedsWithZeroReplacements( ) {
        string filePath = Path.Combine( _tempDir, "test.txt" );
        await File.WriteAllTextAsync( filePath, "hello world", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new FindReplaceParameters {
            Path = filePath,
            Find = "missing",
            Replace = "found"
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );

        _channel.Writer.Complete( );
        List<OperatorOutput> messages = [ ];
        await foreach (OperatorOutput msg in _channel.Reader.ReadAllAsync( TestContext.CancellationToken )) {
            messages.Add( msg );
        }

        Assert.IsTrue(
            messages.Exists( m => m.Message.Contains( "0 replacement(s)" ) ),
            "Expected zero replacements in output."
        );
    }

    /// <summary>
    /// Verifies that an invalid regex returns failure.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task FindReplace_InvalidRegex_ReturnsFailure( ) {
        string filePath = Path.Combine( _tempDir, "test.txt" );
        await File.WriteAllTextAsync( filePath, "content", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new FindReplaceParameters {
            Path = filePath,
            Find = "[invalid",
            Replace = "x",
            IsRegex = true
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
    }

    /// <summary>
    /// Verifies that a non-existent file returns failure.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task FindReplace_FileNotFound_ReturnsFailure( ) {
        string missingPath = Path.Combine( _tempDir, "missing.txt" );

        JsonElement parameters = Serialize( new FindReplaceParameters {
            Path = missingPath,
            Find = "a",
            Replace = "b"
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        Assert.IsInstanceOfType<FileNotFoundException>( result.Exception );
    }

    /// <summary>
    /// Verifies that a denied path returns failure with UnauthorizedAccessException.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task FindReplace_DeniedPath_ReturnsFailure( ) {
        FindReplaceHandler denied = new(
            TestFilePathResolver.DenyAll,
            NullLogger<FindReplaceHandler>.Instance
        );

        JsonElement parameters = Serialize( new FindReplaceParameters {
            Path = "/some/path",
            Find = "a",
            Replace = "b"
        } );
        ActionOperatorResult result = await denied.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        Assert.IsInstanceOfType<UnauthorizedAccessException>( result.Exception );
    }
}
