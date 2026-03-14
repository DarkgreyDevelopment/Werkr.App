import type { Graph } from "@antv/x6";

const LANE_PADDING = 16;

/** Node position info for lane calculation. */
interface NodeRect {
  id: string;
  x: number;
  y: number;
  width: number;
  height: number;
}

/**
 * Render background swim-lane rectangles for ranks with ≥ 2 parallel nodes.
 * Removes any previously rendered lanes before adding new ones.
 */
export function renderParallelLanes(
  graph: Graph,
  nodeRanks: Map<string, number>
): void {
  // Remove existing lane nodes
  const existingLanes = graph.getNodes().filter(
    n => n.getData<{ isLane?: boolean }>()?.isLane === true
  );
  for ( const lane of existingLanes ) {
    graph.removeNode( lane.id );
  }

  // Group nodes by rank
  const rankGroups = new Map<number, NodeRect[]>();

  for ( const node of graph.getNodes() ) {
    // Skip lane nodes that might still be iterating
    if ( node.getData<{ isLane?: boolean }>()?.isLane ) continue;

    const rank = nodeRanks.get( node.id );
    if ( rank == null ) continue;

    const pos = node.getPosition();
    const size = node.getSize();
    const rect: NodeRect = {
      id: node.id,
      x: pos.x,
      y: pos.y,
      width: size.width,
      height: size.height,
    };

    if ( !rankGroups.has( rank ) ) {
      rankGroups.set( rank, [] );
    }
    rankGroups.get( rank )!.push( rect );
  }

  // Create lane backgrounds for ranks with ≥ 2 nodes
  for ( const [rank, rects] of rankGroups ) {
    if ( rects.length < 2 ) continue;

    const minX = Math.min( ...rects.map( r => r.x ) );
    const minY = Math.min( ...rects.map( r => r.y ) );
    const maxX = Math.max( ...rects.map( r => r.x + r.width ) );
    const maxY = Math.max( ...rects.map( r => r.y + r.height ) );

    graph.addNode( {
      x: minX - LANE_PADDING,
      y: minY - LANE_PADDING,
      width: maxX - minX + LANE_PADDING * 2,
      height: maxY - minY + LANE_PADDING * 2,
      zIndex: -1,
      shape: "rect",
      attrs: {
        body: {
          fill: "var(--werkr-parallel-group-bg, rgba(108,117,125,0.08))",
          stroke: "none",
          rx: 8,
          ry: 8,
          pointerEvents: "none",
        },
        label: {
          text: `Level ${rank + 1}`,
          fill: "var(--bs-secondary-color, #6c757d)",
          fontSize: 10,
          fontWeight: "500",
          refX: LANE_PADDING,
          refY: 4,
          textAnchor: "start",
          textVerticalAnchor: "top",
        },
      },
      data: { isLane: true },
      // Not interactive — cannot select or move
    } );
  }
}
