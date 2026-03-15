import type { Graph, Node } from "@antv/x6";
import type { DotNetObjectReference } from "../types/dotnet-interop";
import type { EditorNodeData } from "./dag-types";
import { wouldCreateCycle } from "./cycle-detection";
import { Changeset } from "./changeset";
import { copySelection, pasteSelection } from "./clipboard-handler";

let zoomDebounceTimer: ReturnType<typeof setTimeout> | null = null;

// ── Blank-click suppression ──
// Shape.HTML nodes render inside <foreignObject> in SVG. Safari (and sometimes
// Chrome) fires BOTH node:click AND blank:click for the same physical click on
// a Shape.HTML node, because the foreignObject click propagates to the SVG
// background. Timing-based approaches fail because the blank:click can be
// delayed by hundreds of milliseconds (Blazor Server round-trip + DOM patching).
//
// Strategy: When blank:click fires, we geometrically check whether the click
// coordinates fall inside any existing node's bounding box. If they do, the
// blank:click is a ghost event from foreignObject propagation and is suppressed.
// This is deterministic and has zero timing dependencies.

/**
 * Bind editor-specific X6 graph events to .NET callbacks and changeset tracking.
 * Handles: node move, edge connect/remove, node selection, keyboard shortcuts.
 */
export function bindEditorEvents(
  graph: Graph,
  dotNetRef: DotNetObjectReference,
  changeset: Changeset
): void {
  // ── Node click → C# callback ──
  graph.on( "node:click", ( { node } ) => {
    const data = node.getData<{ stepId?: number; isLane?: boolean }>();
    if ( data?.isLane ) return;
    if ( data?.stepId != null ) {
      console.log( "[werkr-dag] node:click stepId=", data.stepId );
      graph.cleanSelection();
      graph.select( node );
      dotNetRef.invokeMethodAsync( "OnNodeSelectedCallback", data.stepId );
    }
    // Focus the graph container so keyboard shortcuts (Delete/Backspace) work
    const container = graph.container;
    if ( container && !container.getAttribute( "tabindex" ) ) {
      container.setAttribute( "tabindex", "-1" );
      container.style.outline = "none";
    }
    container?.focus();
  } );

  // ── Click on blank canvas → deselect (unless click is inside a node bbox) ──
  // Ghost blank:click events from foreignObject propagation will have coordinates
  // that fall inside the clicked node's bounding box. Genuine blank clicks will
  // have coordinates that are outside all nodes.
  graph.on( "blank:click", ( { x, y }: { e: MouseEvent; x: number; y: number } ) => {
    for ( const node of graph.getNodes() ) {
      const bbox = node.getBBox();
      if ( bbox.containsPoint( { x, y } ) ) {
        console.log( "[werkr-dag] blank:click SUPPRESSED — inside node bbox at", x, y );
        return;
      }
    }
    console.log( "[werkr-dag] blank:click → deselecting (genuine blank at", x, y, ")" );
    graph.cleanSelection();
    dotNetRef.invokeMethodAsync( "OnNodeDeselectedCallback" );
  } );

  // ── Zoom changed → C# callback (debounced 100ms) ──
  graph.on( "scale", ( { sx } ) => {
    if ( zoomDebounceTimer ) {
      clearTimeout( zoomDebounceTimer );
    }
    zoomDebounceTimer = setTimeout( () => {
      dotNetRef.invokeMethodAsync( "OnZoomChangedCallback", sx );
      zoomDebounceTimer = null;
    }, 100 );
  } );

  // ── Node moved → update changeset with new position ──
  graph.on( "node:moved", ( { node } ) => {
    const data = node.getData<{ stepId?: number; isLane?: boolean }>();
    if ( data?.isLane ) return;
    if ( data?.stepId != null ) {
      const pos = node.getPosition();
      changeset.updateStep( data.stepId, { position: { x: pos.x, y: pos.y } } );
      notifyDirty( dotNetRef, changeset );
    }
  } );

  // ── Edge connected (new connection drawn) → cycle check + changeset ──
  graph.on( "edge:connected", ( { edge, isNew } ) => {
    if ( !isNew ) return;

    const sourceCell = edge.getSourceCell();
    const targetCell = edge.getTargetCell();
    if ( !sourceCell || !targetCell ) {
      graph.removeEdge( edge.id );
      return;
    }

    const sourceData = sourceCell.getData<EditorNodeData>();
    const targetData = targetCell.getData<EditorNodeData>();
    if ( !sourceData?.stepId || !targetData?.stepId ) {
      graph.removeEdge( edge.id );
      return;
    }

    // Cycle check (exclude the just-added edge for the check since it's already in graph)
    // We temporarily remove it, check, then re-add if valid
    graph.removeEdge( edge.id );
    if ( wouldCreateCycle( graph, sourceCell.id, targetCell.id ) ) {
      dotNetRef.invokeMethodAsync( "OnCycleDetectedCallback", sourceData.stepId, targetData.stepId );
      return;
    }

    // Re-add the valid edge
    graph.addEdge( edge.toJSON() );
    changeset.addDependency( targetData.stepId, sourceData.stepId );
    notifyDirty( dotNetRef, changeset );
  } );

  // ── Edge removed → remove dependency from changeset ──
  graph.on( "edge:removed", ( { edge } ) => {
    const sourceCell = edge.getSourceCell();
    const targetCell = edge.getTargetCell();
    if ( !sourceCell || !targetCell ) return;

    const sourceData = sourceCell.getData<EditorNodeData>();
    const targetData = targetCell.getData<EditorNodeData>();
    if ( !sourceData?.stepId || !targetData?.stepId ) return;

    changeset.deleteDependency( targetData.stepId, sourceData.stepId );
    notifyDirty( dotNetRef, changeset );
  } );

  // ── Node removed → mark step deleted in changeset ──
  graph.on( "node:removed", ( { node } ) => {
    const data = node.getData<{ stepId?: number; isLane?: boolean }>();
    if ( data?.isLane ) return;
    if ( data?.stepId != null ) {
      changeset.deleteStep( data.stepId );
      notifyDirty( dotNetRef, changeset );
    }
  } );
}

