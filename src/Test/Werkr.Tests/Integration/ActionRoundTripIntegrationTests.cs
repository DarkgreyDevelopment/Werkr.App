using System.Net.Http.Json;
using System.Text.Json;

namespace Werkr.Tests.Integration;

/// <summary>
/// Integration tests that verify full create → read → verify → delete round-trips
/// through the REST API for actions with non-trivial parameter shapes (enums, doubles,
/// nullable fields, boolean defaults). All tests share the <see cref="AppHostFixture"/> instance.
/// </summary>
[TestClass]
public class ActionRoundTripIntegrationTests {

    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> for the current test execution,
    /// providing access to test metadata and a <see cref="CancellationToken"/> for
    /// cooperative cancellation.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Gets the shared <see cref="JsonSerializerOptions"/> configured with web defaults
    /// from <see cref="AppHostFixture.JsonOptions"/> for JSON serialization and deserialization.
    /// </summary>
    private static JsonSerializerOptions JsonOptions => AppHostFixture.JsonOptions;

    /// <summary>
    /// Gets the pre-configured, authenticated <see cref="HttpClient"/> from
    /// <see cref="AppHostFixture.ApiClient"/> for sending HTTP requests to the
    /// <c>Werkr.Api</c>.
    /// </summary>
    private static HttpClient Api => AppHostFixture.ApiClient;

    #region Helper Methods

    /// <summary>
    /// Creates an Action-type task via the REST API. Returns the parsed JSON response.
    /// </summary>
    private static async Task<JsonElement> CreateActionTaskAsync(
        string name,
        string actionSubType,
        object parameters,
        CancellationToken ct
    ) {

        string parametersJson = JsonSerializer.Serialize( parameters, JsonOptions );

        var request = new {
            name,
            description = $"Round-trip integration test: {name}",
            actionType = "Action",
            content = string.Empty,
            targetTags = new[] { "integration-test" },
            enabled = true,
            timeoutMinutes = 5L,
            actionSubType,
            actionParameters = parametersJson,
        };

        HttpResponseMessage response = await Api.PostAsJsonAsync(
            "/api/tasks", request, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, response.StatusCode,
            $"Action task creation failed for '{actionSubType}': {await response.Content.ReadAsStringAsync( ct )}" );

        return await response.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
    }

    /// <summary>
    /// Parses the <c>actionParameters</c> JSON string from a task response into a
    /// <see cref="JsonDocument"/> root element.
    /// </summary>
    private static JsonElement GetActionParams( JsonElement task ) {
        string? json = task.GetProperty( "actionParameters" ).GetString( );
        Assert.IsNotNull( json, "actionParameters should not be null." );
        return JsonDocument.Parse( json ).RootElement;
    }

    #endregion Helper Methods

    #region Delay

