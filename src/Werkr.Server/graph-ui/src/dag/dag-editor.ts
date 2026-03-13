import type { Graph, Dnd } from "@antv/x6";
import type { DotNetObjectReference } from "../types/dotnet-interop";
import type { DagEdgeDto, EditorDagNodeDto, EditorNodeData, LayoutConfig } from "./dag-types";
import { createGraph } from "./create-graph";
import { bindEditorEvents, bindKeyboardShortcuts } from "./editor-bindings";
import { setupDnd } from "./dnd-handler";
import { Changeset } from "./changeset";
import { saveDraft, loadDraft, clearDraft, hasDraft } from "./draft-storage";
import { applyDagreLayout, relayout, defaultLayoutConfig } from "./layout-engine";
import {
  applyStatusOverlay,
  clearStatusOverlay,
  updateSingleNodeStatus,
} from "./status-overlay";
import { registerWerkrNode, NODE_WIDTH, NODE_HEIGHT } from "./werkr-node";
import { werkrEdgeDefaults } from "./werkr-edge";
import { renderParallelLanes } from "./parallel-lanes";

let graph: Graph | null = null;
let dndPlugin: Dnd | null = null;
let dotNetRef: DotNetObjectReference | null = null;
let changeset = new Changeset();
let currentDirection: "LR" | "TB" = "LR";
let draftTimer: ReturnType<typeof setInterval> | null = null;
let currentUserId: string = "";
let currentWorkflowId: number = 0;
let dndHandler: ReturnType<typeof setupDnd> | null = null;

// Register custom node shape on module load
registerWerkrNode();

/**
 * Initialize the X6 graph in editor mode.
 * Called from C# DagEditorJsInterop.InitEditorAsync().
 */
export function initEditor(
  containerId: string,
  minimapContainerId: string,
  dotNetObjRef: DotNetObjectReference,
  userId: string,
  workflowId: number
): void {
  const result = createGraph( {
    containerId,
    minimapContainerId,
    readonly: false,
  } );

  graph = result.graph;
  dndPlugin = result.dnd ?? null;
  dotNetRef = dotNetObjRef;
  currentUserId = userId;
  currentWorkflowId = workflowId;
  changeset = new Changeset();

  bindEditorEvents( graph, dotNetRef, changeset );
  bindKeyboardShortcuts( graph, dotNetRef, changeset );

  if ( dndPlugin ) {
    dndHandler = setupDnd( graph, dndPlugin, dotNetRef );
  }

  // Start auto-save draft timer (30s)
  draftTimer = setInterval( () => {
    if ( !changeset.isEmpty() ) {
      saveDraft( currentUserId, currentWorkflowId, changeset );
    }
  }, 30_000 );
}

/**
 * Load nodes and edges into the editor graph.
 * Called from C# DagEditorJsInterop.LoadGraphAsync().
 */
