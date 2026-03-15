import { Graph, Selection, Snapline, MiniMap, History, Keyboard, Dnd } from "@antv/x6";
import { wouldCreateCycle } from "./cycle-detection";

export interface GraphOptions {
  containerId: string;
  minimapContainerId: string;
  readonly: boolean;
}

export interface GraphResult {
  graph: Graph;
  dnd?: Dnd;
}

/** Create and configure an X6 Graph instance with all plugins enabled. */
export function createGraph( options: GraphOptions ): GraphResult {
  const container = document.getElementById( options.containerId );
  if ( !container ) {
    throw new Error( `DAG container '#${options.containerId}' not found.` );
  }

  const graph = new Graph( {
    container,
    autoResize: true,
    background: { color: "transparent" },
    grid: {
      visible: false,
      size: 20,
      type: "dot",
      args: {
        color: "rgba(128, 128, 128, 0.3)",
        thickness: 1,
      },
    },

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

    // Node interaction — read-only in Phase 4, editable in Phase 5
    interacting: {
      nodeMovable: !options.readonly,
      edgeMovable: false,
      edgeLabelMovable: false,
    },

    // Connection validation (editor mode only)
    connecting: options.readonly ? undefined : {
      snap: { radius: 30 },
      allowBlank: false,
      allowLoop: false,
      allowMulti: false,
      highlight: true,
      validateEdge( { edge } ): boolean {
        const sourceId = edge.getSourceCellId();
        const targetId = edge.getTargetCellId();
        if ( !sourceId || !targetId || sourceId === targetId ) return false;
        return !wouldCreateCycle( graph as Graph, sourceId, targetId );
      },
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

  // Editor-only plugins (Phase 5)
  let dnd: Dnd | undefined;
  if ( !options.readonly ) {
    graph.use( new History( { enabled: true } ) );

    graph.use( new Keyboard( {
      enabled: true,
      global: false,
    } ) );

    dnd = new Dnd( { target: graph } );
    graph.use( dnd );
  }

  return { graph, dnd };
}
