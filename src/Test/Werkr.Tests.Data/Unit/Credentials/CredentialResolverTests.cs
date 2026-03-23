using Werkr.Core.Credentials;

namespace Werkr.Tests.Data.Unit.Credentials;

/// <summary>
/// Unit tests for <see cref="CredentialResolver"/> static JSON scanning and replacement methods.
/// </summary>
[TestClass]
public class CredentialResolverTests {

    // ── FindCredentialReferences ──

    /// <summary>Finds a credential referenced via the CredentialName property.</summary>
    [TestMethod]
    public void FindCredentialReferences_FindsCredentialName( ) {
        const string json = """{"ActionType":"SendEmail","CredentialName":"smtp-cred","SmtpHost":"mail.local"}""";

        IReadOnlyList<string> refs = CredentialResolver.FindCredentialReferences( json );

        Assert.HasCount( 1, refs );
        Assert.AreEqual( "smtp-cred", refs[0] );
    }

    /// <summary>Finds a credential referenced via the AuthCredential property.</summary>
    [TestMethod]
    public void FindCredentialReferences_FindsAuthCredential( ) {
        const string json = """{"ActionType":"HttpRequest","AuthCredential":"api-key-1","Url":"https://example.com"}""";

        IReadOnlyList<string> refs = CredentialResolver.FindCredentialReferences( json );

        Assert.HasCount( 1, refs );
        Assert.AreEqual( "api-key-1", refs[0] );
    }

    /// <summary>Finds credentials in nested JSON structures.</summary>
    [TestMethod]
    public void FindCredentialReferences_FindsNestedReferences( ) {
        const string json = """{"Outer":{"CredentialName":"cred-a","Inner":{"AuthCredential":"cred-b"}}}""";

        IReadOnlyList<string> refs = CredentialResolver.FindCredentialReferences( json );

        Assert.HasCount( 2, refs );
        CollectionAssert.Contains( refs.ToList( ), "cred-a" );
        CollectionAssert.Contains( refs.ToList( ), "cred-b" );
    }

    /// <summary>Returns empty when no credential properties are present.</summary>
    [TestMethod]
    public void FindCredentialReferences_ReturnsEmptyForNoReferences( ) {
        const string json = """{"ActionType":"ShellCommand","Content":"echo hello"}""";

        IReadOnlyList<string> refs = CredentialResolver.FindCredentialReferences( json );

        Assert.HasCount( 0, refs );
    }

    /// <summary>Returns empty and does not throw for null input.</summary>
    [TestMethod]
    public void FindCredentialReferences_ReturnsEmptyForNull( ) {
        IReadOnlyList<string> refs = CredentialResolver.FindCredentialReferences( null );

        Assert.HasCount( 0, refs );
    }

    /// <summary>Returns empty and does not throw for malformed JSON.</summary>
    [TestMethod]
    public void FindCredentialReferences_HandlesMalformedJson( ) {
        IReadOnlyList<string> refs = CredentialResolver.FindCredentialReferences( "not { valid json" );

        Assert.HasCount( 0, refs );
    }

    /// <summary>De-duplicates identical credential names.</summary>
    [TestMethod]
    public void FindCredentialReferences_DeduplicatesNames( ) {
        const string json = """{"CredentialName":"shared","Inner":{"AuthCredential":"shared"}}""";

        IReadOnlyList<string> refs = CredentialResolver.FindCredentialReferences( json );

        Assert.HasCount( 1, refs );
    }

    // ── ReplaceCredentialName ──

    /// <summary>Replaces a CredentialName property value when it matches.</summary>
    [TestMethod]
    public void ReplaceCredentialName_ReplacesMatchingProperties( ) {
        const string json = """{"ActionType":"SendEmail","CredentialName":"old-cred","SmtpHost":"mail.local"}""";

        string? result = CredentialResolver.ReplaceCredentialName( json, "old-cred", "new-cred" );

        Assert.IsNotNull( result );
        Assert.Contains( "new-cred", result );
        Assert.DoesNotContain( "old-cred", result );
    }

    /// <summary>Preserves non-credential properties unchanged.</summary>
    [TestMethod]
    public void ReplaceCredentialName_PreservesOtherProperties( ) {
        const string json = """{"ActionType":"SendEmail","CredentialName":"old-cred","SmtpHost":"mail.local"}""";

        string? result = CredentialResolver.ReplaceCredentialName( json, "old-cred", "new-cred" );

        Assert.IsNotNull( result );
        Assert.Contains( "SendEmail", result );
        Assert.Contains( "mail.local", result );
    }

    /// <summary>Returns null when no credential property matches the old name.</summary>
    [TestMethod]
    public void ReplaceCredentialName_ReturnsNullWhenNoMatch( ) {
        const string json = """{"ActionType":"ShellCommand","Content":"echo hello"}""";

        string? result = CredentialResolver.ReplaceCredentialName( json, "old-cred", "new-cred" );

        Assert.IsNull( result );
    }

    /// <summary>Returns null for null input.</summary>
    [TestMethod]
    public void ReplaceCredentialName_ReturnsNullForNull( ) {
        string? result = CredentialResolver.ReplaceCredentialName( null, "old", "new" );

        Assert.IsNull( result );
    }

    /// <summary>Returns null and does not throw for malformed JSON.</summary>
    [TestMethod]
    public void ReplaceCredentialName_HandlesMalformedJson( ) {
        string? result = CredentialResolver.ReplaceCredentialName( "not json", "old", "new" );

        Assert.IsNull( result );
    }
}
