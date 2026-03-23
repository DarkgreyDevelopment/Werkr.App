namespace Werkr.Tests;

/// <summary>
/// Basic health-check tests for the API service hosted via Testcontainers + WebApplicationFactory.
/// Verifies that the service starts successfully and responds on its root endpoint.
/// </summary>
[TestClass]
public class ScheduleIntegrationTests {
    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> for the current test execution,
    /// providing access to test metadata and a <see cref="CancellationToken"/> for cooperative cancellation.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Verifies that the <c>Werkr.Api</c> service returns a healthy response (HTTP 200 OK)
    /// at the root endpoint when schedule gRPC services are mapped. Sends a GET request to <c>/</c>
    /// and asserts that the response status is OK and the body contains "Werkr API", confirming the
    /// API is operational with schedule gRPC endpoints registered.
    /// </summary>
    [TestMethod]
    [Timeout( 30_000, CooperativeCancellation = true )]
    public async Task ApiServiceReturnsHealthy_WithScheduleGrpcMapped( ) {
        CancellationToken ct = TestContext.CancellationToken;
        HttpClient httpClient = AppHostFixture.ApiClient;

        HttpResponseMessage response = await httpClient.GetAsync( "/", ct );

        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        string content = await response.Content.ReadAsStringAsync( ct );
        Assert.IsTrue( content.Contains( "Werkr API", StringComparison.OrdinalIgnoreCase ) );
    }
}
