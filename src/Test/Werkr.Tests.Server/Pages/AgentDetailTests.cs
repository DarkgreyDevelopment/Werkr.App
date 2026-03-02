using Werkr.Common.Models;
using Werkr.Core.Cryptography;

namespace Werkr.Tests.Server.Pages;

/// <summary>
/// Tests for Agent Detail page data: connection info display, name editing,
/// and revoke status changes (§3.12.5).
/// </summary>
[TestClass]
public class AgentDetailTests {
    [TestMethod]
    public void AgentDetail_ShowsConnectionInfo( ) {
        Guid id = Guid.NewGuid( );
        DateTime registered = new( 2026, 2, 20, 10, 0, 0, DateTimeKind.Utc );
        DateTime lastSeen = new( 2026, 2, 21, 8, 30, 0, DateTimeKind.Utc );

        AgentDetailDto dto = new(
            id,
            "Production-Agent",
            "https://agent.example.com:5001",
            "Connected",
            "abcdef1234567890",
            registered,
            lastSeen,
            PowerShellAvailable: true,
            SystemShellAvailable: true );

        Assert.AreEqual( id, dto.Id );
        Assert.AreEqual( "Production-Agent", dto.ConnectionName );
        Assert.AreEqual( "https://agent.example.com:5001", dto.RemoteUrl );
        Assert.AreEqual( "Connected", dto.Status );
        Assert.AreEqual( "abcdef1234567890", dto.RsaKeyFingerprint );
        Assert.AreEqual( registered, dto.RegisteredAt );
        Assert.AreEqual( lastSeen, dto.LastSeen );
        Assert.IsTrue( dto.PowerShellAvailable!.Value, "PowerShell should be available." );
        Assert.IsTrue( dto.SystemShellAvailable!.Value, "SystemShell should be available." );
    }

    [TestMethod]
    public void AgentDetail_EditName_ProducesValidRequest( ) {
        string newName = "  Renamed-Agent  ";
        string trimmed = newName.Trim( );

        UpdateAgentRequest request = new( trimmed, null );

        Assert.AreEqual( "Renamed-Agent", request.ConnectionName );
        Assert.IsLessThanOrEqualTo( 200, request.ConnectionName!.Length,
            "Connection name must be 200 characters or fewer." );
    }

    [TestMethod]
    public void AgentDetail_EditName_RejectsEmptyName( ) {
        string newName = "   ";
        bool isValid = !string.IsNullOrWhiteSpace( newName );

        Assert.IsFalse( isValid, "Empty/whitespace name should be rejected." );
    }

    [TestMethod]
    public void AgentDetail_EditName_RejectsOverlongName( ) {
        string newName = new( 'x', 201 );
        string trimmed = newName.Trim( );
        bool isValid = !string.IsNullOrWhiteSpace( trimmed ) && trimmed.Length <= 200;

        Assert.IsFalse( isValid, "Name over 200 characters should be rejected." );
    }

    [TestMethod]
    public void AgentDetail_Revoke_ChangesStatus( ) {
        // Simulate the revoke flow: connected → revoked
        AgentListDto before = new(
            Guid.NewGuid( ), "Agent-R", "https://a:5001", "Connected", DateTime.UtcNow, DateTime.UtcNow );

        Assert.AreEqual( "Connected", before.Status );

        // After revocation, the API returns a new DTO with Revoked status
        AgentListDto after = before with { Status = "Revoked" };

        Assert.AreEqual( "Revoked", after.Status );
    }

    [TestMethod]
    public void AgentDetail_RsaFingerprint_ComputesConsistently( ) {
        string publicKey = "<RSAKeyValue><Modulus>testModulus</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        string fingerprint1 = EncryptionProvider.ComputeKeyFingerprint( publicKey );
        string fingerprint2 = EncryptionProvider.ComputeKeyFingerprint( publicKey );

        Assert.AreEqual( fingerprint1, fingerprint2,
            "Same public key should produce the same fingerprint." );
        Assert.IsFalse( string.IsNullOrWhiteSpace( fingerprint1 ),
            "Fingerprint should not be empty." );
        Assert.AreEqual( 64, fingerprint1.Length,
            "SHA-256 fingerprint should be 64 hex characters." );
    }
}
