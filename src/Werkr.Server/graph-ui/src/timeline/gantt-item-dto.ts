/** DTO received from C# TimelineJsInterop, serialized as JSON. */
export interface GanttItemDto {
  id: string;
  content: string;
  start: string;
  end: string | null;
  className: string;
  title: string;
}
