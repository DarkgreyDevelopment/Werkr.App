using Microsoft.JSInterop;
using Werkr.Common.Models;

namespace Werkr.Server.Helpers;

/// <summary>
/// Typed JS interop wrapper for the read-only AntV X6 DAG canvas.
/// Manages <see cref="IJSObjectReference"/> lifecycle and DotNetObjectReference callbacks.
/// </summary>
/// <remarks>Creates a new interop wrapper using the specified JS runtime.</remarks>
public sealed class DagJsInterop( IJSRuntime js ) : GraphJsInteropBase<DagJsInterop>( js ) {

    /// <inheritdoc/>
    protected override string ModulePath => "/js/dist/dag/dag-readonly.js";

    /// <inheritdoc/>
    protected override string DestroyFunctionName => "destroyGraph";

    /// <summary>Raised when a user clicks a DAG node (passes the step ID).</summary>
    public event Func<long, Task>? OnNodeClicked;

    /// <summary>Raised when the selection changes (passes selected step IDs).</summary>
    public event Func<long[], Task>? OnSelectionChanged;

    /// <summary>Raised when the zoom level changes.</summary>
    public event Func<double, Task>? OnZoomChanged;

    /// <summary>Load the DAG JS module and create the X6 graph instance.</summary>
    public async Task InitAsync( string containerId, string minimapContainerId ) {
        await LoadModuleAsync( );
        await InvokeVoidAsync( "initGraph", containerId, minimapContainerId, DotNetRef );
    }

    /// <summary>Load nodes and edges into the graph with Dagre layout.</summary>
    public async Task LoadGraphAsync( DagNodeDto[] nodes, DagEdgeDto[] edges ) {
        await InvokeVoidAsync( "loadGraph", nodes, edges );
    }

    /// <summary>Update the execution status of a single node (for SignalR incremental updates).</summary>
    public async Task UpdateNodeStatusAsync( long stepId, string status ) {
        await InvokeVoidAsync( "updateNodeStatus", stepId, status );
    }

    /// <summary>Apply execution status to all nodes from a status dictionary.</summary>
    public async Task ApplyAllStatusesAsync( Dictionary<long, string> statuses ) {
        await InvokeVoidAsync( "applyAllStatuses", statuses );
    }

    /// <summary>Clear all execution status overlays.</summary>
    public async Task ClearStatusesAsync( ) {
        await InvokeVoidAsync( "clearStatuses" );
    }

    /// <summary>Zoom to fit all content in the viewport.</summary>
    public async Task ZoomToFitAsync( ) {
        await InvokeVoidAsync( "zoomToFit" );
    }

    /// <summary>Zoom to a specific scale level.</summary>
    public async Task ZoomToAsync( double scale ) {
        await InvokeVoidAsync( "zoomTo", scale );
    }

    /// <summary>Switch layout direction (LR ↔ TB) and re-layout.</summary>
    public async Task SetLayoutDirectionAsync( string direction ) {
        await InvokeVoidAsync( "setLayoutDirection", direction );
    }

    /// <summary>Export the graph as SVG and return the SVG markup.</summary>
    public async Task<string> ExportSvgAsync( ) {
        return await InvokeAsync<string>( "exportSvgAsync" );
    }

    /// <summary>Export the graph as PNG (triggers browser download).</summary>
    public async Task ExportPngAsync( ) {
        await InvokeVoidAsync( "exportPngAsync" );
    }

    /// <summary>Load annotation nodes from JSON onto the read-only canvas.</summary>
    public async Task LoadAnnotationsAsync( string annotationsJson ) {
        await InvokeVoidAsync( "loadAnnotations", annotationsJson );
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
}
