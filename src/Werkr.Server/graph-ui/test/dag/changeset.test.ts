import { describe, it, expect } from "vitest";
import { Changeset } from "../../src/dag/changeset";
import type { BatchRequestDto } from "../../src/dag/changeset";

describe( "Changeset", () => {
  // ── Temp ID generation ──

  it( "generates sequential negative temp IDs", () => {
    const cs = new Changeset();
    expect( cs.nextTempId() ).toBe( -1 );
    expect( cs.nextTempId() ).toBe( -2 );
    expect( cs.nextTempId() ).toBe( -3 );
  } );

  // ── Step operations ──

  it( "tracks a step addition", () => {
    const cs = new Changeset();
    cs.addStep( -1, 10, 1, { x: 100, y: 200 } );

    expect( cs.getDirtyCount() ).toBe( 1 );
    expect( cs.isEmpty() ).toBe( false );

    const dto = cs.serialize();
    expect( dto.operations ).toHaveLength( 1 );
    expect( dto.operations[0].operationType ).toBe( "Add" );
    expect( dto.operations[0].stepId ).toBe( -1 );
    expect( dto.operations[0].taskId ).toBe( 10 );
    expect( dto.operations[0].order ).toBe( 1 );
    expect( dto.operations[0].controlStatement ).toBe( "Default" );
    expect( dto.operations[0].dependencyMode ).toBe( "All" );
    expect( dto.operations[0].maxIterations ).toBe( 100 );
  } );

  it( "tracks a step update", () => {
    const cs = new Changeset();
    cs.updateStep( 42, { controlStatement: "Conditional", conditionExpression: "x > 0" } );

    const dto = cs.serialize();
    expect( dto.operations ).toHaveLength( 1 );
    expect( dto.operations[0].operationType ).toBe( "Update" );
    expect( dto.operations[0].stepId ).toBe( 42 );
    expect( dto.operations[0].controlStatement ).toBe( "Conditional" );
    expect( dto.operations[0].conditionExpression ).toBe( "x > 0" );
  } );

  it( "tracks a step deletion", () => {
    const cs = new Changeset();
    cs.deleteStep( 7 );

    const dto = cs.serialize();
    expect( dto.operations ).toHaveLength( 1 );
    expect( dto.operations[0].operationType ).toBe( "Delete" );
    expect( dto.operations[0].stepId ).toBe( 7 );
  } );

  // ── Merge rules ──

  it( "merges update into an existing add (add + update → add)", () => {
    const cs = new Changeset();
    cs.addStep( -1, 10, 1 );
    cs.updateStep( -1, { controlStatement: "Loop", maxIterations: 5 } );

    expect( cs.getDirtyCount() ).toBe( 1 );
    const dto = cs.serialize();
    expect( dto.operations ).toHaveLength( 1 );
    expect( dto.operations[0].operationType ).toBe( "Add" );
    expect( dto.operations[0].controlStatement ).toBe( "Loop" );
    expect( dto.operations[0].maxIterations ).toBe( 5 );
    expect( dto.operations[0].taskId ).toBe( 10 );
  } );

  it( "merges multiple updates into one update", () => {
    const cs = new Changeset();
    cs.updateStep( 5, { order: 2 } );
    cs.updateStep( 5, { controlStatement: "Fallback" } );

    expect( cs.getDirtyCount() ).toBe( 1 );
    const dto = cs.serialize();
    expect( dto.operations[0].operationType ).toBe( "Update" );
    expect( dto.operations[0].order ).toBe( 2 );
    expect( dto.operations[0].controlStatement ).toBe( "Fallback" );
  } );

  it( "cancels add + delete for newly-created step", () => {
    const cs = new Changeset();
    cs.addStep( -1, 10, 1 );
    cs.deleteStep( -1 );

    expect( cs.getDirtyCount() ).toBe( 0 );
    expect( cs.isEmpty() ).toBe( true );
    expect( cs.serialize().operations ).toHaveLength( 0 );
  } );

  it( "replaces pending update with delete for existing step", () => {
    const cs = new Changeset();
    cs.updateStep( 5, { order: 3 } );
    cs.deleteStep( 5 );

    expect( cs.getDirtyCount() ).toBe( 1 );
    const dto = cs.serialize();
    expect( dto.operations ).toHaveLength( 1 );
    expect( dto.operations[0].operationType ).toBe( "Delete" );
  } );

  // ── Dependency operations ──

  it( "tracks a dependency addition", () => {
    const cs = new Changeset();
    cs.addDependency( 2, 1 );

    expect( cs.getDirtyCount() ).toBe( 1 );
    const dto = cs.serialize();
    // Orphaned dep change emitted as Update with dependencyChanges
    expect( dto.operations ).toHaveLength( 1 );
    expect( dto.operations[0].operationType ).toBe( "Update" );
    expect( dto.operations[0].stepId ).toBe( 2 );
    expect( dto.operations[0].dependencyChanges ).toHaveLength( 1 );
    expect( dto.operations[0].dependencyChanges![0].operationType ).toBe( "Add" );
    expect( dto.operations[0].dependencyChanges![0].dependsOnStepId ).toBe( 1 );
  } );

  it( "tracks a dependency deletion", () => {
    const cs = new Changeset();
    cs.deleteDependency( 3, 1 );

    const dto = cs.serialize();
    expect( dto.operations[0].dependencyChanges![0].operationType ).toBe( "Delete" );
  } );

  it( "cancels add + delete of the same dependency", () => {
    const cs = new Changeset();
    cs.addDependency( 2, 1 );
    cs.deleteDependency( 2, 1 );

    expect( cs.getDirtyCount() ).toBe( 0 );
    expect( cs.isEmpty() ).toBe( true );
  } );

  it( "cancels delete + add of the same dependency", () => {
    const cs = new Changeset();
    cs.deleteDependency( 2, 1 );
    cs.addDependency( 2, 1 );

    expect( cs.getDirtyCount() ).toBe( 0 );
    expect( cs.isEmpty() ).toBe( true );
  } );

  it( "does not duplicate identical dependency additions", () => {
    const cs = new Changeset();
    cs.addDependency( 2, 1 );
    cs.addDependency( 2, 1 );

    expect( cs.getDirtyCount() ).toBe( 1 );
  } );

  it( "removes dependency changes when their step is add-then-deleted", () => {
    const cs = new Changeset();
    cs.addStep( -1, 10, 1 );
    cs.addDependency( -1, 5 );

    expect( cs.getDirtyCount() ).toBe( 2 );

    cs.deleteStep( -1 );

    expect( cs.getDirtyCount() ).toBe( 0 );
    expect( cs.isEmpty() ).toBe( true );
  } );

  // ── Serialization ──

  it( "attaches dependency changes to their step's operation", () => {
    const cs = new Changeset();
    cs.updateStep( 5, { order: 2 } );
    cs.addDependency( 5, 3 );

    const dto = cs.serialize();
    expect( dto.operations ).toHaveLength( 1 );
    expect( dto.operations[0].operationType ).toBe( "Update" );
    expect( dto.operations[0].stepId ).toBe( 5 );
    expect( dto.operations[0].order ).toBe( 2 );
    expect( dto.operations[0].dependencyChanges ).toHaveLength( 1 );
    expect( dto.operations[0].dependencyChanges![0].operationType ).toBe( "Add" );
  } );

  it( "omits taskId and fields on delete operations", () => {
    const cs = new Changeset();
    cs.deleteStep( 7 );

    const dto = cs.serialize();
    const op = dto.operations[0];
    expect( op.taskId ).toBeUndefined();
    expect( op.order ).toBeUndefined();
    expect( op.controlStatement ).toBeUndefined();
  } );

  // ── Clear ──

  it( "clears all tracked changes and resets temp IDs", () => {
    const cs = new Changeset();
    cs.addStep( cs.nextTempId(), 10, 1 );
    cs.addDependency( 2, 1 );

    expect( cs.getDirtyCount() ).toBe( 2 );

    cs.clear();

    expect( cs.getDirtyCount() ).toBe( 0 );
    expect( cs.isEmpty() ).toBe( true );
    expect( cs.nextTempId() ).toBe( -1 ); // reset
  } );

  // ── Snapshot round-trip ──

  it( "round-trips through toSnapshot / fromSnapshot", () => {
    const cs = new Changeset();
    const tempId = cs.nextTempId();
    cs.addStep( tempId, 10, 1, { x: 50, y: 100 } );
    cs.addDependency( tempId, 3 );

    const snapshot = cs.toSnapshot();
    const restored = Changeset.fromSnapshot( snapshot );

    expect( restored.getDirtyCount() ).toBe( cs.getDirtyCount() );
    expect( restored.isEmpty() ).toBe( false );

    const originalDto = cs.serialize();
    const restoredDto = restored.serialize();
    expect( restoredDto.operations ).toHaveLength( originalDto.operations.length );
    expect( restoredDto.operations[0].operationType ).toBe( originalDto.operations[0].operationType );
    expect( restoredDto.operations[0].stepId ).toBe( originalDto.operations[0].stepId );
  } );

  it( "preserves nextTempId across snapshot restore", () => {
    const cs = new Changeset();
    cs.nextTempId(); // -1
    cs.nextTempId(); // -2

    const snapshot = cs.toSnapshot();
    const restored = Changeset.fromSnapshot( snapshot );

    // Next temp ID should continue from where we left off
    expect( restored.nextTempId() ).toBe( -3 );
  } );

  // ── Complex multi-operation scenario ──

  it( "handles a realistic editing session", () => {
    const cs = new Changeset();

    // Add two new steps
    const step1 = cs.nextTempId(); // -1
    const step2 = cs.nextTempId(); // -2
    cs.addStep( step1, 10, 1 );
    cs.addStep( step2, 20, 2 );

    // Add dependency between them
    cs.addDependency( step2, step1 );

    // Update existing step
    cs.updateStep( 100, { controlStatement: "Conditional" } );

    // Delete an existing step
    cs.deleteStep( 200 );

    // Cancel: add then delete step2
    cs.deleteStep( step2 );

    // Should have: add step1, update step100, delete step200
    expect( cs.getDirtyCount() ).toBe( 3 );

    const dto = cs.serialize();
    expect( dto.operations ).toHaveLength( 3 );

    const types = dto.operations.map( o => o.operationType ).sort();
    expect( types ).toEqual( ["Add", "Delete", "Update"] );
  } );
} );
