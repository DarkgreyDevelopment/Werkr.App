/** Status-to-CSS class mapping for Gantt bar coloring. */
export function getStatusClassName( status: string ): string {
  switch ( status.toLowerCase() ) {
    case "running": return "gantt-running";
    case "succeeded": return "gantt-succeeded";
    case "failed": return "gantt-failed";
    case "skipped": return "gantt-skipped";
    case "pending": return "gantt-pending";
    default: return "gantt-pending";
  }
}
