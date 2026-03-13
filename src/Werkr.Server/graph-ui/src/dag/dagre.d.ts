declare module "dagre" {
  namespace graphlib {
    class Graph {
      setGraph( options: Record<string, unknown> ): void;
      setDefaultEdgeLabel( fn: () => Record<string, unknown> ): void;
      setNode( id: string, label: { width: number; height: number } ): void;
      setEdge( source: string, target: string ): void;
      nodes(): string[];
      node( id: string ): { x: number; y: number; width: number; height: number } | undefined;
    }
  }

  function layout( graph: graphlib.Graph ): void;
}
