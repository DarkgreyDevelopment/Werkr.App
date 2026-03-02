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
public class MoveFileHandlerTests {

    private string _tempDir = null!;
    private MoveFileHandler _handler = null!;
    private Channel<OperatorOutput> _channel = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void TestInit( ) {
        _tempDir = Path.Combine( Path.GetTempPath( ), $"werkr-test-{Guid.NewGuid( )}" );
        _ = Directory.CreateDirectory( _tempDir );
        _handler = new MoveFileHandler( TestFilePathResolver.AllowAll, NullLogger<MoveFileHandler>.Instance );
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
    public async Task MoveSingleFile_Succeeds( ) {
        string src = Path.Combine( _tempDir, "source.txt" );
        string dest = Path.Combine( _tempDir, "dest.txt" );
        await File.WriteAllTextAsync( src, "hello", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new MoveFileParameters { Source = src, Destination = dest } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.IsFalse( File.Exists( src ) );
        Assert.IsTrue( File.Exists( dest ) );
        Assert.AreEqual( "hello", await File.ReadAllTextAsync( dest, TestContext.CancellationToken ) );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task MoveDirectory_Succeeds( ) {
        string srcDir = Path.Combine( _tempDir, "srcDir" );
        _ = Directory.CreateDirectory( srcDir );
        await File.WriteAllTextAsync( Path.Combine( srcDir, "file.txt" ), "data", TestContext.CancellationToken );

        string destDir = Path.Combine( _tempDir, "destDir" );

        JsonElement parameters = Serialize( new MoveFileParameters { Source = srcDir, Destination = destDir } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.IsFalse( Directory.Exists( srcDir ) );
        Assert.IsTrue( File.Exists( Path.Combine( destDir, "file.txt" ) ) );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task MoveNoMatch_ReturnsFailure( ) {
        JsonElement parameters = Serialize( new MoveFileParameters {
            Source = Path.Combine( _tempDir, "*.xyz" ),
            Destination = Path.Combine( _tempDir, "out" )
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task MoveSameSourceAndDest_ThrowsArgument( ) {
        string path = Path.Combine( _tempDir, "file.txt" );
        await File.WriteAllTextAsync( path, "data", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new MoveFileParameters { Source = path, Destination = path } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
    }
}
