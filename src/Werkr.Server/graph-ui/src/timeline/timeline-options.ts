import type { TimelineOptions } from "vis-timeline/standalone";

/** Creates the default vis-timeline options for the Gantt view. */
export function createTimelineOptions(): TimelineOptions {
  return {
    orientation: { axis: "top", item: "top" },
    align: "left",
    zoomMin: 1000,
    zoomMax: 1000 * 60 * 60 * 24,
    horizontalScroll: true,
    zoomKey: "ctrlKey",
    stack: true,
    snap: null,
    maxMinorCharacters: 6,
    margin: { item: { horizontal: 2, vertical: 4 } },
    editable: false,
    selectable: true,
  };
}
