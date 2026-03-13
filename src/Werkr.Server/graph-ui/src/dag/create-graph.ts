import { Graph, Selection, Snapline, MiniMap } from "@antv/x6";

export interface GraphOptions {
  containerId: string;
  minimapContainerId: string;
  readonly: boolean;
}

/** Create and configure an X6 Graph instance with all plugins enabled. */
export function createGraph( options: GraphOptions ): Graph {
  const container = document.getElementById( options.containerId );
  if ( !container ) {
    throw new Error( `DAG container '#${options.containerId}' not found.` );
  }

  const graph = new Graph( {
    container,
    autoResize: true,
    background: { color: "transparent" },
    grid: false,

    // Panning via mouse drag
    panning: {
      enabled: true,
      eventTypes: ["leftMouseDown"],
    },

    // Ctrl+scroll to zoom
    mousewheel: {
      enabled: true,
      modifiers: ["ctrl", "meta"],
      minScale: 0.3,
      maxScale: 3,
    },

    // Node interaction — read-only in Phase 4
    interacting: {
      nodeMovable: !options.readonly,
      edgeMovable: false,
      edgeLabelMovable: false,
    },
  } );

  // Selection plugin (Ctrl+click for multi-select)
  graph.use( new Selection( {
    enabled: true,
    multiple: true,
    modifiers: "ctrl",
    rubberband: false,
    showNodeSelectionBox: true,
  } ) );

  // Alignment snaplines (visual aid only)
  graph.use( new Snapline( {
    enabled: true,
  } ) );

  // Minimap plugin
  const minimapContainer = document.getElementById( options.minimapContainerId );
  if ( minimapContainer ) {
    graph.use( new MiniMap( {
      container: minimapContainer,
      width: 180,
      height: 120,
      padding: 10,
    } ) );
  }

  return graph;
}
