using Werkr.Agent.Security;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Security;

[TestClass]
public class FilePathResolverTests {

    private string _tempDir = null!;

    [TestInitialize]
    public void TestInit( ) {
        _tempDir = Path.Combine( Path.GetTempPath( ), $"werkr-test-{Guid.NewGuid( )}" );
        _ = Directory.CreateDirectory( _tempDir );
    }

    [TestCleanup]
    public void TestCleanup( ) {
        if (Directory.Exists( _tempDir )) {
            Directory.Delete( _tempDir, recursive: true );
        }
    }

    [TestMethod]
    public void ResolveSinglePath_ValidPath_ReturnsFullPath( ) {
        FilePathResolver resolver = new( new AllowAllPathValidator() );
        string testFile = Path.Combine( _tempDir, "test.txt" );

        string result = resolver.ResolveSinglePath( testFile );

        Assert.AreEqual( Path.GetFullPath( testFile ), result );
    }

    [TestMethod]
    public void ResolveSinglePath_DenyAll_ThrowsUnauthorized( ) {
        FilePathResolver resolver = new( new DenyAllPathValidator() );
        string testFile = Path.Combine( _tempDir, "test.txt" );

        _ = Assert.ThrowsExactly<UnauthorizedAccessException>(
            ( ) => resolver.ResolveSinglePath( testFile ) );
    }

    [TestMethod]
    public void ResolveSinglePath_RestrictedPrefix_OutsidePath_Throws( ) {
        FilePathResolver resolver = new( new AllowPrefixValidator( _tempDir ) );
        string outsidePath = Path.Combine( Path.GetTempPath(), "other-dir", "file.txt" );

        _ = Assert.ThrowsExactly<UnauthorizedAccessException>(
            ( ) => resolver.ResolveSinglePath( outsidePath ) );
    }

    [TestMethod]
    public void ResolveFiles_WildcardMatches( ) {
        FilePathResolver resolver = new( new AllowAllPathValidator() );
        File.WriteAllText( Path.Combine( _tempDir, "a.txt" ), "a" );
        File.WriteAllText( Path.Combine( _tempDir, "b.txt" ), "b" );
        File.WriteAllText( Path.Combine( _tempDir, "c.log" ), "c" );

        string wildcard = Path.Combine( _tempDir, "*.txt" );
        string[] results = resolver.ResolveFiles( wildcard );

        Assert.HasCount( 2, results );
    }

    [TestMethod]
    public void ResolveFiles_NoMatches_ReturnsEmpty( ) {
        FilePathResolver resolver = new( new AllowAllPathValidator() );

        string wildcard = Path.Combine( _tempDir, "*.xyz" );
        string[] results = resolver.ResolveFiles( wildcard );

        Assert.IsEmpty( results );
    }

    [TestMethod]
    public void ResolveFiles_DirectoryDoesNotExist_ReturnsEmpty( ) {
        FilePathResolver resolver = new( new AllowAllPathValidator() );

        string wildcard = Path.Combine( _tempDir, "nonexistent", "*.txt" );
        string[] results = resolver.ResolveFiles( wildcard );

        Assert.IsEmpty( results );
    }

    [TestMethod]
    public void ResolveFiles_DenyAll_ThrowsForEachFile( ) {
        FilePathResolver resolver = new( new DenyAllPathValidator() );
        File.WriteAllText( Path.Combine( _tempDir, "a.txt" ), "a" );

        string wildcard = Path.Combine( _tempDir, "*.txt" );

        _ = Assert.ThrowsExactly<UnauthorizedAccessException>(
            ( ) => resolver.ResolveFiles( wildcard ) );
    }

    [TestMethod]
    public void ValidateSourceDestination_SamePath_ThrowsArgument( ) {
        FilePathResolver resolver = new( new AllowAllPathValidator() );
        string path = Path.Combine( _tempDir, "file.txt" );

        _ = Assert.ThrowsExactly<ArgumentException>(
            ( ) => resolver.ValidateSourceDestination( path, path ) );
    }

    [TestMethod]
    public void ValidateSourceDestination_DifferentPaths_NoThrow( ) {
        FilePathResolver resolver = new( new AllowAllPathValidator() );
        string source = Path.Combine( _tempDir, "a.txt" );
        string dest = Path.Combine( _tempDir, "b.txt" );

        // Should not throw
        resolver.ValidateSourceDestination( source, dest );
    }
}
