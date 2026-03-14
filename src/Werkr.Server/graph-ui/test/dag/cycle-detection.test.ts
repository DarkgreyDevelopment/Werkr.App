import { describe, it, expect, vi } from "vitest";
import { wouldCreateCycle } from "../../src/dag/cycle-detection";
import type { Graph } from "@antv/x6";

/** Helper to build a minimal Graph mock with a configurable adjacency list. */
function mockGraph( adjacency: Record<string, string[]> ): Graph {
  return {
    getOutgoingEdges( nodeId: string ) {
      const targets = adjacency[nodeId];
      if ( !targets || targets.length === 0 ) return null;
      return targets.map( t => ( {
        getTargetCellId: () => t,
      } ) );
    },
  } as unknown as Graph;
}

describe( "wouldCreateCycle", () => {
  it( "returns false for a simple DAG with no cycle", () => {
    // A → B → C, adding D → A would not create cycle from D's perspective
    // But test: adding edge A → C in graph A→B→C (no cycle)
    const graph = mockGraph( { A: ["B"], B: ["C"], C: [] } );
    expect( wouldCreateCycle( graph, "A", "C" ) ).toBe( false );
  } );

  it( "returns true for a self-loop", () => {
    const graph = mockGraph( { A: [] } );
    expect( wouldCreateCycle( graph, "A", "A" ) ).toBe( true );
  } );

  it( "detects a simple two-node cycle", () => {
    // A → B exists, adding B → A would create cycle
    const graph = mockGraph( { A: ["B"], B: [] } );
    expect( wouldCreateCycle( graph, "B", "A" ) ).toBe( true );
  } );

  it( "detects a longer cycle through multiple hops", () => {
    // A → B → C → D exists, adding D → A would create cycle
    const graph = mockGraph( { A: ["B"], B: ["C"], C: ["D"], D: [] } );
    expect( wouldCreateCycle( graph, "D", "A" ) ).toBe( true );
  } );

  it( "returns false in a complex DAG with cross-edges but no cycle", () => {
    // Diamond: A→B, A→C, B→D, C→D — adding A→D is not a cycle
    const graph = mockGraph( { A: ["B", "C"], B: ["D"], C: ["D"], D: [] } );
    expect( wouldCreateCycle( graph, "A", "D" ) ).toBe( false );
  } );

  it( "returns false for a single isolated node", () => {
    const graph = mockGraph( { X: [] } );
    expect( wouldCreateCycle( graph, "X", "Y" ) ).toBe( false );
  } );

  it( "detects cycle in branching graph", () => {
    // A→B, A→C, B→D, C→D, D→E — adding E→A creates cycle
    const graph = mockGraph( { A: ["B", "C"], B: ["D"], C: ["D"], D: ["E"], E: [] } );
    expect( wouldCreateCycle( graph, "E", "A" ) ).toBe( true );
  } );

  it( "handles graph where getOutgoingEdges returns null", () => {
    const graph = mockGraph( {} );
    expect( wouldCreateCycle( graph, "A", "B" ) ).toBe( false );
  } );
} );
