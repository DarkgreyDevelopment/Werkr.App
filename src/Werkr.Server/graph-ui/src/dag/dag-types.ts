/** DTO received from C# DagJsInterop — one per workflow step. */
export interface DagNodeDto {
  stepId: number;
  stepLabel: string;
  taskName: string;
  controlStatement: string;
  order: number;
}

/** DTO received from C# DagJsInterop — one per step dependency. */
export interface DagEdgeDto {
  sourceStepId: number;
  targetStepId: number;
}

/** Internal node data stored in X6 cell.data for rendering and status tracking. */
export interface WerkrNodeData {
  stepId: number;
  stepLabel: string;
  taskName: string;
  controlStatement: string;
  order: number;
  executionStatus?: string;
}

/** Configuration for Dagre hierarchical layout. */
export interface LayoutConfig {
  rankdir: "LR" | "TB";
  nodesep: number;
  ranksep: number;
  align: "UL" | "UR" | "DL" | "DR" | undefined;
}
