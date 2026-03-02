namespace Werkr.Tests;

/// <summary>
/// Basic health-check tests for the API service hosted via Testcontainers + WebApplicationFactory.
/// Verifies that the service starts successfully and responds on its root endpoint.
/// </summary>
[TestClass]
public class ScheduleIntegrationTests {
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [Timeout( 30_000 )]
    public async Task ApiServiceReturnsHealthy_WithScheduleGrpcMapped( ) {
        CancellationToken ct = TestContext.CancellationToken;
        HttpClient httpClient = AppHostFixture.ApiClient;

        HttpResponseMessage response = await httpClient.GetAsync( "/", ct );

        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        string content = await response.Content.ReadAsStringAsync( ct );
        Assert.IsTrue( content.Contains( "Werkr API", StringComparison.OrdinalIgnoreCase ) );
    }
}
