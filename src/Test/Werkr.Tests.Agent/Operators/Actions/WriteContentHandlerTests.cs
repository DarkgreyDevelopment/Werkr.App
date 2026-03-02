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
public class WriteContentHandlerTests {

    private string _tempDir = null!;
    private WriteContentHandler _handler = null!;
    private Channel<OperatorOutput> _channel = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void TestInit( ) {
        _tempDir = Path.Combine( Path.GetTempPath( ), $"werkr-test-{Guid.NewGuid( )}" );
        _ = Directory.CreateDirectory( _tempDir );
        _handler = new WriteContentHandler( TestFilePathResolver.AllowAll, NullLogger<WriteContentHandler>.Instance );
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
    public async Task WriteContent_NewFile_Succeeds( ) {
        string path = Path.Combine( _tempDir, "file.txt" );

        JsonElement parameters = Serialize( new WriteContentParameters { Path = path, Content = "hello" } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "hello", await File.ReadAllTextAsync( path, TestContext.CancellationToken ) );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task WriteContent_Overwrite_ReplacesContent( ) {
        string path = Path.Combine( _tempDir, "file.txt" );
        await File.WriteAllTextAsync( path, "old", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new WriteContentParameters { Path = path, Content = "new" } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "new", await File.ReadAllTextAsync( path, TestContext.CancellationToken ) );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task WriteContent_Append_AppendsContent( ) {
        string path = Path.Combine( _tempDir, "file.txt" );
        await File.WriteAllTextAsync( path, "hello", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new WriteContentParameters { Path = path, Content = " world", Append = true } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "hello world", await File.ReadAllTextAsync( path, TestContext.CancellationToken ) );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task WriteContent_CustomEncoding( ) {
        string path = Path.Combine( _tempDir, "ascii.txt" );

        JsonElement parameters = Serialize( new WriteContentParameters { Path = path, Content = "test", Encoding = "ascii" } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.IsTrue( File.Exists( path ) );
    }
}
