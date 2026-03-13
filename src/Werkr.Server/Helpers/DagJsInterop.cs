using Microsoft.JSInterop;
using Werkr.Common.Models;

namespace Werkr.Server.Helpers;

/// <summary>
/// Typed JS interop wrapper for the AntV X6 DAG canvas.
/// Manages <see cref="IJSObjectReference"/> lifecycle and DotNetObjectReference callbacks.
/// </summary>
public sealed class DagJsInterop : IAsyncDisposable {

    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private DotNetObjectReference<DagJsInterop>? _dotNetRef;

    /// <summary>Raised when a user clicks a DAG node (passes the step ID).</summary>
    public event Func<long, Task>? OnNodeClicked;

    /// <summary>Raised when the selection changes (passes selected step IDs).</summary>
    public event Func<long[], Task>? OnSelectionChanged;

    /// <summary>Raised when the zoom level changes.</summary>
    public event Func<double, Task>? OnZoomChanged;

    /// <summary>Creates a new interop wrapper using the specified JS runtime.</summary>
    public DagJsInterop( IJSRuntime js ) {
        _js = js;
    }

    /// <summary>Load the DAG JS module and create the X6 graph instance.</summary>
    public async Task InitAsync( string containerId, string minimapContainerId ) {
        _module = await _js.InvokeAsync<IJSObjectReference>(
            "import", "/js/dist/dag-readonly.js" );
        _dotNetRef = DotNetObjectReference.Create( this );
        await _module.InvokeVoidAsync( "initGraph", containerId, minimapContainerId, _dotNetRef );
    }

    /// <summary>Load nodes and edges into the graph with Dagre layout.</summary>
    public async Task LoadGraphAsync( DagNodeDto[] nodes, DagEdgeDto[] edges ) {
        if (_module is null) return;
        await _module.InvokeVoidAsync( "loadGraph", nodes, edges );
    }

    /// <summary>Update the execution status of a single node (for SignalR incremental updates).</summary>
    public async Task UpdateNodeStatusAsync( long stepId, string status ) {
        if (_module is null) return;
        await _module.InvokeVoidAsync( "updateNodeStatus", stepId, status );
    }

    /// <summary>Apply execution status to all nodes from a status dictionary.</summary>
    public async Task ApplyAllStatusesAsync( Dictionary<long, string> statuses ) {
        if (_module is null) return;
        await _module.InvokeVoidAsync( "applyAllStatuses", statuses );
    }

    /// <summary>Clear all execution status overlays.</summary>
    public async Task ClearStatusesAsync( ) {
        if (_module is null) return;
        await _module.InvokeVoidAsync( "clearStatuses" );
    }

    /// <summary>Zoom to fit all content in the viewport.</summary>
    public async Task ZoomToFitAsync( ) {
        if (_module is null) return;
        await _module.InvokeVoidAsync( "zoomToFit" );
    }

    /// <summary>Zoom to a specific scale level.</summary>
    public async Task ZoomToAsync( double scale ) {
        if (_module is null) return;
        await _module.InvokeVoidAsync( "zoomTo", scale );
    }

    /// <summary>Switch layout direction (LR ↔ TB) and re-layout.</summary>
    public async Task SetLayoutDirectionAsync( string direction ) {
        if (_module is null) return;
        await _module.InvokeVoidAsync( "setLayoutDirection", direction );
    }

    /// <summary>Destroy the graph and clean up JS resources.</summary>
    public async Task DestroyAsync( ) {
        if (_module is null) return;
        await _module.InvokeVoidAsync( "destroyGraph" );
    }

    /// <summary>Callback invoked from JS when a user clicks a DAG node.</summary>
    [JSInvokable]
    public async Task OnNodeClickedCallback( long stepId ) {
        if (OnNodeClicked is not null) {
            await OnNodeClicked.Invoke( stepId );
        }
    }

    /// <summary>Callback invoked from JS when the selection changes.</summary>
    [JSInvokable]
    public async Task OnSelectionChangedCallback( long[] stepIds ) {
        if (OnSelectionChanged is not null) {
            await OnSelectionChanged.Invoke( stepIds );
        }
    }

    /// <summary>Callback invoked from JS when the zoom level changes.</summary>
    [JSInvokable]
    public async Task OnZoomChangedCallback( double zoom ) {
        if (OnZoomChanged is not null) {
            await OnZoomChanged.Invoke( zoom );
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
