using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Werkr.Agent.Security;
using Werkr.Common.Models;

namespace Werkr.Tests.Agent.Security;

[TestClass]
public class PathAllowlistValidatorTests {

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

    private static PathAllowlistValidator CreateValidator(
        AllowedPathsConfiguration config,
        ILogger<PathAllowlistValidator>? logger = null ) {
        IOptionsMonitor<AllowedPathsConfiguration> options =
            new TestOptionsMonitor<AllowedPathsConfiguration>( config );
        return new PathAllowlistValidator( options, logger ?? NullLogger<PathAllowlistValidator>.Instance );
    }

    [TestMethod]
    public void EnforceDisabled_AllPathsAllowed( ) {
        AllowedPathsConfiguration config = new( ) { EnforceAllowlist = false };
        PathAllowlistValidator validator = CreateValidator( config );

        Assert.IsTrue( validator.IsPathAllowed( @"C:\Windows\System32\cmd.exe" ) );
        Assert.IsTrue( validator.IsPathAllowed( "/etc/passwd" ) );
    }

    [TestMethod]
    public void EnforceEnabled_AllowedPrefix_Permits( ) {
        AllowedPathsConfiguration config = new( ) {
            EnforceAllowlist = true,
            Paths = [_tempDir],
        };
        PathAllowlistValidator validator = CreateValidator( config );

        string testFile = Path.Combine( _tempDir, "test.txt" );
        Assert.IsTrue( validator.IsPathAllowed( testFile ) );
    }

    [TestMethod]
    public void EnforceEnabled_OutsidePrefix_Denies( ) {
        AllowedPathsConfiguration config = new( ) {
            EnforceAllowlist = true,
            Paths = [_tempDir],
        };
        PathAllowlistValidator validator = CreateValidator( config );

        Assert.IsFalse( validator.IsPathAllowed( Path.Combine( Path.GetTempPath( ), "other-dir", "file.txt" ) ) );
    }

    [TestMethod]
    public void EnforceEnabled_NoPaths_DeniesAll( ) {
        AllowedPathsConfiguration config = new( ) {
            EnforceAllowlist = true,
            Paths = [],
        };
        PathAllowlistValidator validator = CreateValidator( config );

        Assert.IsFalse( validator.IsPathAllowed( _tempDir ) );
    }

    [TestMethod]
    public void ValidatePath_OutsideAllowlist_ThrowsUnauthorized( ) {
        AllowedPathsConfiguration config = new( ) {
            EnforceAllowlist = true,
            Paths = [_tempDir],
        };
        PathAllowlistValidator validator = CreateValidator( config );

        _ = Assert.ThrowsExactly<UnauthorizedAccessException>(
            ( ) => validator.ValidatePath( Path.Combine( Path.GetTempPath( ), "other-dir", "file.txt" ) ) );
    }

    [TestMethod]
    public void ValidatePaths_MultiplePathsValidated( ) {
        AllowedPathsConfiguration config = new( ) {
            EnforceAllowlist = true,
            Paths = [_tempDir],
        };
        PathAllowlistValidator validator = CreateValidator( config );

        string allowed = Path.Combine( _tempDir, "a.txt" );
        string denied = Path.Combine( Path.GetTempPath(), "other-dir", "b.txt" );

        // First path is fine, second should throw
        _ = Assert.ThrowsExactly<UnauthorizedAccessException>(
            ( ) => validator.ValidatePaths( allowed, denied ) );
    }

    [TestMethod]
    public void TraversalAttack_Rejected( ) {
        // Create a path that tries to escape via ..
        AllowedPathsConfiguration config = new( ) {
            EnforceAllowlist = true,
            Paths = [_tempDir],
        };
        PathAllowlistValidator validator = CreateValidator( config );

        // Path.GetFullPath will resolve the .., making it outside the prefix
        string attack = Path.Combine( _tempDir, "..", "escape.txt" );
        Assert.IsFalse( validator.IsPathAllowed( attack ) );
    }

    [TestMethod]
    public void MultiplePrefixes_AnyMatch_Permits( ) {
        string otherDir = Path.Combine( Path.GetTempPath(), $"werkr-test-{Guid.NewGuid()}" );
        _ = Directory.CreateDirectory( otherDir );
        try {
            AllowedPathsConfiguration config = new( ) {
                EnforceAllowlist = true,
                Paths = [_tempDir, otherDir],
            };
            PathAllowlistValidator validator = CreateValidator( config );

            Assert.IsTrue( validator.IsPathAllowed( Path.Combine( _tempDir, "a.txt" ) ) );
            Assert.IsTrue( validator.IsPathAllowed( Path.Combine( otherDir, "b.txt" ) ) );
        } finally {
            Directory.Delete( otherDir, recursive: true );
        }
    }

    /// <summary>
    /// Simple <see cref="IOptionsMonitor{T}"/> implementation for tests.
    /// </summary>
    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T> {

        public TestOptionsMonitor( T currentValue ) {
            CurrentValue = currentValue;
        }

        public T CurrentValue { get; }

        public T Get( string? name ) => CurrentValue;

        public IDisposable? OnChange( Action<T, string?> listener ) => null;
    }
}
