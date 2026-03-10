using Werkr.Agent.Security;

namespace Werkr.Tests.Agent.Security;

/// <summary>
/// Unit tests for the <see cref="PathAllowlistValidator"/> class. Validates
/// enforcement-disabled mode (all paths allowed), prefix-based allow/deny
/// logic, empty-paths deny-all,
/// <see cref="PathAllowlistValidator.ValidatePath"/>/<see cref="PathAllowlistValidator.ValidatePaths"/>
/// exception behavior, directory-traversal attack rejection, and
/// multi-prefix matching.
/// </summary>
[TestClass]
public class PathAllowlistValidatorTests {

    /// <summary>
    /// Temporary directory created for each test and cleaned up afterward.
    /// </summary>
    private string _tempDir = null!;

    /// <summary>
    /// Creates a unique temporary directory for test operations.
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
    /// Creates a <see cref="PathAllowlistValidator"/> with the specified configuration and optional logger.
    /// </summary>
    private static PathAllowlistValidator CreateValidator(
        AllowedPathsConfiguration config,
        ILogger<PathAllowlistValidator>? logger = null ) {
        IOptionsMonitor<AllowedPathsConfiguration> options =
            new TestOptionsMonitor<AllowedPathsConfiguration>( config );
        return new PathAllowlistValidator(
            options,
            logger ?? NullLogger<PathAllowlistValidator>.Instance
        );
    }

    /// <summary>
    /// Verifies that when enforcement is disabled all paths are considered allowed, regardless of their location.
    /// </summary>
    [TestMethod]
    public void EnforceDisabled_AllPathsAllowed( ) {
        AllowedPathsConfiguration config = new( ) { EnforceAllowlist = false };
        PathAllowlistValidator validator = CreateValidator( config );

        Assert.IsTrue( validator.IsPathAllowed( @"C:\Windows\System32\cmd.exe" ) );
        Assert.IsTrue( validator.IsPathAllowed( "/etc/passwd" ) );
    }

    /// <summary>
    /// Verifies that a path within an allowed directory prefix is permitted when enforcement is enabled.
    /// </summary>
    [TestMethod]
    public void EnforceEnabled_AllowedPrefix_Permits( ) {
        AllowedPathsConfiguration config = new( ) {
            EnforceAllowlist = true,
            Paths = [_tempDir],
        };
        PathAllowlistValidator validator = CreateValidator( config );

        string testFile = Path.Combine(
            _tempDir,
            "test.txt"
        );
        Assert.IsTrue( validator.IsPathAllowed( testFile ) );
    }

    /// <summary>
    /// Verifies that a path outside every allowed prefix is denied when enforcement is enabled.
    /// </summary>
    [TestMethod]
    public void EnforceEnabled_OutsidePrefix_Denies( ) {
        AllowedPathsConfiguration config = new( ) {
            EnforceAllowlist = true,
            Paths = [_tempDir],
        };
        PathAllowlistValidator validator = CreateValidator( config );

        Assert.IsFalse( validator.IsPathAllowed( Path.Combine( Path.GetTempPath( ), "other-dir", "file.txt" ) ) );
    }

    /// <summary>
    /// Verifies that when enforcement is enabled but no paths are configured, all paths are denied.
    /// </summary>
    [TestMethod]
    public void EnforceEnabled_NoPaths_DeniesAll( ) {
        AllowedPathsConfiguration config = new( ) {
            EnforceAllowlist = true,
            Paths = [],
        };
        PathAllowlistValidator validator = CreateValidator( config );

        Assert.IsFalse( validator.IsPathAllowed( _tempDir ) );
    }

    /// <summary>
    /// Verifies that <see cref="PathAllowlistValidator.ValidatePath"/>
    /// throws an <see cref="UnauthorizedAccessException"/> for a path
    /// outside the allowlist.
    /// </summary>
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

    /// <summary>
    /// Verifies that <see cref="PathAllowlistValidator.ValidatePaths"/>
    /// throws on the first denied path when given a mix of allowed and
    /// denied paths.
    /// </summary>
    [TestMethod]
    public void ValidatePaths_MultiplePathsValidated( ) {
        AllowedPathsConfiguration config = new( ) {
            EnforceAllowlist = true,
            Paths = [_tempDir],
        };
        PathAllowlistValidator validator = CreateValidator( config );

        string allowed = Path.Combine(
            _tempDir,
            "a.txt"
        );
        string denied = Path.Combine(
            Path.GetTempPath(),
            "other-dir",
            "b.txt"
        );

        // First path is fine, second should throw
        _ = Assert.ThrowsExactly<UnauthorizedAccessException>(
            ( ) => validator.ValidatePaths(
                allowed,
                denied
            )
        );
    }

    /// <summary>
    /// Verifies that a directory traversal attack (e.g., "../escape.txt")
    /// is rejected because the resolved full path falls outside the allowed
    /// prefix.
    /// </summary>
    [TestMethod]
    public void TraversalAttack_Rejected( ) {
        // Create a path that tries to escape via ..
        AllowedPathsConfiguration config = new( ) {
            EnforceAllowlist = true,
            Paths = [_tempDir],
        };
        PathAllowlistValidator validator = CreateValidator( config );

        // Path.GetFullPath will resolve the .., making it outside the prefix
        string attack = Path.Combine(
            _tempDir,
            "..",
            "escape.txt"
        );
        Assert.IsFalse( validator.IsPathAllowed( attack ) );
    }

    /// <summary>
    /// Verifies that when multiple directory prefixes are configured, a path under any one of them is allowed.
    /// </summary>
    [TestMethod]
    public void MultiplePrefixes_AnyMatch_Permits( ) {
        string otherDir = Path.Combine(
            Path.GetTempPath(),
            $"werkr-test-{Guid.NewGuid()}"
        );
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
            Directory.Delete(
                otherDir,
                recursive: true
            );
        }
    }

    /// <summary>
    /// Simple <see cref="IOptionsMonitor{T}"/> implementation for tests.
    /// </summary>
    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T> {

        /// <summary>
        /// Initializes a new instance of the <see cref="TestOptionsMonitor{T}"/> class.
        /// </summary>
        public TestOptionsMonitor( T currentValue ) {
            CurrentValue = currentValue;
        }

        /// <summary>
        /// Gets the current options value.
        /// </summary>
        public T CurrentValue { get; }

        /// <summary>
        /// Returns the current value regardless of the supplied <paramref name="name"/>.
        /// </summary>
        public T Get( string? name ) => CurrentValue;

        /// <summary>
        /// No-op change listener registration. Returns <see langword="null"/>.
        /// </summary>
        public IDisposable? OnChange( Action<T, string?> listener ) => null;
    }
}
