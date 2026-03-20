/**
 * Browser polyfills for cross-browser compatibility.
 *
 * requestIdleCallback / cancelIdleCallback — not implemented in Safari.
 * Used by AntV X6's rendering scheduler (queueJob.js) for deferred work.
 */
if (typeof window !== "undefined" && !("requestIdleCallback" in window)) {
  (window as any).requestIdleCallback = (
    cb: (deadline: IdleDeadline) => void,
    options?: IdleRequestOptions,
  ): number => {
    const start = Date.now();
    return window.setTimeout(() => {
      cb({
        didTimeout: false,
        timeRemaining: () => Math.max(0, 50 - (Date.now() - start)),
      } as IdleDeadline);
    }, 1);
  };

  (window as any).cancelIdleCallback = (id: number): void => {
    window.clearTimeout(id);
  };
}
