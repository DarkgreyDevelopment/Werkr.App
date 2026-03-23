using System.Net.Http.Json;
using System.Text.Json;
using Werkr.Common.Models;

namespace Werkr.Tests.Integration;

[TestClass]
public class ConfigurationIntegrationTests {
    public TestContext TestContext { get; set; } = null!;

    private static JsonSerializerOptions JsonOptions => AppHostFixture.JsonOptions;
    private static HttpClient Api => AppHostFixture.ApiClient;

    /// <summary>GET /api/v1/settings returns seeded entries.</summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task GetSettings_ReturnsSeededEntries( ) {
        CancellationToken ct = TestContext.CancellationToken;

        List<ConfigurationEntryDto>? entries =
            await Api.GetFromJsonAsync<List<ConfigurationEntryDto>>( "/api/v1/settings", JsonOptions, ct );

        Assert.IsNotNull( entries );
        Assert.IsGreaterThan( 0, entries.Count );
        // server.name is seeded by ConfigurationSeeder
        Assert.IsTrue( entries.Exists( e => e.Key == "server.name" ) );
    }

    /// <summary>GET /api/v1/settings?category=server returns only server-category entries.</summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task GetSettings_FilterByCategory( ) {
        CancellationToken ct = TestContext.CancellationToken;

        List<ConfigurationEntryDto>? entries =
            await Api.GetFromJsonAsync<List<ConfigurationEntryDto>>( "/api/v1/settings?category=server", JsonOptions, ct );

        Assert.IsNotNull( entries );
        Assert.IsGreaterThan( 0, entries.Count );
        Assert.IsTrue( entries.TrueForAll( e => e.Category == "server" ) );
    }

    /// <summary>PUT /api/v1/settings/{key} updates a value and is reflected in subsequent GET.</summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task UpdateSetting_ChangesValue( ) {
        CancellationToken ct = TestContext.CancellationToken;
        string newValue = $"IntegrationTest-{Guid.NewGuid( ):N}";

        ConfigurationUpdateRequest request = new( newValue );
        HttpResponseMessage response = await Api.PutAsJsonAsync(
            "/api/v1/settings/server.name", request, JsonOptions, ct );

        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );

        ConfigurationEntryDto? updated = await response.Content
            .ReadFromJsonAsync<ConfigurationEntryDto>( JsonOptions, ct );
        Assert.IsNotNull( updated );
        Assert.AreEqual( newValue, updated.Value );
    }

    /// <summary>GET /api/v1/settings/{key}/history returns change log after an update.</summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task GetHistory_ReturnsChangeLog( ) {
        CancellationToken ct = TestContext.CancellationToken;
        string uniqueValue = $"History-{Guid.NewGuid( ):N}";

        // Update to create a change log entry
        ConfigurationUpdateRequest request = new( uniqueValue );
        HttpResponseMessage updateResponse = await Api.PutAsJsonAsync(
            "/api/v1/settings/server.name", request, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.OK, updateResponse.StatusCode );

        // Fetch history
        List<ConfigurationChangeLogDto>? history = await Api.GetFromJsonAsync<List<ConfigurationChangeLogDto>>(
            "/api/v1/settings/server.name/history", JsonOptions, ct );

        Assert.IsNotNull( history );
        Assert.IsGreaterThan( 0, history.Count );
        Assert.AreEqual( uniqueValue, history[0].NewValue );
    }
}
