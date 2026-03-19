/** A step mutation tracked since last save. */
export interface StepChange {
  type: "add" | "update" | "delete";
  stepId: number;
  taskId?: number;
  order?: number;
  controlStatement?: string;
  conditionExpression?: string | null;
  maxIterations?: number;
  dependencyMode?: string;
  agentConnectionIdOverride?: string | null;
  inputVariableName?: string | null;
  outputVariableName?: string | null;
  position?: { x: number; y: number };
  isComposite?: boolean;
  compositeType?: string;
  childWorkflowId?: number;
  iterationVariableName?: string | null;
  collectionVariableName?: string | null;
}

/** A dependency mutation tracked since last save. */
export interface DependencyChange {
  type: "add" | "delete";
  stepId: number;
  dependsOnStepId: number;
}

/** Serialized batch request matching C# WorkflowStepBatchRequest. */
export interface BatchRequestDto {
  operations: BatchOperationDto[];
}

export interface BatchOperationDto {
  operationType: string;
  stepId: number;
  taskId?: number | null;
  order?: number;
  controlStatement?: string;
  conditionExpression?: string | null;
  maxIterations?: number;
  dependencyMode?: string;
  agentConnectionIdOverride?: string | null;
  inputVariableName?: string | null;
  outputVariableName?: string | null;
  dependencyChanges?: DependencyBatchItemDto[] | null;
  isComposite?: boolean;
  compositeType?: string;
  childWorkflowId?: number | null;
  iterationVariableName?: string | null;
  collectionVariableName?: string | null;
}

export interface DependencyBatchItemDto {
  operationType: string;
  dependsOnStepId: number;
}

/**
 * In-memory changeset tracking all graph mutations since the last save.
 * Supports merge rules to minimize batch request size.
 */
export class Changeset {
  private _stepChanges = new Map<number, StepChange>();
  private _depChanges: DependencyChange[] = [];
  private _nextTempId = -1;

  /** Generate a new negative temp ID for a newly-created step. */
  nextTempId(): number {
    return this._nextTempId--;
  }

  /** Record a new step added to the graph. */
  addStep(
    stepId: number,
    taskId: number,
    order: number,
    position?: { x: number; y: number },
    extra?: Partial<Pick<StepChange, "isComposite" | "compositeType" | "childWorkflowId" | "iterationVariableName" | "collectionVariableName">>
  ): void {
    this._stepChanges.set( stepId, {
      type: "add",
      stepId,
      taskId,
      order,
      controlStatement: "Default",
      dependencyMode: "All",
      maxIterations: 100,
      position,
      ...( extra ?? {} ),
    } );
  }

  /** Record an update to an existing or new step. */
  updateStep( stepId: number, fields: Partial<Omit<StepChange, "type" | "stepId">> ): void {
    const existing = this._stepChanges.get( stepId );

    if ( existing?.type === "add" ) {
      // Merge update into the add operation
      Object.assign( existing, fields );
      return;
    }

    if ( existing?.type === "update" ) {
      // Merge latest field values
      Object.assign( existing, fields );
      return;
    }

    // New update for an existing step (real ID > 0)
    this._stepChanges.set( stepId, {
      type: "update",
      stepId,
      ...fields,
    } );
  }

  /** Record a step deletion. */
  deleteStep( stepId: number ): void {
    const existing = this._stepChanges.get( stepId );

    if ( existing?.type === "add" ) {
      // Add + Delete = cancel both (step never saved)
      this._stepChanges.delete( stepId );
      // Also remove any dependency changes referencing this step
      this._depChanges = this._depChanges.filter(
        d => d.stepId !== stepId && d.dependsOnStepId !== stepId
      );
      return;
    }

    // Remove any pending updates, replace with delete
    this._stepChanges.set( stepId, { type: "delete", stepId } );

    // Remove dependency changes for this step (will be cascade-deleted)
    this._depChanges = this._depChanges.filter(
      d => d.stepId !== stepId && d.dependsOnStepId !== stepId
    );
  }

  /** Record a new dependency. */
  addDependency( stepId: number, dependsOnStepId: number ): void {
    // Check for and remove a matching pending delete
    const deleteIdx = this._depChanges.findIndex(
      d => d.type === "delete" && d.stepId === stepId && d.dependsOnStepId === dependsOnStepId
    );
    if ( deleteIdx >= 0 ) {
      this._depChanges.splice( deleteIdx, 1 );
      return;
    }

    // Avoid duplicates
    const exists = this._depChanges.some(
      d => d.type === "add" && d.stepId === stepId && d.dependsOnStepId === dependsOnStepId
    );
    if ( !exists ) {
      this._depChanges.push( { type: "add", stepId, dependsOnStepId } );
    }
  }

