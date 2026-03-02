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
public class RenameFileHandlerTests {

    private string _tempDir = null!;
    private RenameFileHandler _handler = null!;
    private Channel<OperatorOutput> _channel = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void TestInit( ) {
        _tempDir = Path.Combine( Path.GetTempPath( ), $"werkr-test-{Guid.NewGuid( )}" );
        _ = Directory.CreateDirectory( _tempDir );
        _handler = new RenameFileHandler( TestFilePathResolver.AllowAll, NullLogger<RenameFileHandler>.Instance );
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
    public async Task RenameFile_Succeeds( ) {
        string path = Path.Combine( _tempDir, "old.txt" );
        await File.WriteAllTextAsync( path, "data", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new RenameFileParameters { Path = path, NewName = "new.txt" } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.IsFalse( File.Exists( path ) );
        Assert.IsTrue( File.Exists( Path.Combine( _tempDir, "new.txt" ) ) );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task RenameDirectory_Succeeds( ) {
        string dir = Path.Combine( _tempDir, "oldDir" );
        _ = Directory.CreateDirectory( dir );

        JsonElement parameters = Serialize( new RenameFileParameters { Path = dir, NewName = "newDir" } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.IsFalse( Directory.Exists( dir ) );
        Assert.IsTrue( Directory.Exists( Path.Combine( _tempDir, "newDir" ) ) );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task RenameNonExistent_ReturnsFailure( ) {
        string path = Path.Combine( _tempDir, "missing.txt" );

        JsonElement parameters = Serialize( new RenameFileParameters { Path = path, NewName = "new.txt" } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task RenameFile_DestinationExists_OverwriteFalse_ReturnsFailure( ) {
        string path = Path.Combine( _tempDir, "old.txt" );
        string existing = Path.Combine( _tempDir, "new.txt" );
        await File.WriteAllTextAsync( path, "data", TestContext.CancellationToken );
        await File.WriteAllTextAsync( existing, "existing", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new RenameFileParameters { Path = path, NewName = "new.txt", Overwrite = false } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
    }

    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task RenameFile_DestinationExists_OverwriteTrue_Succeeds( ) {
        string path = Path.Combine( _tempDir, "old.txt" );
        string existing = Path.Combine( _tempDir, "new.txt" );
        await File.WriteAllTextAsync( path, "newdata", TestContext.CancellationToken );
        await File.WriteAllTextAsync( existing, "existing", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new RenameFileParameters { Path = path, NewName = "new.txt", Overwrite = true } );
        ActionOperatorResult result = await _handler.ExecuteAsync( parameters, _channel.Writer, TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "newdata", await File.ReadAllTextAsync( Path.Combine( _tempDir, "new.txt" ), TestContext.CancellationToken ) );
    }
}
