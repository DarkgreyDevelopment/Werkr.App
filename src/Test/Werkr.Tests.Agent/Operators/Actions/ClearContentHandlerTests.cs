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
public class ClearContentHandlerTests {

    private string _tempDir = null!;
    private ClearContentHandler _handler = null!;
    private Channel<OperatorOutput> _channel = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void TestInit( ) {
        _tempDir = Path.Combine( Path.GetTempPath( ), $"werkr-test-{Guid.NewGuid( )}" );
        _ = Directory.CreateDirectory( _tempDir );
        _handler = new ClearContentHandler( TestFilePathResolver.AllowAll, NullLogger<ClearContentHandler>.Instance );
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
    public async Task ClearContent_Succeeds( ) {
        string path = Path.Combine( _tempDir, "file.txt" );
        await File.WriteAllTextAsync( path, "hello world", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new ClearContentParameters { Path = path } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( string.Empty, await File.ReadAllTextAsync( path, TestContext.CancellationToken ) );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ClearContent_FileNotFound_ReturnsFailure( ) {
        string path = Path.Combine( _tempDir, "missing.txt" );

        JsonElement parameters = Serialize( new ClearContentParameters { Path = path } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
        _ = Assert.IsInstanceOfType<FileNotFoundException>( result.Exception );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ClearContent_AlreadyEmpty_StillSucceeds( ) {
        string path = Path.Combine( _tempDir, "empty.txt" );
        await File.WriteAllTextAsync( path, string.Empty, TestContext.CancellationToken );

        JsonElement parameters = Serialize( new ClearContentParameters { Path = path } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
    }
}
