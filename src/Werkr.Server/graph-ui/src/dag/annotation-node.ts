import { Shape } from "@antv/x6";

export const ANNOTATION_DEFAULT_WIDTH = 200;
export const ANNOTATION_DEFAULT_HEIGHT = 120;

interface AnnotationData {
  id: string;
  text: string;
  color: string;
  isAnnotation: true;
}

/**
 * Build HTML DOM for a sticky-note annotation.
 * Uses textContent exclusively (no innerHTML) — XSS-safe by construction.
 */
function buildAnnotationDom( data: AnnotationData ): HTMLElement {
  const wrapper = document.createElement( "div" );
  wrapper.className = "werkr-annotation-card";
  wrapper.style.width = "100%";
  wrapper.style.height = "100%";
  wrapper.style.backgroundColor = data.color || "#fef3cd";
  wrapper.style.opacity = "0.9";
  wrapper.style.borderRadius = "4px";
  wrapper.style.padding = "8px 10px";
  wrapper.style.boxSizing = "border-box";
  wrapper.style.fontFamily = "inherit";
  wrapper.style.fontSize = "12px";
  wrapper.style.color = "#333";
  wrapper.style.overflow = "hidden";
  wrapper.style.wordWrap = "break-word";
  wrapper.style.cursor = "default";
  wrapper.style.boxShadow = "0 1px 3px rgba(0,0,0,0.15)";

  const textEl = document.createElement( "div" );
  textEl.className = "werkr-annotation-text";
  textEl.textContent = data.text || "";
  textEl.style.whiteSpace = "pre-wrap";
  textEl.style.lineHeight = "1.4";

  wrapper.appendChild( textEl );
  return wrapper;
}

/** Register the "werkr-annotation" X6 shape. Call once at module init. */
export function registerAnnotationShape(): void {
  Shape.HTML.register( {
    shape: "werkr-annotation",
    width: ANNOTATION_DEFAULT_WIDTH,
    height: ANNOTATION_DEFAULT_HEIGHT,
    html( cell ) {
      const data = cell.getData<AnnotationData>();
      return buildAnnotationDom( data ?? { id: "", text: "", color: "#fef3cd", isAnnotation: true } );
    },
    effect: ["data"],
  } );
}