/**
 * Register keyboard shortcuts scoped to the graph canvas.
 * Called after graph creation with Keyboard plugin enabled.
 */
export function bindKeyboardShortcuts(
  graph: Graph,
  dotNetRef: DotNetObjectReference,
  changeset: Changeset
): void {
  // Delete selected cells
  graph.bindKey( ["delete", "backspace"], () => {
    const selected = graph.getSelectedCells();
    if ( selected.length > 0 ) {
      // Remove edges first, then nodes to avoid stale references
      const edges = selected.filter( c => c.isEdge() );
      const nodes = selected.filter( c => c.isNode() );
      for ( const edge of edges ) graph.removeEdge( edge.id );
      for ( const node of nodes ) graph.removeNode( node.id );
    }
  } );

  // Select all nodes
  graph.bindKey( "ctrl+a", ( e ) => {
    e.preventDefault();
    const nodes = graph.getNodes().filter( n => !n.getData<{ isLane?: boolean }>()?.isLane );
    graph.select( nodes );
  } );

  // Undo
  graph.bindKey( "ctrl+z", () => {
    if ( graph.canUndo() ) graph.undo();
  } );

  // Redo
  graph.bindKey( ["ctrl+shift+z", "ctrl+y"], () => {
    if ( graph.canRedo() ) graph.redo();
  } );

  // Save
  graph.bindKey( "ctrl+s", ( e ) => {
    e.preventDefault();
    dotNetRef.invokeMethodAsync( "OnSaveRequestedCallback" );
  } );

  // Escape — deselect all + notify .NET to close config panel
  graph.bindKey( "escape", () => {
    graph.cleanSelection();
    dotNetRef.invokeMethodAsync( "OnNodeDeselectedCallback" );
  } );

  // Arrow key nudge (1px, 10px with shift)
  const nudge = ( dx: number, dy: number ) => {
    const selected = graph.getSelectedCells().filter( c => c.isNode() );
    for ( const node of selected ) {
      const pos = node.getPosition();
      node.position( pos.x + dx, pos.y + dy );
    }
  };

  graph.bindKey( "up", () => nudge( 0, -1 ) );
  graph.bindKey( "down", () => nudge( 0, 1 ) );
  graph.bindKey( "left", () => nudge( -1, 0 ) );
  graph.bindKey( "right", () => nudge( 1, 0 ) );
  graph.bindKey( "shift+up", () => nudge( 0, -10 ) );
  graph.bindKey( "shift+down", () => nudge( 0, 10 ) );
  graph.bindKey( "shift+left", () => nudge( -10, 0 ) );
  graph.bindKey( "shift+right", () => nudge( 10, 0 ) );

  // Copy selection (Ctrl+C)
  graph.bindKey( "ctrl+c", ( e ) => {
    e.preventDefault();
    copySelection( graph );
  } );

  // Paste selection (Ctrl+V)
  graph.bindKey( "ctrl+v", ( e ) => {
    e.preventDefault();
    pasteSelection( graph, changeset );
    notifyDirty( dotNetRef, changeset );
  } );

  // Duplicate selected nodes (Ctrl+D)
  graph.bindKey( "ctrl+d", ( e ) => {
    e.preventDefault();
    const selected = graph.getSelectedCells().filter(
      c => c.isNode() && !c.getData<{ isLane?: boolean }>()?.isLane
    ) as Node[];
    for ( const node of selected ) {
      const data = node.getData<EditorNodeData>();
      if ( !data ) continue;
      const pos = node.getPosition();
      const tempId = changeset.nextTempId();
      const cloneData: EditorNodeData = {
        ...data,
        stepId: tempId,
        tempId,
      };
      graph.addNode( {
        ...node.toJSON(),
        id: `step-${tempId}`,
        x: pos.x + 20,
        y: pos.y + 20,
        data: cloneData,
      } );
      changeset.addStep( tempId, data.taskId, data.order, { x: pos.x + 20, y: pos.y + 20 } );
    }
    notifyDirty( dotNetRef, changeset );
  } );
}

function notifyDirty( dotNetRef: DotNetObjectReference, changeset: Changeset ): void {
  dotNetRef.invokeMethodAsync( "OnGraphDirtyChangedCallback", changeset.getDirtyCount() );
}
