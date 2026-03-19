import type { Graph } from "@antv/x6";
import type { WerkrNodeData } from "./dag-types";

// ── Control statement → CSS fill mapping ──

const controlFillMap: Record<string, string> = {
  "default": "var(--werkr-node-default)",
  "if": "var(--werkr-node-conditional)",
  "elseif": "var(--werkr-node-conditional)",
  "else": "var(--werkr-node-fallback)",
  "while": "var(--werkr-node-loop)",
  "do": "var(--werkr-node-loop)",
};

// ── Execution status → CSS fill mapping ──

const statusFillMap: Record<string, string> = {
  "Running": "var(--werkr-running)",
  "Succeeded": "var(--werkr-success)",
  "Failed": "var(--werkr-failed)",
  "Skipped": "var(--werkr-skipped)",
  "Pending": "var(--werkr-pending)",
};

// ── Execution status → icon mapping ──

const statusIconMap: Record<string, string> = {
  "Running": "●",
  "Succeeded": "✓",
  "Failed": "✕",
  "Skipped": "⊘",
  "Pending": "◷",
};

/**
 * Returns a CSS var() expression for the node border fill.
 * When executionStatus is present, it takes priority over control statement.
 */
export function getNodeFillVar(
  controlStatement: string,
  executionStatus?: string
): string {
  if ( executionStatus ) {
    return statusFillMap[executionStatus] ?? "var(--werkr-node-unknown)";
  }
  return controlFillMap[controlStatement.toLowerCase()] ?? "var(--werkr-node-unknown)";
}

/**
 * Returns the CSS var() expression for the control statement badge specifically
 * (always uses control fill, ignoring execution status).
 */
export function getControlFillVar( controlStatement: string ): string {
  return controlFillMap[controlStatement.toLowerCase()] ?? "var(--werkr-node-unknown)";
}

/** Returns a Unicode icon character for the execution status. */
export function getStatusIcon( executionStatus?: string ): string {
  if ( !executionStatus ) return "";
  return statusIconMap[executionStatus] ?? "";
}

/** Apply execution status overlay to all nodes in the graph. */
export function applyStatusOverlay(
  graph: Graph,
  statuses: Record<number, string>
): void {
  for ( const node of graph.getNodes() ) {
    const data = node.getData<WerkrNodeData>();
    if ( !data ) continue;
    const status = statuses[data.stepId];
    if ( status ) {
      node.setData( { ...data, executionStatus: status }, { silent: false } );
    }
  }
}

/** Clear execution status overlay from all nodes. */
export function clearStatusOverlay( graph: Graph ): void {
  for ( const node of graph.getNodes() ) {
    const data = node.getData<WerkrNodeData>();
    if ( !data ) continue;
    node.setData(
      { ...data, executionStatus: undefined },
      { silent: false }
    );
  }
}

/** Update a single node's execution status (for incremental SignalR updates). */
export function updateSingleNodeStatus(
  graph: Graph,
  stepId: number,
  status: string
): void {
  const node = graph.getNodes().find( n => {
    const data = n.getData<WerkrNodeData>();
    return data?.stepId === stepId;
  } );
  if ( !node ) return;
  const data = node.getData<WerkrNodeData>();
  node.setData( { ...data, executionStatus: status }, { silent: false } );
}
