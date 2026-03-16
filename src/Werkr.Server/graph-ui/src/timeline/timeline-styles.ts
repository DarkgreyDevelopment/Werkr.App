/** Status-to-CSS class mapping for Gantt bar coloring. */
export function getStatusClassName( status: string ): string {
  switch ( status.toLowerCase() ) {
    case "running": return "gantt-running";
    case "completed": return "gantt-completed";
    case "failed": return "gantt-failed";
    case "skipped": return "gantt-skipped";
    case "pending": return "gantt-pending";
    default: return "gantt-pending";
  }
}
