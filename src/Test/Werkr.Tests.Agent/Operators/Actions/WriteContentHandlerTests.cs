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
/// Unit tests for the <see cref="WriteContentHandler"/> action handler.
/// Validates writing content to a new file, overwriting existing content,
/// appending to a file, and writing with a custom encoding.
/// </summary>
[TestClass]
public class WriteContentHandlerTests {

    /// <summary>
    /// Temporary directory created for each test and cleaned up afterward.
    /// </summary>
    private string _tempDir = null!;
    /// <summary>
    /// The handler instance under test.
    /// </summary>
    private WriteContentHandler _handler = null!;
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
        _handler = new WriteContentHandler(
            TestFilePathResolver.AllowAll,
            NullLogger<WriteContentHandler>.Instance
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
    /// Verifies that writing content to a new (non-existent) file succeeds and the file contains the expected text.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task WriteContent_NewFile_Succeeds( ) {
        string path = Path.Combine(
            _tempDir,
            "file.txt"
        );

        JsonElement parameters = Serialize( new WriteContentParameters { Path = path, Content = "hello" } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.AreEqual(
            "hello",
            await File.ReadAllTextAsync(
                path,
                TestContext.CancellationToken
            )
        );
    }

    /// <summary>
    /// Verifies that writing content to an existing file replaces the old content.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task WriteContent_Overwrite_ReplacesContent( ) {
        string path = Path.Combine(
            _tempDir,
            "file.txt"
        );
        await File.WriteAllTextAsync(
            path,
            "old",
            TestContext.CancellationToken
        );

        JsonElement parameters = Serialize( new WriteContentParameters { Path = path, Content = "new" } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.AreEqual(
            "new",
            await File.ReadAllTextAsync(
                path,
                TestContext.CancellationToken
            )
        );
    }

    /// <summary>
    /// Verifies that the <c>Append</c> mode adds content to the end of an existing file.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task WriteContent_Append_AppendsContent( ) {
        string path = Path.Combine(
            _tempDir,
            "file.txt"
        );
        await File.WriteAllTextAsync(
            path,
            "hello",
            TestContext.CancellationToken
        );

        JsonElement parameters = Serialize(
            new WriteContentParameters { Path = path, Content = " world", Append = true }
        );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.AreEqual(
            "hello world",
            await File.ReadAllTextAsync(
                path,
                TestContext.CancellationToken
            )
        );
    }

    /// <summary>
    /// Verifies that specifying a custom encoding (ASCII) produces a file on disk without error.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task WriteContent_CustomEncoding( ) {
        string path = Path.Combine(
            _tempDir,
            "ascii.txt"
        );

        JsonElement parameters = Serialize(
            new WriteContentParameters { Path = path, Content = "test", Encoding = "ascii" }
        );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.IsTrue( File.Exists( path ) );
    }
}
