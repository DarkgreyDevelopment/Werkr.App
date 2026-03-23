using Werkr.Agent.Security;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Security;

/// <summary>
/// Unit tests for the <see cref="FilePathResolver"/> class. Validates
/// single-path resolution with allow/deny validators, prefix-restricted
/// resolution, wildcard file resolution including no-match and non-existent
/// directory scenarios, deny-all wildcard resolution, and the
/// source/destination same-path guard.
/// </summary>
[TestClass]
public class FilePathResolverTests {

    /// <summary>
    /// Temporary directory created for each test and cleaned up afterward.
    /// </summary>
    private string _tempDir = null!;

    /// <summary>
    /// Creates a unique temporary directory for test file operations.
    /// </summary>
    [TestInitialize]
    public void TestInit( ) {
        _tempDir = Path.Combine(
            Path.GetTempPath( ),
            $"werkr-test-{Guid.NewGuid( )}"
        );
        _ = Directory.CreateDirectory( _tempDir );
    }

    /// <summary>
    /// Deletes the temporary directory and all its contents.
    /// </summary>
    [TestCleanup]
    public void TestCleanup( ) {
        if (Directory.Exists( _tempDir )) {
            Directory.Delete(
                _tempDir,
                recursive: true
            );
        }
    }

    /// <summary>
    /// Verifies that resolving a single path with an
    /// <see cref="AllowAllPathValidator"/> returns the full path without
    /// throwing.
    /// </summary>
    [TestMethod]
    public void ResolveSinglePath_ValidPath_ReturnsFullPath( ) {
        FilePathResolver resolver = new( new AllowAllPathValidator() );
        string testFile = Path.Combine(
            _tempDir,
            "test.txt"
        );

        string result = resolver.ResolveSinglePath( testFile );

        Assert.AreEqual(
            Path.GetFullPath( testFile ),
            result
        );
    }

    /// <summary>
    /// Verifies that resolving a single path with a
    /// <see cref="DenyAllPathValidator"/> throws an
    /// <see cref="UnauthorizedAccessException"/>.
    /// </summary>
    [TestMethod]
    public void ResolveSinglePath_DenyAll_ThrowsUnauthorized( ) {
        FilePathResolver resolver = new( new DenyAllPathValidator() );
        string testFile = Path.Combine(
            _tempDir,
            "test.txt"
        );

        _ = Assert.ThrowsExactly<UnauthorizedAccessException>(
            ( ) => resolver.ResolveSinglePath( testFile ) );
    }

    /// <summary>
    /// Verifies that resolving a path outside the allowed prefix throws an
    /// <see cref="UnauthorizedAccessException"/> when using an
    /// <see cref="AllowPrefixValidator"/> restricted to the temp directory.
    /// </summary>
    [TestMethod]
    public void ResolveSinglePath_RestrictedPrefix_OutsidePath_Throws( ) {
        FilePathResolver resolver = new( new AllowPrefixValidator( _tempDir ) );
        string outsidePath = Path.Combine(
            Path.GetTempPath(),
            "other-dir",
            "file.txt"
        );

        _ = Assert.ThrowsExactly<UnauthorizedAccessException>(
            ( ) => resolver.ResolveSinglePath( outsidePath ) );
    }

    /// <summary>
    /// Verifies that a wildcard pattern resolves to only the matching files.
    /// </summary>
    [TestMethod]
    public void ResolveFiles_WildcardMatches( ) {
        FilePathResolver resolver = new( new AllowAllPathValidator() );
        File.WriteAllText(
            Path.Combine(
                _tempDir,
                "a.txt"
            ),
            "a"
        );
        File.WriteAllText(
            Path.Combine(
                _tempDir,
                "b.txt"
            ),
            "b"
        );
        File.WriteAllText(
            Path.Combine(
                _tempDir,
                "c.log"
            ),
            "c"
        );

        string wildcard = Path.Combine(
            _tempDir,
            "*.txt"
        );
        string[] results = resolver.ResolveFiles( wildcard );

        Assert.HasCount(
            2,
            results
        );
    }

    /// <summary>
    /// Verifies that a wildcard pattern matching zero files returns an empty array.
    /// </summary>
    [TestMethod]
    public void ResolveFiles_NoMatches_ReturnsEmpty( ) {
        FilePathResolver resolver = new( new AllowAllPathValidator() );

        string wildcard = Path.Combine(
            _tempDir,
            "*.xyz"
        );
        string[] results = resolver.ResolveFiles( wildcard );

        Assert.IsEmpty( results );
    }

    /// <summary>
    /// Verifies that resolving a wildcard under a non-existent directory returns an empty array rather than throwing.
    /// </summary>
    [TestMethod]
    public void ResolveFiles_DirectoryDoesNotExist_ReturnsEmpty( ) {
        FilePathResolver resolver = new( new AllowAllPathValidator() );

        string wildcard = Path.Combine(
            _tempDir,
            "nonexistent",
            "*.txt"
        );
        string[] results = resolver.ResolveFiles( wildcard );

        Assert.IsEmpty( results );
    }

    /// <summary>
    /// Verifies that resolving a wildcard with a
    /// <see cref="DenyAllPathValidator"/> throws an
    /// <see cref="UnauthorizedAccessException"/> for matching files.
    /// </summary>
    [TestMethod]
    public void ResolveFiles_DenyAll_ThrowsForEachFile( ) {
        FilePathResolver resolver = new( new DenyAllPathValidator() );
        File.WriteAllText(
            Path.Combine(
                _tempDir,
                "a.txt"
            ),
            "a"
        );

        string wildcard = Path.Combine(
            _tempDir,
            "*.txt"
        );

        _ = Assert.ThrowsExactly<UnauthorizedAccessException>(
            ( ) => resolver.ResolveFiles( wildcard ) );
    }

    /// <summary>
    /// Verifies that passing the same path as both source and destination throws an <see cref="ArgumentException"/>.
    /// </summary>
    [TestMethod]
    public void ValidateSourceDestination_SamePath_ThrowsArgument( ) {
        FilePathResolver resolver = new( new AllowAllPathValidator() );
        string path = Path.Combine(
            _tempDir,
            "file.txt"
        );

        _ = Assert.ThrowsExactly<ArgumentException>(
            ( ) => resolver.ValidateSourceDestination(
                path,
                path
            )
        );
    }

    /// <summary>
    /// Verifies that passing different source and destination paths does not throw.
    /// </summary>
    [TestMethod]
    public void ValidateSourceDestination_DifferentPaths_NoThrow( ) {
        FilePathResolver resolver = new( new AllowAllPathValidator() );
        string source = Path.Combine(
            _tempDir,
            "a.txt"
        );
        string dest = Path.Combine(
            _tempDir,
            "b.txt"
        );

        // Should not throw
        resolver.ValidateSourceDestination(
            source,
            dest
        );
    }
}
