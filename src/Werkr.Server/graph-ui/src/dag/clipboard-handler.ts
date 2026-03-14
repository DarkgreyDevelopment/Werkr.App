import type { Graph, Cell, Node } from "@antv/x6";
import type { EditorNodeData } from "./dag-types";
import { Changeset } from "./changeset";

/**
 * Copy selected step nodes to the X6 clipboard.
 * Excludes annotation nodes and lane backgrounds.
 */
export function copySelection( graph: Graph ): void {
  const selected = graph.getSelectedCells().filter( ( cell: Cell ) => {
    if ( !cell.isNode() ) return cell.isEdge();
    const data = cell.getData<{ isLane?: boolean }>();
    if ( data?.isLane ) return false;
    // Exclude annotation nodes
    if ( cell.shape === "werkr-annotation" ) return false;
    return true;
  } );

  if ( selected.length === 0 ) return;
  graph.copy( selected );
}

/**
 * Paste nodes from the X6 clipboard with a 20px offset.
 * Assigns new temp IDs to pasted nodes, removes external dependency edges,
 * and tracks additions in the changeset.
 */
export function pasteSelection( graph: Graph, changeset: Changeset ): void {
  const pasted = graph.paste( { offset: 20 } );
  if ( !pasted || pasted.length === 0 ) return;

  const pastedNodeIds = new Set<string>();
  const pastedNodes: Node[] = [];

  for ( const cell of pasted ) {
    if ( cell.isNode() ) {
      pastedNodeIds.add( cell.id );
      pastedNodes.push( cell as Node );
    }
  }

  // Assign temp IDs to pasted nodes and track in changeset
  for ( const node of pastedNodes ) {
    const data = node.getData<EditorNodeData>();
    if ( !data ) continue;

    const tempId = changeset.nextTempId();
    const newData: EditorNodeData = {
      ...data,
      stepId: tempId,
      tempId,
    };
    node.setData( newData );
    node.prop( "id", `step-${tempId}` );

    const pos = node.getPosition();
    changeset.addStep( tempId, data.taskId, data.order, { x: pos.x, y: pos.y } );
  }

  // Remove edges that reference nodes outside the paste set
  for ( const cell of pasted ) {
    if ( !cell.isEdge() ) continue;
    const sourceId = cell.getSourceCellId();
    const targetId = cell.getTargetCellId();
    if ( !pastedNodeIds.has( sourceId ) || !pastedNodeIds.has( targetId ) ) {
      graph.removeEdge( cell.id );
    }
  }
}
