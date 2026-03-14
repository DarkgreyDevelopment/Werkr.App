// eslint-disable-next-line @typescript-eslint/no-require-imports
import dagre from "dagre";
import type { Graph } from "@antv/x6";
import type { LayoutConfig } from "./dag-types";

export const defaultLayoutConfig: LayoutConfig = {
  rankdir: "LR",
  nodesep: 60,
  ranksep: 120,
  align: "UL",
};

/**
 * Run Dagre layout on the graph and reposition all nodes.
 * Returns a map of node-id → Dagre-assigned rank for lane calculation.
 */
export function applyDagreLayout(
  graph: Graph,
  config: Partial<LayoutConfig> = {}
): Map<string, number> {
  const mergedConfig = { ...defaultLayoutConfig, ...config };

  const nodes = graph.getNodes();
  const edges = graph.getEdges();

  // Build dagre graph
  const g = new dagre.graphlib.Graph();
  g.setGraph( {
    rankdir: mergedConfig.rankdir,
    nodesep: mergedConfig.nodesep,
    ranksep: mergedConfig.ranksep,
    align: mergedConfig.align,
  } );
  g.setDefaultEdgeLabel( () => ( {} ) );

  for ( const node of nodes ) {
    const size = node.getSize();
    g.setNode( node.id, { width: size.width, height: size.height } );
  }

  for ( const edge of edges ) {
    g.setEdge( edge.getSourceCellId(), edge.getTargetCellId() );
  }

  dagre.layout( g );

  // Apply positions from layout result
  const nodeRanks = new Map<string, number>();

  // Gather rank positions for grouping
  const rankPositions: number[] = [];
  for ( const nodeId of g.nodes() ) {
    const layoutNode = g.node( nodeId );
    if ( !layoutNode ) continue;
    const pos = mergedConfig.rankdir === "LR" ? layoutNode.x : layoutNode.y;
    if ( !rankPositions.includes( pos ) ) {
      rankPositions.push( pos );
    }
  }
  rankPositions.sort( ( a, b ) => a - b );

  for ( const node of nodes ) {
    const layoutNode = g.node( node.id );
    if ( layoutNode ) {
      // Dagre returns center position; X6 positions from top-left corner
      const size = node.getSize();
      node.position( layoutNode.x - size.width / 2, layoutNode.y - size.height / 2 );
      const rankPos = mergedConfig.rankdir === "LR" ? layoutNode.x : layoutNode.y;
      nodeRanks.set( node.id, rankPositions.indexOf( rankPos ) );
    }
  }

  return nodeRanks;
}

/** Re-layout with a new direction and return updated ranks. */
export function relayout(
  graph: Graph,
  direction: "LR" | "TB"
): Map<string, number> {
  return applyDagreLayout( graph, { rankdir: direction } );
}
