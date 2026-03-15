import { describe, it, expect, vi, beforeEach } from "vitest";
import { copySelection, pasteSelection } from "../../src/dag/clipboard-handler";
import { Changeset } from "../../src/dag/changeset";

// ── Helpers to build minimal Graph / Cell / Node / Edge stubs ──

function makeNode( id: string, shape: string, data: Record<string, unknown> = {} ) {
  return {
    id,
    shape,
    isNode: () => true,
    isEdge: () => false,
    getData: <T>() => data as T,
    setData: vi.fn(),
    prop: vi.fn(),
    getPosition: () => ( { x: 0, y: 0 } ),
  };
}

function makeEdge( id: string, sourceId: string, targetId: string ) {
  return {
    id,
    isNode: () => false,
    isEdge: () => true,
    getData: <T>() => ( {} as T ),
    getSourceCellId: () => sourceId,
    getTargetCellId: () => targetId,
  };
}

function makeGraph( selectedCells: unknown[] = [], pasteCells: unknown[] = [] ) {
  return {
    getSelectedCells: () => selectedCells,
    copy: vi.fn(),
    paste: vi.fn( () => pasteCells ),
    removeEdge: vi.fn(),
  } as unknown as import("@antv/x6").Graph;
}

// ── Tests ──

describe( "copySelection", () => {
  it( "excludes annotation nodes from copy", () => {
    const stepNode = makeNode( "step-1", "werkr-step", { stepId: 1, taskId: 10, order: 1 } );
    const annotationNode = makeNode( "ann-1", "werkr-annotation", {} );

    const graph = makeGraph( [ stepNode, annotationNode ] );

    copySelection( graph );

    // copy should have been called with only the step node
    expect( ( graph.copy as ReturnType<typeof vi.fn> ) ).toHaveBeenCalledOnce();
    const copied = ( graph.copy as ReturnType<typeof vi.fn> ).mock.calls[0][0];
    expect( copied ).toHaveLength( 1 );
    expect( copied[0].id ).toBe( "step-1" );
  } );

  it( "excludes lane background nodes from copy", () => {
    const stepNode = makeNode( "step-1", "werkr-step", { stepId: 1, taskId: 10, order: 1 } );
    const laneNode = makeNode( "lane-1", "rect", { isLane: true } );

    const graph = makeGraph( [ stepNode, laneNode ] );

    copySelection( graph );

    const copied = ( graph.copy as ReturnType<typeof vi.fn> ).mock.calls[0][0];
    expect( copied ).toHaveLength( 1 );
    expect( copied[0].id ).toBe( "step-1" );
  } );

  it( "does nothing when no valid cells are selected", () => {
    const annotationNode = makeNode( "ann-1", "werkr-annotation", {} );
    const graph = makeGraph( [ annotationNode ] );

    copySelection( graph );

    expect( ( graph.copy as ReturnType<typeof vi.fn> ) ).not.toHaveBeenCalled();
  } );
} );

describe( "pasteSelection", () => {
  let changeset: Changeset;
  beforeEach( () => {
    changeset = new Changeset();
  } );

  it( "assigns new temp IDs to pasted nodes", () => {
    const node1 = makeNode( "step-1", "werkr-step", { stepId: 1, taskId: 10, order: 1 } );
    const node2 = makeNode( "step-2", "werkr-step", { stepId: 2, taskId: 20, order: 2 } );

    const graph = makeGraph( [], [ node1, node2 ] );

    pasteSelection( graph, changeset );

    // Each pasted node should get a new temp ID via setData
    expect( node1.setData ).toHaveBeenCalledOnce();
    expect( node2.setData ).toHaveBeenCalledOnce();

    const data1 = node1.setData.mock.calls[0][0];
    const data2 = node2.setData.mock.calls[0][0];
    expect( data1.tempId ).toBe( -1 );
    expect( data1.stepId ).toBe( -1 );
    expect( data2.tempId ).toBe( -2 );
    expect( data2.stepId ).toBe( -2 );
  } );

  it( "removes edges that reference nodes outside the paste set", () => {
    const node1 = makeNode( "step-1", "werkr-step", { stepId: 1, taskId: 10, order: 1 } );
    // Edge from an external node (not in paste set) to a pasted node
    const externalEdge = makeEdge( "edge-ext", "step-99", "step-1" );
    // Edge between two pasted nodes — should be kept
    const node2 = makeNode( "step-2", "werkr-step", { stepId: 2, taskId: 20, order: 2 } );
    const internalEdge = makeEdge( "edge-int", "step-1", "step-2" );

    const graph = makeGraph( [], [ node1, node2, externalEdge, internalEdge ] );

    pasteSelection( graph, changeset );

    // External edge should be removed, internal should remain
    expect( ( graph.removeEdge as ReturnType<typeof vi.fn> ) ).toHaveBeenCalledOnce();
    expect( ( graph.removeEdge as ReturnType<typeof vi.fn> ) ).toHaveBeenCalledWith( "edge-ext" );
  } );

  it( "records additions in the changeset", () => {
    const node1 = makeNode( "step-1", "werkr-step", { stepId: 1, taskId: 10, order: 1 } );
    const graph = makeGraph( [], [ node1 ] );

    pasteSelection( graph, changeset );

    expect( changeset.getDirtyCount() ).toBe( 1 );
    const dto = changeset.serialize();
    expect( dto.operations ).toHaveLength( 1 );
    expect( dto.operations[0].operationType ).toBe( "Add" );
    expect( dto.operations[0].stepId ).toBe( -1 );
    expect( dto.operations[0].taskId ).toBe( 10 );
  } );

  it( "does nothing when paste returns empty", () => {
    const graph = makeGraph( [], [] );

    pasteSelection( graph, changeset );

    expect( changeset.isEmpty() ).toBe( true );
  } );
} );