export function loadGraph(
  nodes: EditorDagNodeDto[],
  edges: DagEdgeDto[],
  layoutConfig?: Partial<LayoutConfig>
): void {
  if ( !graph ) return;

  if ( layoutConfig?.rankdir ) {
    currentDirection = layoutConfig.rankdir;
  }

  graph.clearCells();

  // Add nodes with editable ports (magnet: true)
  for ( const dto of nodes ) {
    const node = graph.addNode( {
      id: `step-${dto.stepId}`,
      shape: "werkr-step",
      width: NODE_WIDTH,
      height: NODE_HEIGHT,
      x: 0,
      y: 0,
      ports: {
        items: [
          { id: "in", group: "in" },
          { id: "out", group: "out" },
        ],
        groups: {
          in: {
            position: currentDirection === "LR" ? "left" : "top",
            attrs: {
              circle: {
                r: 5,
                magnet: true,
                stroke: "var(--werkr-edge-color)",
                strokeWidth: 1,
                fill: "var(--bs-body-bg)",
              },
            },
          },
          out: {
            position: currentDirection === "LR" ? "right" : "bottom",
            attrs: {
              circle: {
                r: 5,
                magnet: true,
                stroke: "var(--werkr-edge-color)",
                strokeWidth: 1,
                fill: "var(--bs-body-bg)",
              },
            },
          },
        },
      },
      data: {
        stepId: dto.stepId,
        stepLabel: dto.stepLabel,
        taskName: dto.taskName,
        controlStatement: dto.controlStatement,
        order: dto.order,
        taskId: dto.taskId ?? 0,
        inputVariableName: dto.inputVariableName,
        outputVariableName: dto.outputVariableName,
        dependencyMode: dto.dependencyMode,
        conditionExpression: dto.conditionExpression,
        maxIterations: dto.maxIterations,
      } satisfies EditorNodeData as unknown as EditorNodeData,
    } );

    // Ensure magnets are active
    node.setPortProp( "in", "attrs/circle/magnet", true );
    node.setPortProp( "out", "attrs/circle/magnet", true );
  }

  // Add edges
  for ( const dto of edges ) {
    graph.addEdge( {
      source: { cell: `step-${dto.sourceStepId}`, port: "out" },
      target: { cell: `step-${dto.targetStepId}`, port: "in" },
      ...werkrEdgeDefaults,
    } );
  }

  // Run Dagre layout
  const effectiveConfig = { ...defaultLayoutConfig, ...( layoutConfig ?? {} ) };
  const nodeRanks = applyDagreLayout( graph, effectiveConfig );
  renderParallelLanes( graph, nodeRanks );
  graph.zoomToFit( { padding: 40, maxScale: 1.5 } );
}

/**
 * Add a new step node to the graph.
 * Called from C# after palette drop or config panel task assignment.
 */
export function addNode(
  stepId: number,
  data: EditorNodeData,
  x: number,
  y: number
): void {
  if ( !graph ) return;

  const node = graph.addNode( {
    id: `step-${stepId}`,
    shape: "werkr-step",
    width: NODE_WIDTH,
    height: NODE_HEIGHT,
    x,
    y,
    ports: {
      items: [
        { id: "in", group: "in" },
        { id: "out", group: "out" },
      ],
      groups: {
        in: {
          position: currentDirection === "LR" ? "left" : "top",
          attrs: {
            circle: {
              r: 5,
              magnet: true,
              stroke: "var(--werkr-edge-color)",
              strokeWidth: 1,
              fill: "var(--bs-body-bg)",
            },
          },
        },
        out: {
          position: currentDirection === "LR" ? "right" : "bottom",
          attrs: {
            circle: {
              r: 5,
              magnet: true,
              stroke: "var(--werkr-edge-color)",
              strokeWidth: 1,
              fill: "var(--bs-body-bg)",
            },
          },
        },
      },
    },
    data,
  } );

  node.setPortProp( "in", "attrs/circle/magnet", true );
  node.setPortProp( "out", "attrs/circle/magnet", true );

  changeset.addStep( stepId, data.taskId, data.order, { x, y } );
  dotNetRef?.invokeMethodAsync( "OnGraphDirtyChangedCallback", changeset.getDirtyCount() );
}

/** Remove nodes by their step IDs. */
export function removeNodes( stepIds: number[] ): void {
  if ( !graph ) return;
  for ( const id of stepIds ) {
    const node = graph.getCellById( `step-${id}` );
    if ( node ) graph.removeNode( node.id );
  }
}

/** Update node data (e.g. after config panel changes). */
export function updateNodeData( stepId: number, fields: Partial<EditorNodeData> ): void {
  if ( !graph ) return;
  const node = graph.getCellById( `step-${stepId}` );
  if ( node ) {
    const existing = node.getData<EditorNodeData>();
    node.setData( { ...existing, ...fields } );
  }
}

/** Undo last graph operation. */
export function undo(): void {
  if ( graph?.canUndo() ) graph.undo();
}

