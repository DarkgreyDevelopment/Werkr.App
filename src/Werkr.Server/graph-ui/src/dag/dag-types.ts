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
  isComposite?: boolean;
  compositeType?: string;
  childWorkflowId?: number;
}

/** Configuration for Dagre hierarchical layout. */
export interface LayoutConfig {
  rankdir: "LR" | "TB";
  nodesep: number;
  ranksep: number;
  align: "UL" | "UR" | "DL" | "DR" | undefined;
}

// ── Editor-specific types (Phase 5) ──

/** Extended DTO for editor mode — includes mutable step configuration fields from the server. */
export interface EditorDagNodeDto extends DagNodeDto {
  taskId?: number;
  inputVariableName?: string | null;
  outputVariableName?: string | null;
  dependencyMode?: string;
  conditionExpression?: string | null;
  maxIterations?: number;
  isComposite?: boolean;
  compositeType?: string;
  childWorkflowId?: number;
}

/** Extended node data for editor mode — adds mutable step configuration fields. */
export interface EditorNodeData extends WerkrNodeData {
  tempId?: number;
  taskId: number;
  inputVariableName?: string | null;
  outputVariableName?: string | null;
  dependencyMode?: string;
  conditionExpression?: string | null;
  maxIterations?: number;
  actionType?: string;
  isComposite?: boolean;
  compositeType?: string;
  childWorkflowId?: number;
}
