using System.Text.Json;
using System.Text.Json.Serialization;
using Werkr.Common.Models.Actions;

namespace Werkr.Tests.Server.Components;

/// <summary>
/// Tests that the <see cref="ActionParameterEditor"/> component's JSON output
/// (camelCase, DictionaryKeyPolicy=CamelCase) round-trips correctly through
/// the API-side deserialization (case-insensitive + enum converter).
///
/// This validates the critical contract between UI serialization and API consumption
/// for all 27 actions, especially the 8 new complex-type actions.
/// </summary>
[TestClass]
public class ActionJsonSerializationTests {
    /// <summary>
    /// Matches <c>ActionParameterEditor.s_jsonOptions</c> (UI serialization).
    /// </summary>
    private static readonly JsonSerializerOptions s_uiOptions = new( ) {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Matches <c>TaskMapper.s_jsonOptions</c> (API deserialization).
    /// </summary>
    private static readonly JsonSerializerOptions s_apiOptions = new( ) {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter( ) },
    };

    /// <summary>
    /// Helper: serialize with UI options, then deserialize with API options.
    /// </summary>
    private static T RoundTrip<T>( object uiData ) {
        string json = JsonSerializer.Serialize( uiData, s_uiOptions );
        T? result = JsonSerializer.Deserialize<T>( json, s_apiOptions );
        Assert.IsNotNull( result, $"Deserialization of {typeof( T ).Name} should not return null." );
        return result;
    }

    // ── File operations ─────────────────────────────────────────────

    /// <summary>
    /// CopyFile: Text + Bool fields round-trip.
    /// </summary>
    [TestMethod]
    public void CopyFile_RoundTrips( ) {
        CopyFileParameters result = RoundTrip<CopyFileParameters>( new {
            source = "/src/file.txt",
            destination = "/dst/file.txt",
            overwrite = true,
            recursive = false,
        } );

        Assert.AreEqual( "/src/file.txt", result.Source );
        Assert.AreEqual( "/dst/file.txt", result.Destination );
        Assert.IsTrue( result.Overwrite );
        Assert.IsFalse( result.Recursive );
    }

    /// <summary>
    /// MoveFile: Simple text + bool round-trip.
    /// </summary>
    [TestMethod]
    public void MoveFile_RoundTrips( ) {
        MoveFileParameters result = RoundTrip<MoveFileParameters>( new {
            source = "/a",
            destination = "/b",
            overwrite = false,
        } );

        Assert.AreEqual( "/a", result.Source );
        Assert.AreEqual( "/b", result.Destination );
        Assert.IsFalse( result.Overwrite );
    }

    /// <summary>
    /// RenameFile round-trip.
    /// </summary>
    [TestMethod]
    public void RenameFile_RoundTrips( ) {
        RenameFileParameters result = RoundTrip<RenameFileParameters>( new {
            path = "/old",
            newName = "new.txt",
            overwrite = true,
        } );

        Assert.AreEqual( "/old", result.Path );
        Assert.AreEqual( "new.txt", result.NewName );
        Assert.IsTrue( result.Overwrite );
    }

    /// <summary>
    /// DeleteFile round-trip.
    /// </summary>
    [TestMethod]
    public void DeleteFile_RoundTrips( ) {
        DeleteFileParameters result = RoundTrip<DeleteFileParameters>( new {
            path = "/to-delete",
            recursive = true,
            force = true,
        } );

        Assert.AreEqual( "/to-delete", result.Path );
        Assert.IsTrue( result.Recursive );
        Assert.IsTrue( result.Force );
    }

    /// <summary>
    /// CreateFile with Encoding select and optional Content round-trip.
    /// </summary>
    [TestMethod]
    public void CreateFile_RoundTrips( ) {
        CreateFileParameters result = RoundTrip<CreateFileParameters>( new {
            path = "/new-file.txt",
            content = "Hello World",
            overwrite = false,
            encoding = "utf-8",
            createParentDirectories = true,
        } );

        Assert.AreEqual( "/new-file.txt", result.Path );
        Assert.AreEqual( "Hello World", result.Content );
        Assert.AreEqual( "utf-8", result.Encoding );
    }

    /// <summary>
    /// CreateDirectory round-trip.
    /// </summary>
    [TestMethod]
    public void CreateDirectory_RoundTrips( ) {
        CreateDirectoryParameters result = RoundTrip<CreateDirectoryParameters>( new {
            path = "/new-dir",
        } );

        Assert.AreEqual( "/new-dir", result.Path );
    }