/** Redo last undone operation. */
export function redo(): void {
  if ( graph?.canRedo() ) graph.redo();
}

/** Get serialized changeset as JSON string. */
export function getChangeset(): string {
  return JSON.stringify( changeset.serialize() );
}

/** Clear changeset and update temp ID sequence with new base from server ID mappings. */
export function clearChangeset(): void {
  changeset.clear();
  dotNetRef?.invokeMethodAsync( "OnGraphDirtyChangedCallback", 0 );
}

/** Zoom to fit all content. */
export function zoomToFit(): void {
  graph?.zoomToFit( { padding: 40, maxScale: 1.5 } );
}

/** Zoom to a specific scale. */
export function zoomTo( scale: number ): void {
  graph?.zoomTo( scale );
}

/** Switch layout direction (LR ↔ TB) and re-layout. */
export function setLayoutDirection( direction: "LR" | "TB" ): void {
  if ( !graph ) return;
  currentDirection = direction;

  for ( const node of graph.getNodes() ) {
    const data = node.getData<{ isLane?: boolean }>();
    if ( data?.isLane ) continue;
    node.prop( "ports/groups/in/position", direction === "LR" ? "left" : "top" );
    node.prop( "ports/groups/out/position", direction === "LR" ? "right" : "bottom" );
  }

  const nodeRanks = relayout( graph, direction );
  renderParallelLanes( graph, nodeRanks );
  graph.zoomToFit( { padding: 40, maxScale: 1.5 } );
}

/** Re-run Dagre auto-layout. */
export function autoLayout(): void {
  if ( !graph ) return;
  const nodeRanks = relayout( graph, currentDirection );
  renderParallelLanes( graph, nodeRanks );
  graph.zoomToFit( { padding: 40, maxScale: 1.5 } );
}

/** Start a palette drag from JS (called from Blazor interop). */
export function startPaletteDrag( actionType: string, clientX: number, clientY: number ): void {
  const syntheticEvent = new MouseEvent( 'mousedown', { clientX, clientY, bubbles: true } );
  dndHandler?.startDrag( actionType, syntheticEvent );
}

/** Apply execution statuses to nodes. */
export function applyAllStatuses( statuses: Record<number, string> ): void {
  if ( !graph ) return;
  applyStatusOverlay( graph, statuses );
}

/** Update a single node's execution status. */
export function updateNodeStatus( stepId: number, status: string ): void {
  if ( !graph ) return;
  updateSingleNodeStatus( graph, stepId, status );
}

/** Clear all execution status overlays. */
export function clearStatuses(): void {
  if ( !graph ) return;
  clearStatusOverlay( graph );
}

/** Check if an unsaved draft exists. */
export function checkDraft( userId: string, workflowId: number ): boolean {
  return hasDraft( userId, workflowId );
}

/** Restore draft changeset from localStorage. */
export function restoreDraft( userId: string, workflowId: number ): string | null {
  const draft = loadDraft( userId, workflowId );
  if ( !draft ) return null;
  changeset = draft;
  dotNetRef?.invokeMethodAsync( "OnGraphDirtyChangedCallback", changeset.getDirtyCount() );
  return JSON.stringify( changeset.serialize() );
}

/** Clear the draft from localStorage. */
export function dismissDraft( userId: string, workflowId: number ): void {
  clearDraft( userId, workflowId );
}

/** Destroy the editor and clean up all resources. */
export function destroyEditor(): void {
  if ( draftTimer ) {
    clearInterval( draftTimer );
    draftTimer = null;
  }

  // Save final draft if dirty
  if ( !changeset.isEmpty() && currentUserId && currentWorkflowId ) {
    saveDraft( currentUserId, currentWorkflowId, changeset );
  }

  graph?.dispose();
  graph = null;
  dndPlugin = null;
  dotNetRef = null;
  dndHandler = null;
  changeset = new Changeset();
}
