import type { Graph } from "@antv/x6";
import type { DotNetObjectReference } from "../types/dotnet-interop";

let zoomDebounceTimer: ReturnType<typeof setTimeout> | null = null;

/** Bind X6 graph events to .NET callbacks via DotNetObjectReference. */
export function bindGraphEvents(
  graph: Graph,
  dotNetRef: DotNetObjectReference
): void {
  // Node click → C# callback
  graph.on( "node:click", ( { node } ) => {
    const data = node.getData<{ stepId?: number; isLane?: boolean }>();
    if ( data?.isLane ) return;
    if ( data?.stepId != null ) {
      dotNetRef.invokeMethodAsync( "OnNodeClickedCallback", data.stepId );
    }
  } );

  // Selection changed → C# callback
  graph.on( "selection:changed", ( { selected } ) => {
    const stepIds: number[] = [];
    for ( const cell of selected ) {
      if ( cell.isNode() ) {
        const data = cell.getData<{ stepId?: number; isLane?: boolean }>();
        if ( data?.isLane ) continue;
        if ( data?.stepId != null ) {
          stepIds.push( data.stepId );
        }
      }
    }
    dotNetRef.invokeMethodAsync( "OnSelectionChangedCallback", stepIds );
  } );

  // Zoom changed → C# callback (debounced 100ms)
  graph.on( "scale", ( { sx } ) => {
    if ( zoomDebounceTimer ) {
      clearTimeout( zoomDebounceTimer );
    }
    zoomDebounceTimer = setTimeout( () => {
      dotNetRef.invokeMethodAsync( "OnZoomChangedCallback", sx );
      zoomDebounceTimer = null;
    }, 100 );
  } );
}