    /// <summary>
    /// TestExists with PathType enum string round-trip.
    /// </summary>
    [TestMethod]
    public void TestExists_RoundTrips( ) {
        TestExistsParameters result = RoundTrip<TestExistsParameters>( new {
            path = "/check",
            type = "Directory",
        } );

        Assert.AreEqual( "/check", result.Path );
    }

    // ── Content operations ──────────────────────────────────────────

    /// <summary>
    /// ClearContent round-trip.
    /// </summary>
    [TestMethod]
    public void ClearContent_RoundTrips( ) {
        ClearContentParameters result = RoundTrip<ClearContentParameters>( new {
            path = "/clear.txt",
        } );

        Assert.AreEqual( "/clear.txt", result.Path );
    }

    /// <summary>
    /// WriteContent with Append and Encoding round-trip.
    /// </summary>
    [TestMethod]
    public void WriteContent_RoundTrips( ) {
        WriteContentParameters result = RoundTrip<WriteContentParameters>( new {
            path = "/out.txt",
            content = "data",
            append = true,
            encoding = "utf-16",
        } );

        Assert.AreEqual( "/out.txt", result.Path );
        Assert.AreEqual( "data", result.Content );
        Assert.IsTrue( result.Append );
        Assert.AreEqual( "utf-16", result.Encoding );
    }

    /// <summary>
    /// ReadContent with MaxBytes round-trip.
    /// </summary>
    [TestMethod]
    public void ReadContent_RoundTrips( ) {
        ReadContentParameters result = RoundTrip<ReadContentParameters>( new {
            path = "/read.txt",
            encoding = "utf-8",
            maxBytes = 4096L,
        } );

        Assert.AreEqual( "/read.txt", result.Path );
        Assert.AreEqual( "utf-8", result.Encoding );
        Assert.AreEqual( 4096L, result.MaxBytes );
    }

    /// <summary>
    /// FindReplace with all fields round-trip.
    /// </summary>
    [TestMethod]
    public void FindReplace_RoundTrips( ) {
        FindReplaceParameters result = RoundTrip<FindReplaceParameters>( new {
            path = "/config.xml",
            find = "localhost",
            replace = "prod",
            isRegex = false,
            caseSensitive = false,
            encoding = "utf-8",
        } );

        Assert.AreEqual( "localhost", result.Find );
        Assert.AreEqual( "prod", result.Replace );
        Assert.IsFalse( result.IsRegex );
        Assert.IsFalse( result.CaseSensitive );
    }

    // ── File information ────────────────────────────────────────────

    /// <summary>
    /// GetFileInfo round-trip.
    /// </summary>
    [TestMethod]
    public void GetFileInfo_RoundTrips( ) {
        GetFileInfoParameters result = RoundTrip<GetFileInfoParameters>( new {
            path = "/info.dat",
        } );

        Assert.AreEqual( "/info.dat", result.Path );
    }

    /// <summary>
    /// ListDirectory with enum fields round-trip.
    /// </summary>
    [TestMethod]
    public void ListDirectory_RoundTrips( ) {
        ListDirectoryParameters result = RoundTrip<ListDirectoryParameters>( new {
            path = "/data",
            pattern = "*.csv",
            recursive = true,
            type = "Directory",
            sortBy = "Modified",
        } );

        Assert.AreEqual( "/data", result.Path );
        Assert.AreEqual( "*.csv", result.Pattern );
        Assert.IsTrue( result.Recursive );
    }

    // ── Archive operations ──────────────────────────────────────────

    /// <summary>
    /// CompressArchive with enum Format and CompressionLevel round-trip.
    /// </summary>
    [TestMethod]
    public void CompressArchive_RoundTrips( ) {
        CompressArchiveParameters result = RoundTrip<CompressArchiveParameters>( new {
            source = "/src",
            destination = "/dst.tar.gz",
            format = "TarGz",
            compressionLevel = "Fastest",
            includeBaseDirectory = true,
            overwrite = false,
        } );

        Assert.AreEqual( "/src", result.Source );
        Assert.AreEqual( "/dst.tar.gz", result.Destination );
    }

