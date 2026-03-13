using Microsoft.JSInterop;

namespace Werkr.Server.Helpers;

/// <summary>
/// Typed JS interop wrapper for the interactive DAG editor canvas.
/// Loads <c>dag-editor.js</c> and exposes editor-specific operations plus JS→.NET callbacks.
/// </summary>
public sealed class DagEditorJsInterop : GraphJsInteropBase<DagEditorJsInterop> {

    /// <inheritdoc/>
    protected override string ModulePath => "/js/dist/dag-editor.js";

    /// <inheritdoc/>
    protected override string DestroyFunctionName => "destroyEditor";

    /// <summary>Raised when a node is selected on the canvas.</summary>
    public event Func<long, Task>? OnNodeSelected;

    /// <summary>Raised when the node selection is cleared.</summary>
    public event Func<Task>? OnNodeDeselected;

    /// <summary>Raised when a node is dropped from the palette onto the canvas.</summary>
    public event Func<string, double, double, Task>? OnNodeDropped;

    /// <summary>Raised when the user presses Ctrl+S.</summary>
    public event Func<Task>? OnSaveRequested;

    /// <summary>Raised when a connection would create a cycle.</summary>
    public event Func<string, string, Task>? OnCycleDetected;

    /// <summary>Raised when the dirty count changes.</summary>
    public event Func<int, Task>? OnGraphDirtyChanged;

    /// <summary>Raised when the selection changes (passes selected step IDs).</summary>
    public event Func<long[], Task>? OnSelectionChanged;

    /// <summary>Raised when the zoom level changes.</summary>
    public event Func<double, Task>? OnZoomChanged;

    /// <summary>Creates a new editor interop wrapper using the specified JS runtime.</summary>
    public DagEditorJsInterop( IJSRuntime js ) : base( js ) { }

    /// <summary>Load the editor JS module and create the editable X6 graph instance.</summary>
    public async Task InitEditorAsync( string containerId, string minimapContainerId, string userId, long workflowId ) {
        await LoadModuleAsync( );
        await InvokeVoidAsync( "initEditor", containerId, minimapContainerId, DotNetRef, userId, workflowId );
    }

    /// <summary>Load nodes and edges into the editor graph.</summary>
    public async Task LoadGraphAsync( object[] nodes, object[] edges, object? layoutConfig = null ) {
        await InvokeVoidAsync( "loadGraph", nodes, edges, layoutConfig );
    }

    /// <summary>Add a node to the canvas at the specified position.</summary>
    public async Task AddNodeAsync( long stepId, object data, double x, double y ) {
        await InvokeVoidAsync( "addNode", stepId, data, x, y );
    }

    /// <summary>Remove one or more nodes from the canvas.</summary>
    public async Task RemoveNodesAsync( long[] stepIds ) {
        await InvokeVoidAsync( "removeNodes", stepIds );
    }

    /// <summary>Update data fields on an existing node.</summary>
    public async Task UpdateNodeDataAsync( long stepId, object fields ) {
        await InvokeVoidAsync( "updateNodeData", stepId, fields );
    }

    /// <summary>Undo the last editor action.</summary>
    public async Task UndoAsync( ) {
        await InvokeVoidAsync( "undo" );
    }

    /// <summary>Redo the last undone editor action.</summary>
    public async Task RedoAsync( ) {
        await InvokeVoidAsync( "redo" );
    }

    /// <summary>Get the serialized changeset JSON.</summary>
    public async Task<string> GetChangesetJsonAsync( ) {
        return await InvokeAsync<string>( "getChangeset" );
    }

    /// <summary>Clear all tracked changes in the changeset.</summary>
    public async Task ClearChangesetAsync( ) {
        await InvokeVoidAsync( "clearChangeset" );
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

    /// <summary>Run Dagre auto-layout on the current graph.</summary>
    public async Task AutoLayoutAsync( ) {
        await InvokeVoidAsync( "autoLayout" );
    }

    /// <summary>Start a palette drag operation via the X6 Dnd plugin.</summary>
    public async Task StartPaletteDragAsync( string actionType, double clientX, double clientY ) {
        await InvokeVoidAsync( "startPaletteDrag", actionType, clientX, clientY );
    }

    /// <summary>Apply execution status to all nodes from a status dictionary.</summary>
    public async Task ApplyAllStatusesAsync( Dictionary<long, string> statuses ) {
        await InvokeVoidAsync( "applyAllStatuses", statuses );
    }

    /// <summary>Update the execution status of a single node.</summary>
    public async Task UpdateNodeStatusAsync( long stepId, string status ) {
        await InvokeVoidAsync( "updateNodeStatus", stepId, status );
    }

    /// <summary>Clear all execution status overlays.</summary>
    public async Task ClearStatusesAsync( ) {
        await InvokeVoidAsync( "clearStatuses" );
    }

    /// <summary>Check if a draft exists in localStorage.</summary>
    public async Task<bool> CheckDraftAsync( string userId, long workflowId ) {
        return await InvokeAsync<bool>( "checkDraft", userId, workflowId );
    }

    /// <summary>Restore changeset from a saved draft.</summary>
    public async Task RestoreDraftAsync( string userId, long workflowId ) {
        await InvokeVoidAsync( "restoreDraft", userId, workflowId );
    }

    /// <summary>Dismiss (delete) the saved draft.</summary>
    public async Task DismissDraftAsync( string userId, long workflowId ) {
        await InvokeVoidAsync( "dismissDraft", userId, workflowId );
    }

    // ── JS→.NET Callbacks ──

    /// <summary>Callback invoked from JS when a node is selected.</summary>
    [JSInvokable]
    public async Task OnNodeSelectedCallback( long stepId ) {
        if (OnNodeSelected is not null) {
            await OnNodeSelected.Invoke( stepId );
        }
    }

    /// <summary>Callback invoked from JS when node selection is cleared.</summary>
    [JSInvokable]
    public async Task OnNodeDeselectedCallback( ) {
        if (OnNodeDeselected is not null) {
            await OnNodeDeselected.Invoke( );
        }
    }

    /// <summary>Callback invoked from JS when a node is dropped from the palette.</summary>
    [JSInvokable]
    public async Task OnNodeDroppedCallback( string actionType, double x, double y ) {
        if (OnNodeDropped is not null) {
            await OnNodeDropped.Invoke( actionType, x, y );
        }
    }

    /// <summary>Callback invoked from JS when user presses Ctrl+S.</summary>
    [JSInvokable]
    public async Task OnSaveRequestedCallback( ) {
        if (OnSaveRequested is not null) {
            await OnSaveRequested.Invoke( );
        }
    }

    /// <summary>Callback invoked from JS when a connection would create a cycle.</summary>
    [JSInvokable]
    public async Task OnCycleDetectedCallback( string sourceId, string targetId ) {
        if (OnCycleDetected is not null) {
            await OnCycleDetected.Invoke( sourceId, targetId );
        }
    }

    /// <summary>Callback invoked from JS when the dirty count changes.</summary>
    [JSInvokable]
    public async Task OnGraphDirtyChangedCallback( int dirtyCount ) {
        if (OnGraphDirtyChanged is not null) {
            await OnGraphDirtyChanged.Invoke( dirtyCount );
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
