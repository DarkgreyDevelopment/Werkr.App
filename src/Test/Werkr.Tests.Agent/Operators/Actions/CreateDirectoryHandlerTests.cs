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
public class CreateDirectoryHandlerTests {

    private string _tempDir = null!;
    private CreateDirectoryHandler _handler = null!;
    private Channel<OperatorOutput> _channel = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void TestInit( ) {
        _tempDir = Path.Combine( Path.GetTempPath( ), $"werkr-test-{Guid.NewGuid( )}" );
        _ = Directory.CreateDirectory( _tempDir );
        _handler = new CreateDirectoryHandler( TestFilePathResolver.AllowAll, NullLogger<CreateDirectoryHandler>.Instance );
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
    public async Task CreateDirectory_Succeeds( ) {
        string path = Path.Combine( _tempDir, "newDir" );

        JsonElement parameters = Serialize( new CreateDirectoryParameters { Path = path } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.IsTrue( Directory.Exists( path ) );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CreateDirectory_AlreadyExists_StillSucceeds( ) {
        string path = Path.Combine( _tempDir, "existingDir" );
        _ = Directory.CreateDirectory( path );

        JsonElement parameters = Serialize( new CreateDirectoryParameters { Path = path } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CreateDirectory_NestedPath_CreatesAll( ) {
        string path = Path.Combine( _tempDir, "a", "b", "c" );

        JsonElement parameters = Serialize( new CreateDirectoryParameters { Path = path } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.IsTrue( Directory.Exists( path ) );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CreateDirectory_FileExistsAtPath_ReturnsFailure( ) {
        string path = Path.Combine( _tempDir, "conflicting" );
        await File.WriteAllTextAsync( path, "data", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new CreateDirectoryParameters { Path = path } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
    }
}
