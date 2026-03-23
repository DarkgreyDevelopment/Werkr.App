import type { Graph } from "@antv/x6";

/**
 * Check whether adding an edge from sourceId → targetId would create a cycle.
 * Uses DFS from targetId following existing outgoing edges.
 * If any path from targetId reaches sourceId, the new edge would form a cycle.
 * Time complexity: O(V + E).
 */
export function wouldCreateCycle(
  graph: Graph,
  sourceId: string,
  targetId: string
): boolean {
  if ( sourceId === targetId ) return true;

  const visited = new Set<string>();
  const stack = [targetId];

  while ( stack.length > 0 ) {
    const current = stack.pop()!;
    if ( current === sourceId ) return true;
    if ( visited.has( current ) ) continue;

    visited.add( current );

    // Follow outgoing edges from current node
    const outEdges = graph.getOutgoingEdges( current );
    if ( outEdges ) {
      for ( const edge of outEdges ) {
        const next = edge.getTargetCellId();
        if ( !visited.has( next ) ) {
          stack.push( next );
        }
      }
    }
  }

  return false;
}
