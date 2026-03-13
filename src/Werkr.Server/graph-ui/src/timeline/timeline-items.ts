import type { DataItem } from "vis-timeline/standalone";
import type { GanttItemDto } from "./gantt-item-dto";

/** Maps a C# GanttItemDto (JSON) to a vis-timeline DataItem with native Date objects. */
export function mapDtoToDataItem( dto: GanttItemDto ): DataItem {
  return {
    id: dto.id,
    content: dto.content,
    start: new Date( dto.start ),
    end: dto.end ? new Date( dto.end ) : new Date(),
    className: dto.className,
    title: dto.title,
    type: "range",
  };
}
