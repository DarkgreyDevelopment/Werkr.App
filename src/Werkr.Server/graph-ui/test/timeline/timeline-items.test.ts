import { describe, it, expect } from "vitest";
import { mapDtoToDataItem } from "../../src/timeline/timeline-items";
import type { GanttItemDto } from "../../src/timeline/gantt-item-dto";

describe( "mapDtoToDataItem", () => {
  it( "converts a completed step DTO to a vis DataItem with Date objects", () => {
    const dto: GanttItemDto = {
      id: "5-1",
      content: "Step #1: Build",
      start: "2026-03-12T10:00:00.000Z",
      end: "2026-03-12T10:00:12.400Z",
      className: "gantt-completed",
      title: "Step #1: Build (12.4s) — Completed",
    };

    const item = mapDtoToDataItem( dto );

    expect( item.id ).toBe( "5-1" );
    expect( item.content ).toBe( "Step #1: Build" );
    expect( item.start ).toBeInstanceOf( Date );
    expect( ( item.start as Date ).toISOString() ).toBe( "2026-03-12T10:00:00.000Z" );
    expect( item.end ).toBeInstanceOf( Date );
    expect( ( item.end as Date ).toISOString() ).toBe( "2026-03-12T10:00:12.400Z" );
    expect( item.className ).toBe( "gantt-completed" );
    expect( item.title ).toBe( "Step #1: Build (12.4s) — Completed" );
    expect( item.type ).toBe( "range" );
  } );

  it( "uses current time as end when end is null (running step)", () => {
    const before = new Date();
    const dto: GanttItemDto = {
      id: "7-1",
      content: "Step #3: Deploy",
      start: "2026-03-12T10:05:00.000Z",
      end: null,
      className: "gantt-running",
      title: "Step #3: Deploy — Running",
    };

    const item = mapDtoToDataItem( dto );
    const after = new Date();

    expect( item.end ).toBeInstanceOf( Date );
    const endMs = ( item.end as Date ).getTime();
    expect( endMs ).toBeGreaterThanOrEqual( before.getTime() );
    expect( endMs ).toBeLessThanOrEqual( after.getTime() );
  } );

  it( "preserves failed status class name", () => {
    const dto: GanttItemDto = {
      id: "9-2",
      content: "Step #5: Test",
      start: "2026-03-12T10:10:00.000Z",
      end: "2026-03-12T10:10:05.000Z",
      className: "gantt-failed",
      title: "Step #5: Test (5.0s) — Failed",
    };

    const item = mapDtoToDataItem( dto );

    expect( item.className ).toBe( "gantt-failed" );
  } );

  it( "preserves skipped status class name", () => {
    const dto: GanttItemDto = {
      id: "11-1",
      content: "Step #7: Notify",
      start: "2026-03-12T10:12:00.000Z",
      end: "2026-03-12T10:12:00.000Z",
      className: "gantt-skipped",
      title: "Step #7: Notify — Skipped",
    };

    const item = mapDtoToDataItem( dto );

    expect( item.className ).toBe( "gantt-skipped" );
    expect( item.type ).toBe( "range" );
  } );
} );
