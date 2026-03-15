import { Shape } from "@antv/x6";
import type { Cell } from "@antv/x6";
import type { WerkrNodeData } from "./dag-types";
import { getNodeFillVar, getControlFillVar, getStatusIcon } from "./status-overlay";

export const NODE_WIDTH = 220;
export const NODE_HEIGHT = 72;

/**
 * Build HTML DOM for a workflow step node.
 * Uses DOM API exclusively (textContent, no innerHTML) — XSS-safe by construction.
 */
function buildNodeDom( data: WerkrNodeData ): HTMLElement {
  const fillVar = getNodeFillVar( data.controlStatement, data.executionStatus );
  const controlFill = getControlFillVar( data.controlStatement );
  const statusIcon = getStatusIcon( data.executionStatus );

  // Wrapper
  const wrapper = document.createElement( "div" );
  wrapper.className = "werkr-node";
  wrapper.style.borderLeft = `4px solid ${fillVar}`;
  wrapper.style.width = "100%";
  wrapper.style.height = "100%";
  wrapper.style.display = "flex";
  wrapper.style.flexDirection = "column";
  wrapper.style.justifyContent = "center";
  wrapper.style.padding = "6px 10px";
  wrapper.style.boxSizing = "border-box";
  wrapper.style.borderRadius = "6px";
  wrapper.style.backgroundColor = "var(--bs-body-bg)";
  wrapper.style.border = `1px solid var(--werkr-node-stroke)`;
  wrapper.style.borderLeftWidth = "4px";
  wrapper.style.borderLeftColor = fillVar;
  wrapper.style.fontFamily = "inherit";
  wrapper.style.overflow = "hidden";

  if ( data.executionStatus === "Running" ) {
    wrapper.classList.add( "werkr-pulse" );
  }

  // Top row: step label + status icon
  const topRow = document.createElement( "div" );
  topRow.style.display = "flex";
  topRow.style.justifyContent = "space-between";
  topRow.style.alignItems = "center";

  const label = document.createElement( "span" );
  label.style.fontWeight = "600";
  label.style.fontSize = "13px";
  label.style.color = "var(--bs-body-color)";
  label.style.overflow = "hidden";
  label.style.textOverflow = "ellipsis";
  label.style.whiteSpace = "nowrap";
  label.textContent = data.stepLabel;

  topRow.appendChild( label );

  if ( statusIcon ) {
    const icon = document.createElement( "span" );
    icon.style.fontSize = "14px";
    icon.style.color = fillVar;
    icon.style.marginLeft = "4px";
    icon.style.flexShrink = "0";
    icon.textContent = statusIcon;
    topRow.appendChild( icon );
  }

  wrapper.appendChild( topRow );

  // Bottom row: badge + task name
  const bottomRow = document.createElement( "div" );
  bottomRow.style.display = "flex";
  bottomRow.style.alignItems = "center";
  bottomRow.style.marginTop = "4px";
  bottomRow.style.gap = "6px";

  const badge = document.createElement( "span" );
  badge.style.fontSize = "10px";
  badge.style.padding = "1px 5px";
  badge.style.borderRadius = "3px";
  badge.style.backgroundColor = controlFill;
  badge.style.color = "var(--werkr-node-text)";
  badge.style.flexShrink = "0";
  badge.style.lineHeight = "1.4";
  badge.textContent = data.controlStatement;

  const taskName = document.createElement( "span" );
  taskName.style.fontSize = "11px";
  taskName.style.color = "var(--bs-secondary-color)";
  taskName.style.overflow = "hidden";
  taskName.style.textOverflow = "ellipsis";
  taskName.style.whiteSpace = "nowrap";
  taskName.textContent = data.taskName;

  bottomRow.appendChild( badge );
  bottomRow.appendChild( taskName );
  wrapper.appendChild( bottomRow );

  return wrapper;
}

/** Register the custom HTML node shape with X6. Call once at module load. */
export function registerWerkrNode(): void {
  Shape.HTML.register( {
    shape: "werkr-step",
    width: NODE_WIDTH,
    height: NODE_HEIGHT,
    effect: ["data"],
    html( cell: Cell ) {
      const data = cell.getData<WerkrNodeData>();
      if ( !data ) {
        const empty = document.createElement( "div" );
        empty.textContent = "—";
        return empty;
      }
      return buildNodeDom( data );
    },
  } );
}
