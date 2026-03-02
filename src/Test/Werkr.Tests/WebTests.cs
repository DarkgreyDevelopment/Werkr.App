namespace Werkr.Tests;

/// <summary>
/// Basic connectivity test for the API hosted via Testcontainers + WebApplicationFactory.
/// All tests share the <see cref="AppHostFixture"/> instance.
/// </summary>
[TestClass]
public class WebTests {
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [Timeout( 30_000 )]
    public async Task GetApiRootReturnsOkStatusCode( ) {
        CancellationToken ct = TestContext.CancellationToken;
        HttpClient httpClient = AppHostFixture.ApiClient;
        HttpResponseMessage response = await httpClient.GetAsync( "/", ct );

        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );

        string content = await response.Content.ReadAsStringAsync( ct );
        Assert.IsTrue(
            content.Contains( "Werkr API", StringComparison.OrdinalIgnoreCase ),
            "API root should return a response containing 'Werkr API'." );
    }
}
