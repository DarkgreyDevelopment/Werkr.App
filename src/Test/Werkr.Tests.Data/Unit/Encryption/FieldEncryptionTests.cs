using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Werkr.Data;
using Werkr.Data.Encryption;
using Werkr.Data.Entities.Configuration;

namespace Werkr.Tests.Data.Unit.Encryption;

/// <summary>
/// Unit tests for <see cref="FieldEncryptionProvider"/>, EF Core value converters
/// (<see cref="EncryptedStringConverter"/>, <see cref="EncryptedByteArrayConverter"/>,
/// <see cref="EncryptedNullableByteArrayConverter"/>), and encrypted
/// round-trip persistence through <see cref="WerkrDbContext"/>.
/// </summary>
[TestClass]
public class FieldEncryptionTests {

    private string _key = null!;
    private FieldEncryptionProvider _provider = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void TestInit( ) {
        _key = FieldEncryptionProvider.GenerateKey( );
        _provider = new FieldEncryptionProvider( _key );
    }

    // ── FieldEncryptionProvider: String round-trip ──

    [TestMethod]
    public void Encrypt_Decrypt_String_RoundTrip( ) {
        string plaintext = "sensitive-api-key-12345";
        string? encrypted = _provider.Encrypt( plaintext );
        Assert.IsNotNull( encrypted );
        Assert.AreNotEqual( plaintext, encrypted );

        string? decrypted = _provider.Decrypt( encrypted );
        Assert.AreEqual( plaintext, decrypted );
    }

    [TestMethod]
    public void Encrypt_Decrypt_EmptyString_RoundTrip( ) {
        string? encrypted = _provider.Encrypt( "" );
        Assert.IsNotNull( encrypted );

        string? decrypted = _provider.Decrypt( encrypted );
        Assert.AreEqual( "", decrypted );
    }

    [TestMethod]
    public void Encrypt_NullString_ReturnsNull( ) {
        Assert.IsNull( _provider.Encrypt( null ) );
    }

    [TestMethod]
    public void Decrypt_NullString_ReturnsNull( ) {
        Assert.IsNull( _provider.Decrypt( null ) );
    }

    [TestMethod]
    public void Encrypt_SamePlaintext_ProducesDifferentCiphertext( ) {
        string plaintext = "same-value";
        string? first = _provider.Encrypt( plaintext );
        string? second = _provider.Encrypt( plaintext );

        // AES-GCM uses random nonce, so ciphertexts must differ
        Assert.AreNotEqual( first, second );
    }

    [TestMethod]
    public void Decrypt_WithWrongKey_Throws( ) {
        string? encrypted = _provider.Encrypt( "secret" );
        FieldEncryptionProvider wrongProvider = new( FieldEncryptionProvider.GenerateKey( ) );

        _ = Assert.ThrowsExactly<AuthenticationTagMismatchException>( ( ) =>
            wrongProvider.Decrypt( encrypted ) );
    }

    // ── FieldEncryptionProvider: Byte array round-trip ──

    [TestMethod]
    public void EncryptBytes_DecryptBytes_RoundTrip( ) {
        byte[] data = RandomNumberGenerator.GetBytes( 32 );
        string? encrypted = _provider.EncryptBytes( data );
        Assert.IsNotNull( encrypted );

        byte[]? decrypted = _provider.DecryptBytes( encrypted );
        CollectionAssert.AreEqual( data, decrypted );
    }

    [TestMethod]
    public void EncryptBytes_NullOrEmpty_ReturnsNull( ) {
        Assert.IsNull( _provider.EncryptBytes( null ) );
        Assert.IsNull( _provider.EncryptBytes( [] ) );
    }

    [TestMethod]
    public void DecryptBytes_Null_ReturnsNull( ) {
        Assert.IsNull( _provider.DecryptBytes( null ) );
    }

    [TestMethod]
    public void DecryptBytes_WithWrongKey_Throws( ) {
        string? encrypted = _provider.EncryptBytes( RandomNumberGenerator.GetBytes( 16 ) );
        FieldEncryptionProvider wrongProvider = new( FieldEncryptionProvider.GenerateKey( ) );

        _ = Assert.ThrowsExactly<AuthenticationTagMismatchException>( ( ) =>
            wrongProvider.DecryptBytes( encrypted ) );
    }

    // ── Key validation ──

    [TestMethod]
    public void Constructor_InvalidKeyLength_Throws( ) {
        string shortKey = Convert.ToBase64String( new byte[16] );
        _ = Assert.ThrowsExactly<ArgumentException>( ( ) => new FieldEncryptionProvider( shortKey ) );
    }

    [TestMethod]
    public void GenerateKey_ProducesValidKey( ) {
        string key = FieldEncryptionProvider.GenerateKey( );
        byte[] decoded = Convert.FromBase64String( key );
        Assert.HasCount( 32, decoded );
    }

    // ── EF Core converter integration via SQLite ──

