import type { Graph } from "@antv/x6";
import type { DotNetObjectReference } from "../types/dotnet-interop";
import type { DagNodeDto, DagEdgeDto, LayoutConfig } from "./dag-types";
import { createGraph } from "./create-graph";
import { bindGraphEvents } from "./graph-bindings";
import { applyDagreLayout, relayout, defaultLayoutConfig } from "./layout-engine";
import {
  applyStatusOverlay,
  clearStatusOverlay,
  updateSingleNodeStatus,
} from "./status-overlay";
import { registerWerkrNode, NODE_WIDTH, NODE_HEIGHT } from "./werkr-node";
import { werkrEdgeDefaults } from "./werkr-edge";
import { renderParallelLanes } from "./parallel-lanes";
import { exportSvg as doExportSvg, exportPng as doExportPng } from "./export-handler";
import { registerAnnotationShape } from "./annotation-node";

let graph: Graph | null = null;
let dotNetRef: DotNetObjectReference | null = null;
let currentDirection: "LR" | "TB" = "LR";

// Register custom node shapes on module load
registerWerkrNode();
registerAnnotationShape();

/**
 * Initialize the X6 graph in the given container.
 * Called from C# DagJsInterop.InitAsync().
 */
export function initGraph(
  containerId: string,
  minimapContainerId: string,
  dotNetObjRef: DotNetObjectReference
): void {
  const result = createGraph( {
    containerId,
    minimapContainerId,
    readonly: true,
  } );

  graph = result.graph;
  dotNetRef = dotNetObjRef;
  bindGraphEvents( graph, dotNetRef );
}

/**
 * Load nodes and edges into the graph, run Dagre layout, render lanes, zoom to fit.
 * Called from C# DagJsInterop.LoadGraphAsync().
 */
export function loadGraph(
  nodes: DagNodeDto[],
  edges: DagEdgeDto[],
  layoutConfig?: Partial<LayoutConfig>
): void {
  if ( !graph ) return;

  if ( layoutConfig?.rankdir ) {
    currentDirection = layoutConfig.rankdir;
  }

  // Clear existing cells
  graph.clearCells();

  // Add nodes
  for ( const dto of nodes ) {
    graph.addNode( {
      id: `step-${dto.stepId}`,
      shape: "werkr-step",
      width: NODE_WIDTH,
      height: NODE_HEIGHT,
      // Position will be set by Dagre
      x: 0,
      y: 0,
      ports: {
        items: [
          {
            id: "in",
            group: "in",
          },
          {
            id: "out",
            group: "out",
          },
        ],
        groups: {
          in: {
            position: currentDirection === "LR" ? "left" : "top",
            attrs: {
              circle: {
                r: 0,
                magnet: false,
              },
            },
          },
          out: {
            position: currentDirection === "LR" ? "right" : "bottom",
            attrs: {
              circle: {
                r: 0,
                magnet: false,
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
      },
    } );
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
  const effectiveConfig = { ...defaultLayoutConfig, ...layoutConfig };
  const nodeRanks = applyDagreLayout( graph, effectiveConfig );

  // Render parallel grouping lanes
  renderParallelLanes( graph, nodeRanks );

  // Zoom to fit
  graph.zoomToFit( { padding: 40, maxScale: 1.5 } );
}

/** Update the execution status of a single node (for SignalR incremental updates). */
export function updateNodeStatus( stepId: number, status: string ): void {
  if ( !graph ) return;
  updateSingleNodeStatus( graph, stepId, status );
}

/** Apply execution status to all nodes from a status dictionary. */
export function applyAllStatuses( statuses: Record<number, string> ): void {
  if ( !graph ) return;
  applyStatusOverlay( graph, statuses );
}

/** Clear all execution status overlays. */
export function clearStatuses(): void {
  if ( !graph ) return;
  clearStatusOverlay( graph );
}

/** Zoom to fit all content in the viewport. */
export function zoomToFit(): void {
  graph?.zoomToFit( { padding: 40, maxScale: 1.5 } );
}

/** Zoom to a specific scale level. */
export function zoomTo( scale: number ): void {
  graph?.zoomTo( scale );
}

/** Switch layout direction (LR ↔ TB) and re-layout without reloading data. */
export function setLayoutDirection( direction: "LR" | "TB" ): void {
  if ( !graph ) return;
  currentDirection = direction;

  // Update port group positions for new direction
  for ( const node of graph.getNodes() ) {
    const data = node.getData<{ isLane?: boolean }>();
    if ( data?.isLane ) continue;

    node.prop( "ports/groups/in/position", direction === "LR" ? "left" : "top" );
    node.prop( "ports/groups/out/position", direction === "LR" ? "right" : "bottom" );
  }

  // Re-layout
  const nodeRanks = relayout( graph, direction );
  renderParallelLanes( graph, nodeRanks );
  graph.zoomToFit( { padding: 40, maxScale: 1.5 } );
}

/** Destroy the graph and clean up all resources. */
export function destroyGraph(): void {
  graph?.dispose();
  graph = null;
  dotNetRef = null;
}

/** Export the graph as SVG. Returns the SVG markup string. */
export async function exportSvgAsync(): Promise<string> {
  if ( !graph ) return "";
  return doExportSvg( graph );
}

/** Export the graph as PNG (triggers file download). */
export async function exportPngAsync(): Promise<void> {
  if ( !graph ) return;
  await doExportPng( graph );
}

/** Load annotation nodes from JSON onto the read-only canvas. */
export function loadAnnotations( annotationsJson: string ): void {
  if ( !graph || !annotationsJson ) return;
  try {
    const annotations = JSON.parse( annotationsJson ) as Array<{
      id: string; text: string; x: number; y: number;
      width: number; height: number; color: string;
    }>;
    for ( const ann of annotations ) {
      graph.addNode( {
        id: `annotation-${ann.id}`,
        shape: "werkr-annotation",
        x: ann.x,
        y: ann.y,
        width: ann.width,
        height: ann.height,
        data: { id: ann.id, text: ann.text, color: ann.color, isAnnotation: true },
      } );
    }
  } catch {
    // Silently skip invalid annotation JSON
  }
}