    /// <summary>
    /// ExpandArchive round-trip.
    /// </summary>
    [TestMethod]
    public void ExpandArchive_RoundTrips( ) {
        ExpandArchiveParameters result = RoundTrip<ExpandArchiveParameters>( new {
            source = "/archive.zip",
            destination = "/out",
            overwrite = true,
            format = "Auto",
        } );

        Assert.AreEqual( "/archive.zip", result.Source );
        Assert.AreEqual( "/out", result.Destination );
        Assert.IsTrue( result.Overwrite );
    }

    // ── Process operations ──────────────────────────────────────────

    /// <summary>
    /// StartProcess with conditional TimeoutMs round-trip.
    /// </summary>
    [TestMethod]
    public void StartProcess_RoundTrips( ) {
        StartProcessParameters result = RoundTrip<StartProcessParameters>( new {
            fileName = "dotnet",
            arguments = "build",
            workingDirectory = "/repo",
            waitForExit = true,
            timeoutMs = 60000,
        } );

        Assert.AreEqual( "dotnet", result.FileName );
        Assert.AreEqual( "build", result.Arguments );
        Assert.IsTrue( result.WaitForExit );
        Assert.AreEqual<int?>( 60000, result.TimeoutMs );
    }

    /// <summary>
    /// StopProcess round-trip.
    /// </summary>
    [TestMethod]
    public void StopProcess_RoundTrips( ) {
        StopProcessParameters result = RoundTrip<StopProcessParameters>( new {
            processName = "notepad",
            force = true,
        } );

        Assert.AreEqual( "notepad", result.ProcessName );
        Assert.IsTrue( result.Force );
    }

    // ── Control operations ──────────────────────────────────────────

    /// <summary>
    /// Delay with double Seconds round-trip.
    /// </summary>
    [TestMethod]
    public void Delay_RoundTrips( ) {
        DelayParameters result = RoundTrip<DelayParameters>( new {
            seconds = 2.5,
            reason = "Wait for it",
        } );

        Assert.AreEqual( 2.5, result.Seconds, 0.001 );
        Assert.AreEqual( "Wait for it", result.Reason );
    }

    // ── Event operations ────────────────────────────────────────────

    /// <summary>
    /// WatchFile with enum Mode and numeric defaults round-trip.
    /// </summary>
    [TestMethod]
    public void WatchFile_RoundTrips( ) {
        WatchFileParameters result = RoundTrip<WatchFileParameters>( new {
            directory = "/drop",
            pattern = "*.csv",
            stabilitySeconds = 10,
            timeoutSeconds = 600,
            pollIntervalMs = 2000,
            mode = "ExitQuietly",
            usePolling = true,
        } );

        Assert.AreEqual( "/drop", result.Directory );
        Assert.AreEqual( "*.csv", result.Pattern );
        Assert.AreEqual( 10, result.StabilitySeconds );
        Assert.IsTrue( result.UsePolling );
    }

    // ── Iteration ───────────────────────────────────────────────────

    /// <summary>
    /// ForEach: simple text field round-trip.
    /// </summary>
    [TestMethod]
    public void ForEach_RoundTrips( ) {
        ForEachParameters result = RoundTrip<ForEachParameters>( new {
            arrayPropertyName = "items",
        } );

        Assert.AreEqual( "items", result.ArrayPropertyName );
    }

    // ── Network operations (new complex types) ──────────────────────

    /// <summary>
    /// HttpRequest: KeyValueMap (Headers), IntArray (ExpectedStatusCodes), ShowWhen fields.
    /// This is the most complex serialization test.
    /// </summary>
    [TestMethod]
    public void HttpRequest_RoundTrips_Headers_And_StatusCodes( ) {
        HttpRequestParameters result = RoundTrip<HttpRequestParameters>( new {
            url = "https://api.example.com",
            method = "POST",
            headers = new Dictionary<string, string> {
                ["authorization"] = "Bearer token123",
                ["accept"] = "application/json",
            },
            body = "{\"key\":\"val\"}",
            contentType = "application/json",
            timeoutSeconds = 60,
            expectedStatusCodes = new[] { 200, 201 },
            followRedirects = true,
        } );

        Assert.AreEqual( "https://api.example.com", result.Url );
        Assert.AreEqual( "POST", result.Method );
        Assert.IsNotNull( result.Headers );
        Assert.HasCount( 2, result.Headers );
        Assert.AreEqual( "{\"key\":\"val\"}", result.Body );
        Assert.AreEqual( "application/json", result.ContentType );
        Assert.AreEqual( 60, result.TimeoutSeconds );
        Assert.HasCount( 2, result.ExpectedStatusCodes );
        CollectionAssert.AreEqual( new[] { 200, 201 }, result.ExpectedStatusCodes );
        Assert.IsTrue( result.FollowRedirects );
    }

