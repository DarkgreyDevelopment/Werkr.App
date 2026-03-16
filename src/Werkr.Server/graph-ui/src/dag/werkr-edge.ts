/** Default edge configuration for workflow DAG edges. */
export const werkrEdgeDefaults = {
  router: {
    name: "orth" as const,
    args: {
      padding: 16,
    },
  },
  connector: {
    name: "rounded" as const,
    args: {
      radius: 8,
    },
  },
  attrs: {
    line: {
      stroke: "var(--werkr-edge-color)",
      strokeWidth: 1.5,
      targetMarker: {
        name: "block",
        width: 8,
        height: 6,
      },
    },
  },
  zIndex: 0,
};
