using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;

using Werkr.Agent.Operators.Actions;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Operators.Actions;

[TestClass]
public class DeleteFileHandlerTests {

    private string _tempDir = null!;
    private DeleteFileHandler _handler = null!;
    private Channel<OperatorOutput> _channel = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void TestInit( ) {
        _tempDir = Path.Combine( Path.GetTempPath( ), $"werkr-test-{Guid.NewGuid( )}" );
        _ = Directory.CreateDirectory( _tempDir );
        _handler = new DeleteFileHandler( TestFilePathResolver.AllowAll, NullLogger<DeleteFileHandler>.Instance );
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
    public async Task DeleteFile_Succeeds( ) {
        string path = Path.Combine( _tempDir, "file.txt" );
        await File.WriteAllTextAsync( path, "data", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new DeleteFileParameters { Path = path } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.IsFalse( File.Exists( path ) );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task DeleteDirectory_Recursive( ) {
        string dir = Path.Combine( _tempDir, "subdir" );
        _ = Directory.CreateDirectory( dir );
        await File.WriteAllTextAsync( Path.Combine( dir, "file.txt" ), "data", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new DeleteFileParameters { Path = dir, Recursive = true } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.IsFalse( Directory.Exists( dir ) );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task DeleteNonExistent_ReturnsFailure( ) {
        string path = Path.Combine( _tempDir, "missing.txt" );

        JsonElement parameters = Serialize( new DeleteFileParameters { Path = path } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task DeleteReadOnly_ForceRemoves( ) {
        string path = Path.Combine( _tempDir, "readonly.txt" );
        await File.WriteAllTextAsync( path, "data", TestContext.CancellationToken );
        File.SetAttributes( path, FileAttributes.ReadOnly );

        JsonElement parameters = Serialize( new DeleteFileParameters { Path = path, Force = true } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.IsFalse( File.Exists( path ) );
    }
}
