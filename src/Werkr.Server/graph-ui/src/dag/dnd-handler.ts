import type { Graph, Dnd } from "@antv/x6";
import type { DotNetObjectReference } from "../types/dotnet-interop";


/**
 * Set up drag-and-drop integration for the step palette.
 * The palette renders draggable divs in Blazor; this module wires the X6 Dnd plugin
 * so that dropping on the canvas invokes a .NET callback with the action type and position.
 */
export function setupDnd(
  graph: Graph,
  dnd: Dnd,
  dotNetRef: DotNetObjectReference
): { startDrag: ( actionType: string, event: MouseEvent ) => void } {

  function startDrag( actionType: string, event: MouseEvent ): void {
    // Create a temporary placeholder node for drag preview
    const tempNode = graph.createNode( {
      shape: "rect",
      width: 220,
      height: 72,
      attrs: {
        body: {
          fill: "var(--bs-body-bg)",
          stroke: "var(--werkr-node-stroke)",
          strokeWidth: 1,
          strokeDasharray: "4 2",
          rx: 6,
          ry: 6,
        },
        label: {
          text: actionType,
          fill: "var(--bs-secondary-color)",
          fontSize: 12,
        },
      },
      data: { actionType },
    } );

    dnd.start( tempNode, event );
  }

  // When the Dnd plugin drops a node, it adds it to the graph.
  // We intercept this to notify .NET with the action type and position.
  graph.on( "node:added", ( { node } ) => {
    const data = node.getData<{ actionType?: string }>();
    if ( data?.actionType && !data.hasOwnProperty( "stepId" ) ) {
      const pos = node.getPosition();
      console.log( "[werkr-dag] node:added (dnd drop)", data.actionType, pos.x, pos.y );
      // Remove the placeholder — .NET will call addNode with proper data
      graph.removeNode( node.id );
      dotNetRef.invokeMethodAsync( "OnNodeDroppedCallback", data.actionType, pos.x, pos.y );
    }
  } );

  return { startDrag };
}
