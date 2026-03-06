namespace Werkr.Tests;

/// <summary>
/// Basic connectivity test for the API hosted via Testcontainers + WebApplicationFactory.
/// All tests share the <see cref="AppHostFixture"/> instance.
/// </summary>
[TestClass]
public class WebTests {
    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> for the current test execution,
    /// providing access to test metadata and a <see cref="CancellationToken"/> for
    /// cooperative cancellation.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Verifies that sending an HTTP GET request to the API root endpoint (<c>/</c>)
    /// returns an <see cref="HttpStatusCode.OK"/> status code and that the response body
    /// contains the text "Werkr API", confirming the API application is running and serving
    /// its root resource. The test validates both the HTTP status and the content of the response.
    /// </summary>
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
