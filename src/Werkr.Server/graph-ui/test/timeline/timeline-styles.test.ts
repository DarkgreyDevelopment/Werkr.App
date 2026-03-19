import { describe, it, expect } from "vitest";
import { getStatusClassName } from "../../src/timeline/timeline-styles";

describe( "getStatusClassName", () => {
  it( "maps Running to gantt-running", () => {
    expect( getStatusClassName( "Running" ) ).toBe( "gantt-running" );
  } );

  it("maps Succeeded to gantt-succeeded", () => {
    expect(getStatusClassName("Succeeded")).toBe("gantt-succeeded");
  } );

  it( "maps Failed to gantt-failed", () => {
    expect( getStatusClassName( "Failed" ) ).toBe( "gantt-failed" );
  } );

  it( "maps Skipped to gantt-skipped", () => {
    expect( getStatusClassName( "Skipped" ) ).toBe( "gantt-skipped" );
  } );

  it( "maps Pending to gantt-pending", () => {
    expect( getStatusClassName( "Pending" ) ).toBe( "gantt-pending" );
  } );

  it( "is case-insensitive", () => {
    expect( getStatusClassName( "running" ) ).toBe( "gantt-running" );
    expect(getStatusClassName("SUCCEEDED")).toBe("gantt-succeeded");
    expect( getStatusClassName( "fAiLeD" ) ).toBe( "gantt-failed" );
  } );

  it( "returns gantt-pending for unknown status", () => {
    expect( getStatusClassName( "Unknown" ) ).toBe( "gantt-pending" );
    expect( getStatusClassName( "" ) ).toBe( "gantt-pending" );
  } );
} );