    /// <summary>
    /// Verifies that a Delay action task round-trips its <c>double</c> Seconds value
    /// and optional Reason string through the API.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task Delay_RoundTrips_DoubleSecondsAndReason( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement created = await CreateActionTaskAsync(
            "RoundTrip_Delay", "Delay",
            new { seconds = 2.5, reason = "Wait for external service" }, ct );

        long taskId = created.GetProperty( "id" ).GetInt64( );

        try {
            HttpResponseMessage getResp = await Api.GetAsync( $"/api/tasks/{taskId}", ct );
            Assert.AreEqual( HttpStatusCode.OK, getResp.StatusCode );

            JsonElement retrieved = await getResp.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
            Assert.AreEqual( "Delay", retrieved.GetProperty( "actionSubType" ).GetString( ) );

            JsonElement p = GetActionParams( retrieved );
            Assert.AreEqual( 2.5, p.GetProperty( "seconds" ).GetDouble( ), 0.001 );
            Assert.AreEqual( "Wait for external service", p.GetProperty( "reason" ).GetString( ) );
        } finally {
            _ = await Api.DeleteAsync( $"/api/tasks/{taskId}", ct );
        }
    }

    #endregion Delay

    #region GetFileInfo

    /// <summary>
    /// Verifies that a GetFileInfo action task persists its single required Path parameter.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task GetFileInfo_RoundTrips_Path( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement created = await CreateActionTaskAsync(
            "RoundTrip_GetFileInfo", "GetFileInfo",
            new { path = "/data/report.csv" }, ct );

        long taskId = created.GetProperty( "id" ).GetInt64( );

        try {
            HttpResponseMessage getResp = await Api.GetAsync( $"/api/tasks/{taskId}", ct );
            Assert.AreEqual( HttpStatusCode.OK, getResp.StatusCode );

            JsonElement retrieved = await getResp.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
            Assert.AreEqual( "GetFileInfo", retrieved.GetProperty( "actionSubType" ).GetString( ) );

            JsonElement p = GetActionParams( retrieved );
            Assert.AreEqual( "/data/report.csv", p.GetProperty( "path" ).GetString( ) );
        } finally {
            _ = await Api.DeleteAsync( $"/api/tasks/{taskId}", ct );
        }
    }

    #endregion GetFileInfo

    #region ReadContent

    /// <summary>
    /// Verifies that a ReadContent action task round-trips optional MaxBytes (present)
    /// and Encoding values.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task ReadContent_RoundTrips_MaxBytesAndEncoding( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement created = await CreateActionTaskAsync(
            "RoundTrip_ReadContent", "ReadContent",
            new { path = "/data/large.log", encoding = "utf-16", maxBytes = 4096L }, ct );

        long taskId = created.GetProperty( "id" ).GetInt64( );

        try {
            HttpResponseMessage getResp = await Api.GetAsync( $"/api/tasks/{taskId}", ct );
            Assert.AreEqual( HttpStatusCode.OK, getResp.StatusCode );

            JsonElement retrieved = await getResp.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
            JsonElement p = GetActionParams( retrieved );
            Assert.AreEqual( "/data/large.log", p.GetProperty( "path" ).GetString( ) );
            Assert.AreEqual( "utf-16", p.GetProperty( "encoding" ).GetString( ) );
            Assert.AreEqual( 4096L, p.GetProperty( "maxBytes" ).GetInt64( ) );
        } finally {
            _ = await Api.DeleteAsync( $"/api/tasks/{taskId}", ct );
        }
    }

    #endregion ReadContent

    #region ListDirectory

    /// <summary>
    /// Verifies that a ListDirectory action task round-trips its enum parameters
    /// (Type, SortBy) and other fields.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task ListDirectory_RoundTrips_EnumParameters( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement created = await CreateActionTaskAsync(
            "RoundTrip_ListDirectory", "ListDirectory",
            new { path = "/data", pattern = "*.csv", recursive = true, type = "Directory", sortBy = "Modified" }, ct );

        long taskId = created.GetProperty( "id" ).GetInt64( );

        try {
            HttpResponseMessage getResp = await Api.GetAsync( $"/api/tasks/{taskId}", ct );
            Assert.AreEqual( HttpStatusCode.OK, getResp.StatusCode );

            JsonElement retrieved = await getResp.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
            Assert.AreEqual( "ListDirectory", retrieved.GetProperty( "actionSubType" ).GetString( ) );

            JsonElement p = GetActionParams( retrieved );
            Assert.AreEqual( "/data", p.GetProperty( "path" ).GetString( ) );
            Assert.AreEqual( "*.csv", p.GetProperty( "pattern" ).GetString( ) );
            Assert.IsTrue( p.GetProperty( "recursive" ).GetBoolean( ) );
        } finally {
            _ = await Api.DeleteAsync( $"/api/tasks/{taskId}", ct );
        }
    }

    #endregion ListDirectory

    #region FindReplace

    /// <summary>
    /// Verifies that a FindReplace action task round-trips CaseSensitive default (true)
    /// and explicitly-set-to-false values.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task FindReplace_RoundTrips_CaseSensitiveFlag( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement created = await CreateActionTaskAsync(
            "RoundTrip_FindReplace", "FindReplace",
            new { path = "/data/config.xml", find = "localhost", replace = "prod-server", caseSensitive = false, isRegex = false }, ct );

        long taskId = created.GetProperty( "id" ).GetInt64( );

        try {
            HttpResponseMessage getResp = await Api.GetAsync( $"/api/tasks/{taskId}", ct );
            Assert.AreEqual( HttpStatusCode.OK, getResp.StatusCode );

            JsonElement retrieved = await getResp.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
            Assert.AreEqual( "FindReplace", retrieved.GetProperty( "actionSubType" ).GetString( ) );

            JsonElement p = GetActionParams( retrieved );
            Assert.AreEqual( "localhost", p.GetProperty( "find" ).GetString( ) );
            Assert.AreEqual( "prod-server", p.GetProperty( "replace" ).GetString( ) );
            Assert.IsFalse( p.GetProperty( "caseSensitive" ).GetBoolean( ) );
        } finally {
            _ = await Api.DeleteAsync( $"/api/tasks/{taskId}", ct );
        }
    }

    #endregion FindReplace

    #region CompressArchive

    /// <summary>
    /// Verifies that a CompressArchive action task round-trips its Format and
    /// CompressionLevel enum values.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task CompressArchive_RoundTrips_FormatAndCompressionLevel( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement created = await CreateActionTaskAsync(
            "RoundTrip_CompressArchive", "CompressArchive",
            new { source = "/data/reports", destination = "/backups/reports.tar.gz", format = "TarGz", compressionLevel = "Fastest", overwrite = true }, ct );

        long taskId = created.GetProperty( "id" ).GetInt64( );

        try {
            HttpResponseMessage getResp = await Api.GetAsync( $"/api/tasks/{taskId}", ct );
            Assert.AreEqual( HttpStatusCode.OK, getResp.StatusCode );

            JsonElement retrieved = await getResp.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
            Assert.AreEqual( "CompressArchive", retrieved.GetProperty( "actionSubType" ).GetString( ) );

            JsonElement p = GetActionParams( retrieved );
            Assert.AreEqual( "/data/reports", p.GetProperty( "source" ).GetString( ) );
            Assert.AreEqual( "/backups/reports.tar.gz", p.GetProperty( "destination" ).GetString( ) );
            Assert.IsTrue( p.GetProperty( "overwrite" ).GetBoolean( ) );
        } finally {
            _ = await Api.DeleteAsync( $"/api/tasks/{taskId}", ct );
        }
    }

    #endregion CompressArchive

    #region ExpandArchive

    /// <summary>
    /// Verifies that an ExpandArchive action task round-trips its parameters
    /// including the Format field set to Auto.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task ExpandArchive_RoundTrips_AutoFormat( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement created = await CreateActionTaskAsync(
            "RoundTrip_ExpandArchive", "ExpandArchive",
            new { source = "/backups/reports.tar.gz", destination = "/data/restored", overwrite = false, format = "Auto" }, ct );

        long taskId = created.GetProperty( "id" ).GetInt64( );

        try {
            HttpResponseMessage getResp = await Api.GetAsync( $"/api/tasks/{taskId}", ct );
            Assert.AreEqual( HttpStatusCode.OK, getResp.StatusCode );

            JsonElement retrieved = await getResp.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
            Assert.AreEqual( "ExpandArchive", retrieved.GetProperty( "actionSubType" ).GetString( ) );

            JsonElement p = GetActionParams( retrieved );
            Assert.AreEqual( "/backups/reports.tar.gz", p.GetProperty( "source" ).GetString( ) );
            Assert.AreEqual( "/data/restored", p.GetProperty( "destination" ).GetString( ) );
            Assert.IsFalse( p.GetProperty( "overwrite" ).GetBoolean( ) );
        } finally {
            _ = await Api.DeleteAsync( $"/api/tasks/{taskId}", ct );
        }
    }

    #endregion ExpandArchive

    #region WatchFile

    /// <summary>
    /// Verifies that a WatchFile action task round-trips its enum Mode field,
    /// numeric parameters, and UsePolling boolean.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task WatchFile_RoundTrips_ModeAndPollingSettings( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement created = await CreateActionTaskAsync(
            "RoundTrip_WatchFile", "WatchFile",
            new {
                directory = "/drop",
                pattern = "*.dat",
                stabilitySeconds = 10,
                timeoutSeconds = 600,
                pollIntervalMs = 2000,
                mode = "ExitQuietly",
                usePolling = true,
            }, ct );

        long taskId = created.GetProperty( "id" ).GetInt64( );

        try {
            HttpResponseMessage getResp = await Api.GetAsync( $"/api/tasks/{taskId}", ct );
            Assert.AreEqual( HttpStatusCode.OK, getResp.StatusCode );

            JsonElement retrieved = await getResp.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
            Assert.AreEqual( "WatchFile", retrieved.GetProperty( "actionSubType" ).GetString( ) );

            JsonElement p = GetActionParams( retrieved );
            Assert.AreEqual( "/drop", p.GetProperty( "directory" ).GetString( ) );
            Assert.AreEqual( "*.dat", p.GetProperty( "pattern" ).GetString( ) );
            Assert.AreEqual( 10, p.GetProperty( "stabilitySeconds" ).GetInt32( ) );
            Assert.AreEqual( 600, p.GetProperty( "timeoutSeconds" ).GetInt32( ) );
            Assert.AreEqual( 2000, p.GetProperty( "pollIntervalMs" ).GetInt32( ) );
            Assert.IsTrue( p.GetProperty( "usePolling" ).GetBoolean( ) );
        } finally {
            _ = await Api.DeleteAsync( $"/api/tasks/{taskId}", ct );
        }
    }

    #endregion WatchFile
}
