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
public class CopyFileHandlerTests {

    private string _tempDir = null!;
    private CopyFileHandler _handler = null!;
    private Channel<OperatorOutput> _channel = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void TestInit( ) {
        _tempDir = Path.Combine( Path.GetTempPath( ), $"werkr-test-{Guid.NewGuid( )}" );
        _ = Directory.CreateDirectory( _tempDir );
        _handler = new CopyFileHandler( TestFilePathResolver.AllowAll, NullLogger<CopyFileHandler>.Instance );
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
    public async Task CopySingleFile_Succeeds( ) {
        string src = Path.Combine( _tempDir, "source.txt" );
        string dest = Path.Combine( _tempDir, "dest.txt" );
        await File.WriteAllTextAsync( src, "hello", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new CopyFileParameters { Source = src, Destination = dest } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.IsTrue( File.Exists( dest ) );
        Assert.AreEqual( "hello", await File.ReadAllTextAsync( dest, TestContext.CancellationToken ) );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CopyWildcard_CopiesMatchingFiles( ) {
        string src1 = Path.Combine( _tempDir, "a.txt" );
        string src2 = Path.Combine( _tempDir, "b.txt" );
        string destDir = Path.Combine( _tempDir, "out" );
        _ = Directory.CreateDirectory( destDir );
        await File.WriteAllTextAsync( src1, "a", TestContext.CancellationToken );
        await File.WriteAllTextAsync( src2, "b", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new CopyFileParameters {
            Source = Path.Combine( _tempDir, "*.txt" ),
            Destination = destDir
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.IsTrue( File.Exists( Path.Combine( destDir, "a.txt" ) ) );
        Assert.IsTrue( File.Exists( Path.Combine( destDir, "b.txt" ) ) );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CopyDirectory_Recursive( ) {
        string srcDir = Path.Combine( _tempDir, "srcDir" );
        string subDir = Path.Combine( srcDir, "sub" );
        _ = Directory.CreateDirectory( subDir );
        await File.WriteAllTextAsync( Path.Combine( srcDir, "root.txt" ), "root", TestContext.CancellationToken );
        await File.WriteAllTextAsync( Path.Combine( subDir, "child.txt" ), "child", TestContext.CancellationToken );

        string destDir = Path.Combine( _tempDir, "destDir" );

        JsonElement parameters = Serialize( new CopyFileParameters {
            Source = srcDir,
            Destination = destDir,
            Recursive = true
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.IsTrue( File.Exists( Path.Combine( destDir, "root.txt" ) ) );
        Assert.IsTrue( File.Exists( Path.Combine( destDir, "sub", "child.txt" ) ) );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CopyNoMatch_ReturnsFailure( ) {
        string dest = Path.Combine( _tempDir, "out" );
        JsonElement parameters = Serialize( new CopyFileParameters {
            Source = Path.Combine( _tempDir, "*.xyz" ),
            Destination = dest
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CopySameSourceAndDest_ThrowsArgument( ) {
        string path = Path.Combine( _tempDir, "file.txt" );
        await File.WriteAllTextAsync( path, "data", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new CopyFileParameters { Source = path, Destination = path } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CopyDenied_ReturnsFailureWithUnauthorized( ) {
        CopyFileHandler deniedHandler = new( TestFilePathResolver.DenyAll, NullLogger<CopyFileHandler>.Instance );
        string src = Path.Combine( _tempDir, "s.txt" );
        string dest = Path.Combine( _tempDir, "d.txt" );
        await File.WriteAllTextAsync( src, "data", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new CopyFileParameters { Source = src, Destination = dest } );
        ActionOperatorResult result = await deniedHandler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        _ = Assert.IsInstanceOfType<UnauthorizedAccessException>( result.Exception );
    }
}
