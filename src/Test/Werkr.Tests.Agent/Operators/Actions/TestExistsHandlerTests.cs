using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;

using Werkr.Agent.Operators.Actions;
using Werkr.Common.Models;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Operators.Actions;

[TestClass]
public class TestExistsHandlerTests {

    private string _tempDir = null!;
    private TestExistsHandler _handler = null!;
    private Channel<OperatorOutput> _channel = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void TestInit( ) {
        _tempDir = Path.Combine( Path.GetTempPath( ), $"werkr-test-{Guid.NewGuid( )}" );
        _ = Directory.CreateDirectory( _tempDir );
        _handler = new TestExistsHandler( TestFilePathResolver.AllowAll, NullLogger<TestExistsHandler>.Instance );
        _channel = Channel.CreateUnbounded<OperatorOutput>( );
    }

    [TestCleanup]
    public void TestCleanup( ) {
        if (Directory.Exists( _tempDir )) {
            Directory.Delete( _tempDir, recursive: true );
        }
    }

    private static JsonElement Serialize<T>( T value ) =>
        TestActionDescriptor.Serialize( value );

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task FileExists_ReturnsSuccess( ) {
        string path = Path.Combine( _tempDir, "file.txt" );
        await File.WriteAllTextAsync( path, "data", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new TestExistsParameters { Path = path, Type = PathType.File } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task FileNotExists_ReturnsFailure( ) {
        string path = Path.Combine( _tempDir, "missing.txt" );

        JsonElement parameters = Serialize( new TestExistsParameters { Path = path, Type = PathType.File } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task DirectoryExists_ReturnsSuccess( ) {
        string path = Path.Combine( _tempDir, "subdir" );
        _ = Directory.CreateDirectory( path );

        JsonElement parameters = Serialize( new TestExistsParameters { Path = path, Type = PathType.Directory } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task DirectoryNotExists_ReturnsFailure( ) {
        string path = Path.Combine( _tempDir, "missing-dir" );

        JsonElement parameters = Serialize( new TestExistsParameters { Path = path, Type = PathType.Directory } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task AnyType_FileExists_ReturnsSuccess( ) {
        string path = Path.Combine( _tempDir, "file.txt" );
        await File.WriteAllTextAsync( path, "data", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new TestExistsParameters { Path = path, Type = PathType.Any } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task AnyType_DirectoryExists_ReturnsSuccess( ) {
        string path = Path.Combine( _tempDir, "dir" );
        _ = Directory.CreateDirectory( path );

        JsonElement parameters = Serialize( new TestExistsParameters { Path = path, Type = PathType.Any } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task FileType_OnDirectory_ReturnsFailure( ) {
        string path = Path.Combine( _tempDir, "dir" );
        _ = Directory.CreateDirectory( path );

        JsonElement parameters = Serialize( new TestExistsParameters { Path = path, Type = PathType.File } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
    }
}
