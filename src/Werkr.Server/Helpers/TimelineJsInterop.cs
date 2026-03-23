using Microsoft.JSInterop;

namespace Werkr.Server.Helpers;

/// <summary>
/// Typed JS interop wrapper for the vis-timeline Gantt view.
/// Manages <see cref="IJSObjectReference"/> lifecycle and DotNetObjectReference callbacks.
/// </summary>
/// <remarks>Creates a new interop wrapper using the specified JS runtime.</remarks>
public sealed class TimelineJsInterop( IJSRuntime js ) : IAsyncDisposable {
    private IJSObjectReference? _module;
    private DotNetObjectReference<TimelineJsInterop>? _dotNetRef;

    /// <summary>Raised when a user clicks a bar in the timeline (passes the step ID).</summary>
    public event Func<long, Task>? OnItemClicked;

    /// <summary>Load the timeline JS module and create the vis-timeline instance.</summary>
    public async Task InitAsync( string containerId, object options ) {
        _module = await js.InvokeAsync<IJSObjectReference>(
            "import", "/js/dist/timeline-view.js" );
        _dotNetRef = DotNetObjectReference.Create( this );
        await _module.InvokeVoidAsync( "initTimeline", containerId, options, _dotNetRef );
    }

    /// <summary>Load items into the vis-timeline DataSet.</summary>
    public async Task LoadItemsAsync( IEnumerable<Werkr.Common.Models.GanttItemDto> items ) {
        if (_module is null) {
            return;
        }

        await _module.InvokeVoidAsync( "loadItems", items );
    }

    /// <summary>Update a single item in the DataSet (for live bar growth / status change).</summary>
    public async Task UpdateItemAsync( Werkr.Common.Models.GanttItemDto item ) {
        if (_module is null) {
            return;
        }

        await _module.InvokeVoidAsync( "updateItem", item );
    }

    /// <summary>Zoom to fit all items in the viewport.</summary>
    public async Task ZoomToFitAsync( ) {
        if (_module is null) {
            return;
        }

        await _module.InvokeVoidAsync( "zoomToFit" );
    }

    /// <summary>Set the visible time window.</summary>
    public async Task SetWindowAsync( DateTime start, DateTime end ) {
        if (_module is null) {
            return;
        }

        await _module.InvokeVoidAsync( "setWindow", start.ToString( "o" ), end.ToString( "o" ) );
    }

    /// <summary>Destroy the timeline instance and clean up JS resources.</summary>
    public async Task DestroyAsync( ) {
        if (_module is null) {
            return;
        }

        await _module.InvokeVoidAsync( "destroyTimeline" );
    }

    /// <summary>Callback invoked from JS when a user clicks a timeline bar.</summary>
    [JSInvokable]
    public async Task HandleItemClick( long stepId ) {
        if (OnItemClicked is not null) {
            await OnItemClicked.Invoke( stepId );
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync( ) {
        if (_module is not null) {
            try {
                await DestroyAsync( );
                await _module.DisposeAsync( );
            } catch (JSDisconnectedException) {
                // Circuit disconnected — JS cleanup not possible, safe to ignore
            }
        }
        _dotNetRef?.Dispose( );
    }
}