    /// <summary>
    /// DownloadFile: KeyValueMap Headers round-trip.
    /// </summary>
    [TestMethod]
    public void DownloadFile_RoundTrips_Headers( ) {
        DownloadFileParameters result = RoundTrip<DownloadFileParameters>( new {
            url = "https://example.com/file.zip",
            destination = "/downloads/file.zip",
            headers = new Dictionary<string, string> {
                ["authorization"] = "Bearer abc",
            },
            overwrite = true,
            timeoutSeconds = 120,
        } );

        Assert.AreEqual( "https://example.com/file.zip", result.Url );
        Assert.AreEqual( "/downloads/file.zip", result.Destination );
        Assert.IsNotNull( result.Headers );
        Assert.IsNotEmpty( result.Headers );
        Assert.IsTrue( result.Overwrite );
        Assert.AreEqual( 120, result.TimeoutSeconds );
    }

    /// <summary>
    /// TestConnection: enum Protocol and ShowWhen ExpectedStatusCode round-trip.
    /// </summary>
    [TestMethod]
    public void TestConnection_RoundTrips_Protocol_And_StatusCode( ) {
        TestConnectionParameters result = RoundTrip<TestConnectionParameters>( new {
            host = "example.com",
            port = 443,
            protocol = "Https",
            timeoutSeconds = 15,
            expectedStatusCode = 200,
        } );

        Assert.AreEqual( "example.com", result.Host );
        Assert.AreEqual( 443, result.Port );
        Assert.AreEqual( ConnectionProtocol.Https, result.Protocol );
        Assert.AreEqual( 15, result.TimeoutSeconds );
        Assert.AreEqual( 200, result.ExpectedStatusCode );
    }

    /// <summary>
    /// UploadFile: KeyValueMap Headers and Select Method round-trip.
    /// </summary>
    [TestMethod]
    public void UploadFile_RoundTrips_Headers( ) {
        UploadFileParameters result = RoundTrip<UploadFileParameters>( new {
            filePath = "/data/report.pdf",
            url = "https://api.example.com/upload",
            method = "PUT",
            formFieldName = "document",
            headers = new Dictionary<string, string> {
                ["x-api-key"] = "secret",
            },
            timeoutSeconds = 600,
        } );

        Assert.AreEqual( "/data/report.pdf", result.FilePath );
        Assert.AreEqual( "https://api.example.com/upload", result.Url );
        Assert.AreEqual( "PUT", result.Method );
        Assert.AreEqual( "document", result.FormFieldName );
        Assert.IsNotNull( result.Headers );
        Assert.AreEqual( 600, result.TimeoutSeconds );
    }

    // ── Notification operations ─────────────────────────────────────

    /// <summary>
    /// SendEmail: StringArray (To, Cc, Attachments) round-trip.
    /// </summary>
    [TestMethod]
    public void SendEmail_RoundTrips_StringArrays( ) {
        SendEmailParameters result = RoundTrip<SendEmailParameters>( new {
            smtpHost = "smtp.example.com",
            port = 587,
            useSsl = true,
            credentialName = "smtp-cred",
            from = "noreply@example.com",
            to = new[] { "user1@example.com", "user2@example.com" },
            cc = new[] { "cc@example.com" },
            subject = "Test Email",
            body = "<h1>Hello</h1>",
            isHtml = true,
            attachments = new[] { "/path/to/file.pdf" },
        } );

        Assert.AreEqual( "smtp.example.com", result.SmtpHost );
        Assert.AreEqual( 587, result.Port );
        Assert.IsTrue( result.UseSsl );
        Assert.AreEqual( "noreply@example.com", result.From );
        Assert.HasCount( 2, result.To );
        Assert.AreEqual( "user1@example.com", result.To[0] );
        Assert.IsNotNull( result.Cc );
        Assert.HasCount( 1, result.Cc );
        Assert.AreEqual( "Test Email", result.Subject );
        Assert.IsTrue( result.IsHtml );
        Assert.IsNotNull( result.Attachments );
        Assert.HasCount( 1, result.Attachments );
    }