  /** Record a dependency removal. */
  deleteDependency( stepId: number, dependsOnStepId: number ): void {
    // Check for and remove a matching pending add
    const addIdx = this._depChanges.findIndex(
      d => d.type === "add" && d.stepId === stepId && d.dependsOnStepId === dependsOnStepId
    );
    if ( addIdx >= 0 ) {
      this._depChanges.splice( addIdx, 1 );
      return;
    }

    this._depChanges.push( { type: "delete", stepId, dependsOnStepId } );
  }

  /** Number of non-cancelled pending operations. */
  getDirtyCount(): number {
    return this._stepChanges.size + this._depChanges.length;
  }

  /** Whether anything has changed since last save. */
  isEmpty(): boolean {
    return this._stepChanges.size === 0 && this._depChanges.length === 0;
  }

  /** Clear all tracked changes (after successful save). */
  clear(): void {
    this._stepChanges.clear();
    this._depChanges = [];
    this._nextTempId = -1;
  }

  /** Serialize to batch request DTO matching C# WorkflowStepBatchRequest shape. */
  serialize(): BatchRequestDto {
    const operations: BatchOperationDto[] = [];

    // Group dependency changes by stepId
    const depsByStep = new Map<number, DependencyChange[]>();
    for ( const dep of this._depChanges ) {
      const list = depsByStep.get( dep.stepId ) ?? [];
      list.push( dep );
      depsByStep.set( dep.stepId, list );
    }

    // Emit step operations with their associated dependency changes
    for ( const change of this._stepChanges.values() ) {
      const stepDeps = depsByStep.get( change.stepId );
      depsByStep.delete( change.stepId );

      const op: BatchOperationDto = {
        operationType: change.type === "add" ? "Add" : change.type === "update" ? "Update" : "Delete",
        stepId: change.stepId,
      };

      if ( change.type === "add" || change.type === "update" ) {
        if ( change.taskId != null ) op.taskId = change.taskId;
        if ( change.order != null ) op.order = change.order;
        if ( change.controlStatement != null ) op.controlStatement = change.controlStatement;
        if ( change.conditionExpression !== undefined ) op.conditionExpression = change.conditionExpression;
        if ( change.maxIterations != null ) op.maxIterations = change.maxIterations;
        if ( change.dependencyMode != null ) op.dependencyMode = change.dependencyMode;
        if ( change.agentConnectionIdOverride !== undefined ) op.agentConnectionIdOverride = change.agentConnectionIdOverride;
        if ( change.inputVariableName !== undefined ) op.inputVariableName = change.inputVariableName;
        if ( change.outputVariableName !== undefined ) op.outputVariableName = change.outputVariableName;
        if ( change.isComposite != null ) op.isComposite = change.isComposite;
        if ( change.compositeType != null ) op.compositeType = change.compositeType;
        if ( change.childWorkflowId !== undefined ) op.childWorkflowId = change.childWorkflowId;
        if ( change.iterationVariableName !== undefined ) op.iterationVariableName = change.iterationVariableName;
        if ( change.collectionVariableName !== undefined ) op.collectionVariableName = change.collectionVariableName;
      }

      if ( stepDeps?.length ) {
        op.dependencyChanges = stepDeps.map( d => ( {
          operationType: d.type === "add" ? "Add" : "Delete",
          dependsOnStepId: d.dependsOnStepId,
        } ) );
      }

      operations.push( op );
    }

    // Emit orphaned dependency changes (for steps not in _stepChanges)
    for ( const [stepId, deps] of depsByStep ) {
      operations.push( {
        operationType: "Update",
        stepId,
        dependencyChanges: deps.map( d => ( {
          operationType: d.type === "add" ? "Add" : "Delete",
          dependsOnStepId: d.dependsOnStepId,
        } ) ),
      } );
    }

    return { operations };
  }

  /** Restore changeset from a serialized snapshot (for draft restore). */
  static fromSnapshot( snapshot: { stepChanges: StepChange[]; depChanges: DependencyChange[]; nextTempId: number } ): Changeset {
    const cs = new Changeset();
    for ( const sc of snapshot.stepChanges ) {
      cs._stepChanges.set( sc.stepId, sc );
    }
    cs._depChanges = [...snapshot.depChanges];
    cs._nextTempId = snapshot.nextTempId;
    return cs;
  }

  /** Capture changeset state for draft persistence. */
  toSnapshot(): { stepChanges: StepChange[]; depChanges: DependencyChange[]; nextTempId: number } {
    return {
      stepChanges: [...this._stepChanges.values()],
      depChanges: [...this._depChanges],
      nextTempId: this._nextTempId,
    };
  }
}
