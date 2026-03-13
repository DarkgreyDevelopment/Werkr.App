using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;
using Werkr.Common.Models;

namespace Werkr.Server.Services;

/// <summary>
/// Two-tier filter persistence: personal filters in <c>localStorage</c>,
/// shared filters via the API (<c>/api/filters/{pageKey}</c>).
/// Must only be called after the first interactive render (<c>OnAfterRenderAsync</c>).
/// </summary>
public sealed class SavedFilterService( IJSRuntime js, IHttpClientFactory httpClientFactory, ILogger<SavedFilterService> logger ) {
    private readonly IJSRuntime _js = js;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ILogger<SavedFilterService> _logger = logger;

    private static readonly JsonSerializerOptions s_jsonOptions = new( ) {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Returns all saved filters for the given <paramref name="pageKey"/>.</summary>
    public async Task<IReadOnlyList<SavedFilterView>> GetFiltersAsync( string pageKey ) {
        string? json = await _js.InvokeAsync<string?>( "localStorage.getItem", StorageKey( pageKey ) );
        if (string.IsNullOrWhiteSpace( json )) {
            return [];
        }

        FilterStore? store = JsonSerializer.Deserialize<FilterStore>( json, s_jsonOptions );
        return store?.Filters ?? [];
    }

    /// <summary>Creates or updates a saved filter.</summary>
    public async Task SaveFilterAsync( string pageKey, SavedFilterView filter ) {
        List<SavedFilterView> existing = [.. await GetFiltersAsync( pageKey )];

        int idx = existing.FindIndex( f => f.Id == filter.Id );
        if (idx >= 0) {
            existing[idx] = filter;
        } else {
            existing.Add( filter );
        }

        await PersistAsync( pageKey, existing );
    }

    /// <summary>Deletes the filter with the specified <paramref name="filterId"/>.</summary>
    public async Task DeleteFilterAsync( string pageKey, string filterId ) {
        List<SavedFilterView> existing = [.. await GetFiltersAsync( pageKey )];
        _ = existing.RemoveAll( f => f.Id == filterId );
        await PersistAsync( pageKey, existing );
    }

    /// <summary>Marks the given filter as the default for the page, clearing any previous default.</summary>
    public async Task SetDefaultAsync( string pageKey, string filterId ) {
        List<SavedFilterView> existing = [.. await GetFiltersAsync( pageKey )];

        for (int i = 0; i < existing.Count; i++) {
            bool shouldBeDefault = existing[i].Id == filterId;
            existing[i] = existing[i] with { IsDefault = shouldBeDefault };
        }

        await PersistAsync( pageKey, existing );
    }

    private async Task PersistAsync( string pageKey, List<SavedFilterView> filters ) {
        FilterStore store = new( ) { Filters = filters };
        string json = JsonSerializer.Serialize( store, s_jsonOptions );
        await _js.InvokeVoidAsync( "localStorage.setItem", StorageKey( pageKey ), json );
    }

    private static string StorageKey( string pageKey ) => $"werkr-filters-{pageKey}";

    // ── Shared (server-synced) filters ──

    /// <summary>Returns all filters for <paramref name="pageKey"/> merging local + shared.</summary>
    public async Task<IReadOnlyList<SavedFilterView>> GetAllFiltersAsync( string pageKey ) {
        List<SavedFilterView> local = [.. await GetFiltersAsync( pageKey )];
        List<SavedFilterView> shared = [.. await GetSharedFiltersAsync( pageKey )];

        // Merge: local first, then shared that don't duplicate a local id
        HashSet<string> localIds = [.. local.Select( f => f.Id )];
        foreach (SavedFilterView sf in shared) {
            if (!localIds.Contains( sf.Id )) {
                local.Add( sf );
            }
        }

        return local;
    }

    /// <summary>Fetches shared filters from the API.</summary>
    public async Task<IReadOnlyList<SavedFilterView>> GetSharedFiltersAsync( string pageKey ) {
        try {
            HttpClient client = _httpClientFactory.CreateClient( "ApiService" );
            List<ServerFilterDto>? dtos = await client.GetFromJsonAsync<List<ServerFilterDto>>(
                $"/api/filters/{pageKey}", s_jsonOptions );

            if (dtos is null || dtos.Count == 0) {
                return [];
            }

            List<SavedFilterView> result = [];
            foreach (ServerFilterDto dto in dtos) {
                FilterCriteria? criteria = JsonSerializer.Deserialize<FilterCriteria>(
                    dto.CriteriaJson, s_jsonOptions );

                result.Add( new SavedFilterView(
                    Id: $"server-{dto.Id}",
                    Name: dto.Name,
                    IsDefault: false,
                    IsShared: dto.IsShared,
                    Criteria: criteria ?? new FilterCriteria( )
                ) );
            }

            return result;
        } catch (Exception ex) {
            _logger.LogWarning( ex, "Failed to fetch shared filters for page {PageKey}.", pageKey );
            return [];
        }
    }

    /// <summary>Creates a shared filter via the API.</summary>
    public async Task<bool> CreateSharedFilterAsync( string pageKey, string name, FilterCriteria criteria ) {
        try {
            HttpClient client = _httpClientFactory.CreateClient( "ApiService" );
            string criteriaJson = JsonSerializer.Serialize( criteria, s_jsonOptions );
            HttpResponseMessage response = await client.PostAsJsonAsync(
                $"/api/filters/{pageKey}",
                new { Name = name, CriteriaJson = criteriaJson },
                s_jsonOptions );
            return response.IsSuccessStatusCode;
        } catch (Exception ex) {
            _logger.LogWarning( ex, "Failed to create shared filter for page {PageKey}.", pageKey );
            return false;
        }
    }

    /// <summary>Deletes a shared filter via the API.</summary>
    public async Task<bool> DeleteSharedFilterAsync( string pageKey, long serverId ) {
        try {
            HttpClient client = _httpClientFactory.CreateClient( "ApiService" );
            HttpResponseMessage response = await client.DeleteAsync( $"/api/filters/{pageKey}/{serverId}" );
            return response.IsSuccessStatusCode;
        } catch (Exception ex) {
            _logger.LogWarning( ex, "Failed to delete shared filter {Id} for page {PageKey}.", serverId, pageKey );
            return false;
        }
    }

    /// <summary>Internal wrapper matching the localStorage JSON shape.</summary>
    private sealed class FilterStore {
        public List<SavedFilterView> Filters { get; set; } = [];
    }

    /// <summary>Matches the API response DTO shape.</summary>
    private sealed class ServerFilterDto {
        /// <summary>Server-assigned ID.</summary>
        public long Id { get; set; }
        /// <summary>Filter display name.</summary>
        public string Name { get; set; } = "";
        /// <summary>Page key.</summary>
        public string PageKey { get; set; } = "";
        /// <summary>Serialized filter criteria JSON.</summary>
        public string CriteriaJson { get; set; } = "{}";
        /// <summary>Whether the filter is shared.</summary>
        public bool IsShared { get; set; }
        /// <summary>Whether the current user is the owner.</summary>
        public bool IsOwner { get; set; }
    }
}
