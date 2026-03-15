import "vis-timeline/styles/vis-timeline-graph2d.min.css";
import { Timeline, DataSet } from "vis-timeline/standalone";
import type { DataItem } from "vis-timeline/standalone";
import type { DotNetObjectReference } from "../types/dotnet-interop";
import type { GanttItemDto } from "./gantt-item-dto";
import { mapDtoToDataItem } from "./timeline-items";
import { createTimelineOptions } from "./timeline-options";

let timeline: Timeline | null = null;
let dataSet: DataSet<DataItem> | null = null;
let growTimer: ReturnType<typeof setInterval> | null = null;
let dotNetRef: DotNetObjectReference | null = null;

function startGrowTimer(): void {
  if ( growTimer !== null ) return;
  growTimer = setInterval( () => {
    if ( !dataSet ) return;
    const running = dataSet.get( {
      filter: ( item ) => item.className === "gantt-running",
    } );
    if ( running.length === 0 ) {
      stopGrowTimer();
      return;
    }
    const now = new Date();
    dataSet.update(
      running.map( ( item ) => ( { id: item.id, end: now } ) )
    );
  }, 500 );
}

function stopGrowTimer(): void {
  if ( growTimer !== null ) {
    clearInterval( growTimer );
    growTimer = null;
  }
}

/**
 * Initialize the vis-timeline in the given container element.
 * Called from C# TimelineJsInterop.InitAsync().
 */
export function initTimeline(
  containerId: string,
  _options: Record<string, unknown>,
  dotNetObjRef: DotNetObjectReference
): void {
  const container = document.getElementById( containerId );
  if ( !container ) {
    throw new Error( `Timeline container element '#${containerId}' not found.` );
  }

  dotNetRef = dotNetObjRef;
  dataSet = new DataSet<DataItem>();
  const options = createTimelineOptions();
  timeline = new Timeline( container, dataSet, options );

  timeline.on( "select", ( properties: { items: string[] } ) => {
    if ( properties.items.length > 0 && dotNetRef ) {
      const itemId = properties.items[0];
      const stepId = parseInt( itemId.split( "-" )[0], 10 );
      if ( !isNaN( stepId ) ) {
        dotNetRef.invokeMethodAsync( "HandleItemClick", stepId );
      }
    }
  } );
}

/**
 * Load an array of items into the timeline DataSet.
 * Called from C# TimelineJsInterop.LoadItemsAsync().
 */
export function loadItems( items: GanttItemDto[] ): void {
  if ( !dataSet ) return;
  const mapped = items.map( mapDtoToDataItem );
  dataSet.clear();
  dataSet.add( mapped );
  timeline?.fit();

  const hasRunning = mapped.some( ( i ) => i.className === "gantt-running" );
  if ( hasRunning ) {
    startGrowTimer();
  }
}

/**
 * Update a single item in the DataSet (for live bar growth / status change).
 * Called from C# TimelineJsInterop.UpdateItemAsync().
 */
export function updateItem( item: GanttItemDto ): void {
  if ( !dataSet ) return;
  const mapped = mapDtoToDataItem( item );

  if ( dataSet.get( mapped.id as string ) ) {
    dataSet.update( mapped );
  } else {
    dataSet.add( mapped );
  }

  if ( item.className === "gantt-running" ) {
    startGrowTimer();
  } else {
    const running = dataSet.get( {
      filter: ( i ) => i.className === "gantt-running",
    } );
    if ( running.length === 0 ) {
      stopGrowTimer();
    }
  }
}

/**
 * Zoom to fit all items in the viewport.
 * Called from C# TimelineJsInterop.ZoomToFitAsync().
 */
export function zoomToFit(): void {
  timeline?.fit();
}

/**
 * Set the visible time window.
 * Called from C# TimelineJsInterop.SetWindowAsync().
 */
export function setWindow( start: string, end: string ): void {
  timeline?.setWindow( new Date( start ), new Date( end ) );
}

/**
 * Destroy the timeline instance and clean up all resources.
 * Called from C# TimelineJsInterop.DestroyAsync() / DisposeAsync().
 */
export function destroyTimeline(): void {
  stopGrowTimer();
  if ( timeline ) {
    timeline.destroy();
    timeline = null;
  }
  dataSet = null;
  dotNetRef = null;
}