    /// <summary>
    /// SendWebhook: KeyValueMap Headers and optional Payload round-trip.
    /// </summary>
    [TestMethod]
    public void SendWebhook_RoundTrips_Headers( ) {
        SendWebhookParameters result = RoundTrip<SendWebhookParameters>( new {
            url = "https://hooks.example.com/notify",
            payload = "{\"text\":\"done\"}",
            headers = new Dictionary<string, string> {
                ["authorization"] = "Bearer webhook-token",
            },
            timeoutSeconds = 15,
        } );

        Assert.AreEqual( "https://hooks.example.com/notify", result.Url );
        Assert.AreEqual( "{\"text\":\"done\"}", result.Payload );
        Assert.IsNotNull( result.Headers );
        Assert.AreEqual( 15, result.TimeoutSeconds );
    }

    // ── Data operations ─────────────────────────────────────────────

    /// <summary>
    /// TransformJson: ObjectArray (Operations) with sub-objects round-trip.
    /// This validates that nested objects serialize/deserialize correctly through
    /// the UI→API JSON contract.
    /// </summary>
    [TestMethod]
    public void TransformJson_RoundTrips_Operations( ) {
        TransformJsonParameters result = RoundTrip<TransformJsonParameters>( new {
            inputPath = "/in.json",
            outputPath = "/out.json",
            operations = new[] {
                new { type = "Extract", path = "/name", value = (string?)null },
                new { type = "Set", path = "/status", value = (string?)"\"active\"" },
                new { type = "Delete", path = "/temp", value = (string?)null },
                new { type = "Merge", path = "/config", value = (string?)"{\"debug\":true}" },
            },
        } );

        Assert.AreEqual( "/in.json", result.InputPath );
        Assert.AreEqual( "/out.json", result.OutputPath );
        Assert.HasCount( 4, result.Operations );
        Assert.AreEqual( JsonTransformType.Extract, result.Operations[0].Type );
        Assert.AreEqual( "/name", result.Operations[0].Path );
        Assert.AreEqual( JsonTransformType.Set, result.Operations[1].Type );
        Assert.AreEqual( "\"active\"", result.Operations[1].Value );
        Assert.AreEqual( JsonTransformType.Delete, result.Operations[2].Type );
        Assert.AreEqual( JsonTransformType.Merge, result.Operations[3].Type );
    }

    // ── Edge Cases ──────────────────────────────────────────────────

    /// <summary>
    /// Verifies that null optional fields are handled correctly (not included
    /// in serialized JSON when null, and deserialized as null on the API side).
    /// </summary>
    [TestMethod]
    public void Null_Optional_Fields_Handled_Correctly( ) {
        HttpRequestParameters result = RoundTrip<HttpRequestParameters>( new {
            url = "https://example.com",
            method = "GET",
        } );

        Assert.IsNull( result.Headers, "Optional Headers should be null when not provided." );
        Assert.IsNull( result.Body, "Optional Body should be null when not provided." );
        Assert.IsNull( result.OutputFilePath, "Optional OutputFilePath should be null." );
    }

    /// <summary>
    /// Verifies that default values in parameter records are preserved when
    /// the field is not present in the serialized JSON.
    /// </summary>
    [TestMethod]
    public void Default_Values_Preserved_When_Not_Serialized( ) {
        // Only provide required fields
        HttpRequestParameters result = RoundTrip<HttpRequestParameters>( new {
            url = "https://example.com",
        } );

        Assert.AreEqual( "GET", result.Method, "Default Method should be GET." );
        Assert.AreEqual( 30, result.TimeoutSeconds, "Default TimeoutSeconds should be 30." );
        Assert.IsFalse( result.FollowRedirects, "Default FollowRedirects should be false." );
    }

    /// <summary>
    /// Empty dictionary serialized from UI should deserialize as empty (or null depending on type).
    /// </summary>
    [TestMethod]
    public void Empty_Dictionary_Roundtrips( ) {
        DownloadFileParameters result = RoundTrip<DownloadFileParameters>( new {
            url = "https://example.com/file",
            destination = "/out",
            headers = new Dictionary<string, string>( ),
        } );

        // Empty dict may be empty or null depending on JSON settings
        if (result.Headers is not null) {
            Assert.HasCount( 0, result.Headers );
        }
    }
}