    [TestMethod]
    public async Task ConfigurationEntry_Value_IsEncryptedInDatabase( ) {
        CancellationToken ct = TestContext.CancellationToken;
        using SqliteConnection conn = new( "DataSource=:memory:" );
        conn.Open( );

        string key = FieldEncryptionProvider.GenerateKey( );
        FieldEncryptionProvider encProvider = new( key );

        DbContextOptions<SqliteWerkrDbContext> options = new DbContextOptionsBuilder<SqliteWerkrDbContext>( )
            .UseSqlite( conn )
            .EnableServiceProviderCaching( false )
            .Options;

        // Create schema with encryption enabled
        using (SqliteWerkrDbContext db = new( options ) { FieldEncryption = encProvider }) {
            _ = db.Database.EnsureCreated( );

            _ = db.ConfigurationEntries.Add( new ConfigurationEntry {
                Key = "test.setting",
                Value = "plaintext-secret-value",
                ValueType = "string",
                Category = "security",
                ScopeLevel = 0,
                SyncVersion = 1,
                DefaultValue = "default-secret",
                CreatedUtc = DateTime.UtcNow,
                ModifiedUtc = DateTime.UtcNow,
                ModifiedByUserId = "test",
            } );
            _ = await db.SaveChangesAsync( ct );
        }

        // Read via EF (should decrypt transparently)
        using (SqliteWerkrDbContext db = new( options ) { FieldEncryption = encProvider }) {
            ConfigurationEntry? entry = await db.ConfigurationEntries
                .FirstOrDefaultAsync( e => e.Key == "test.setting", ct );
            Assert.IsNotNull( entry );
            Assert.AreEqual( "plaintext-secret-value", entry.Value );
            Assert.AreEqual( "default-secret", entry.DefaultValue );
        }

        // Read raw SQL — should be ciphertext, not plaintext
        using SqliteCommand cmd = conn.CreateCommand( );
        cmd.CommandText = "SELECT Value, DefaultValue FROM configuration_entries WHERE Key = 'test.setting'";
        using SqliteDataReader reader = await cmd.ExecuteReaderAsync( ct );
        Assert.IsTrue( reader.Read( ) );

        string rawValue = reader.GetString( 0 );
        string rawDefault = reader.GetString( 1 );

        Assert.AreNotEqual( "plaintext-secret-value", rawValue, "Value should be ciphertext in raw DB." );
        Assert.AreNotEqual( "default-secret", rawDefault, "DefaultValue should be ciphertext in raw DB." );

        // Verify the raw ciphertext is valid Base64 (our encryption format)
        byte[] decoded = Convert.FromBase64String( rawValue );
        Assert.IsGreaterThan( 28, decoded.Length, "Ciphertext should contain nonce + data + tag." );
    }

    [TestMethod]
    public async Task Credential_EncryptedValue_RoundTrips( ) {
        CancellationToken ct = TestContext.CancellationToken;
        using SqliteConnection conn = new( "DataSource=:memory:" );
        conn.Open( );

        string key = FieldEncryptionProvider.GenerateKey( );
        FieldEncryptionProvider encProvider = new( key );

        DbContextOptions<SqliteWerkrDbContext> options = new DbContextOptionsBuilder<SqliteWerkrDbContext>( )
            .UseSqlite( conn )
            .EnableServiceProviderCaching( false )
            .Options;

        using (SqliteWerkrDbContext db = new( options ) { FieldEncryption = encProvider }) {
            _ = db.Database.EnsureCreated( );

            _ = db.Credentials.Add( new Credential {
                Name = "smtp-password",
                Type = CredentialType.Password,
                EncryptedValue = "my-super-secret-password",
                CreatedUtc = DateTime.UtcNow,
                ModifiedUtc = DateTime.UtcNow,
                CreatedByUserId = "admin",
                ModifiedByUserId = "admin",
            } );
            _ = await db.SaveChangesAsync( ct );
        }

        // Transparent read
        using (SqliteWerkrDbContext db = new( options ) { FieldEncryption = encProvider }) {
            Credential? cred = await db.Credentials.FirstOrDefaultAsync( c => c.Name == "smtp-password", ct );
            Assert.IsNotNull( cred );
            Assert.AreEqual( "my-super-secret-password", cred.EncryptedValue );
        }

        // Raw read — must be ciphertext
        using SqliteCommand cmd = conn.CreateCommand( );
        cmd.CommandText = "SELECT EncryptedValue FROM credentials WHERE Name = 'smtp-password'";
        object? raw = await cmd.ExecuteScalarAsync( ct );
        Assert.IsNotNull( raw );
        Assert.AreNotEqual( "my-super-secret-password", raw.ToString( ),
            "Credential value should be encrypted in the database." );
    }

    [TestMethod]
    public void NullableByteArrayConverter_NullValue_ReturnsNull( ) {
        EncryptedNullableByteArrayConverter converter = new( _provider );
        Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<byte[]?, string?> typedConverter = converter;

        // Null input → null output (both directions)
        Assert.IsNull( typedConverter.ConvertToProvider( null ) );
        Assert.IsNull( typedConverter.ConvertFromProvider( null ) );
    }

    [TestMethod]
    public void NullableByteArrayConverter_NonNullValue_RoundTrips( ) {
        EncryptedNullableByteArrayConverter converter = new( _provider );
        Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<byte[]?, string?> typedConverter = converter;

        byte[] data = RandomNumberGenerator.GetBytes( 32 );
        object? encrypted = typedConverter.ConvertToProvider( data );
        Assert.IsNotNull( encrypted );
        _ = Assert.IsInstanceOfType<string>( encrypted );

        object? decrypted = typedConverter.ConvertFromProvider( encrypted );
        Assert.IsNotNull( decrypted );
        CollectionAssert.AreEqual( data, (byte[])decrypted );
    }

    [TestMethod]
    public void ByteArrayConverter_NonNullValue_RoundTrips( ) {
        EncryptedByteArrayConverter converter = new( _provider );
        Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<byte[], string> typedConverter = converter;

        byte[] data = RandomNumberGenerator.GetBytes( 32 );
        object? encrypted = typedConverter.ConvertToProvider( data );
        Assert.IsNotNull( encrypted );

        object? decrypted = typedConverter.ConvertFromProvider( encrypted );
        Assert.IsNotNull( decrypted );
        CollectionAssert.AreEqual( data, (byte[])decrypted );
    }

}
