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
public class CreateFileHandlerTests {

    private string _tempDir = null!;
    private CreateFileHandler _handler = null!;
    private Channel<OperatorOutput> _channel = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void TestInit( ) {
        _tempDir = Path.Combine( Path.GetTempPath( ), $"werkr-test-{Guid.NewGuid( )}" );
        _ = Directory.CreateDirectory( _tempDir );
        _handler = new CreateFileHandler( TestFilePathResolver.AllowAll, NullLogger<CreateFileHandler>.Instance );
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
    public async Task CreateEmptyFile_Succeeds( ) {
        string path = Path.Combine( _tempDir, "new.txt" );

        JsonElement parameters = Serialize( new CreateFileParameters { Path = path } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.IsTrue( File.Exists( path ) );
        Assert.AreEqual( 0, new FileInfo( path ).Length );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CreateFileWithContent_Succeeds( ) {
        string path = Path.Combine( _tempDir, "content.txt" );

        JsonElement parameters = Serialize( new CreateFileParameters { Path = path, Content = "hello world" } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "hello world", await File.ReadAllTextAsync( path, TestContext.CancellationToken ) );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CreateFileCreatesParentDirectories( ) {
        string path = Path.Combine( _tempDir, "sub", "deep", "file.txt" );

        JsonElement parameters = Serialize( new CreateFileParameters { Path = path, CreateParentDirectories = true } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.IsTrue( File.Exists( path ) );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CreateFile_ExistsNoOverwrite_ReturnsFailure( ) {
        string path = Path.Combine( _tempDir, "existing.txt" );
        await File.WriteAllTextAsync( path, "data", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new CreateFileParameters { Path = path, Overwrite = false } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CreateFile_ExistsWithOverwrite_Succeeds( ) {
        string path = Path.Combine( _tempDir, "existing.txt" );
        await File.WriteAllTextAsync( path, "old", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new CreateFileParameters { Path = path, Content = "new", Overwrite = true } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "new", await File.ReadAllTextAsync( path, TestContext.CancellationToken ) );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CreateFile_NoParentNoCreate_ReturnsFailure( ) {
        string path = Path.Combine( _tempDir, "missing-parent", "file.txt" );

        JsonElement parameters = Serialize( new CreateFileParameters { Path = path, CreateParentDirectories = false } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
    }
}
