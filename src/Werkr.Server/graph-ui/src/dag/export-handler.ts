import type { Graph } from "@antv/x6";

/**
 * Export the graph as an SVG string and trigger a file download.
 * Returns the SVG markup for C# interop.
 */
export async function exportSvg( graph: Graph ): Promise<string> {
  return new Promise<string>( ( resolve ) => {
    graph.toSVG( ( svgString: string ) => {
      const blob = new Blob( [svgString], { type: "image/svg+xml;charset=utf-8" } );
      triggerDownload( blob, "workflow.svg" );
      resolve( svgString );
    }, {
      copyStyles: true,
      preserveDimensions: true,
    } );
  } );
}

/**
 * Export the graph as a PNG image and trigger a file download.
 * Falls back to SVG export if the canvas is tainted.
 */
export async function exportPng( graph: Graph ): Promise<void> {
  return new Promise<void>( ( resolve ) => {
    try {
      graph.toPNG( ( dataUri: string ) => {
        const byteString = atob( dataUri.split( "," )[1] );
        const mimeString = dataUri.split( "," )[0].split( ":" )[1].split( ";" )[0];
        const ab = new ArrayBuffer( byteString.length );
        const ia = new Uint8Array( ab );
        for ( let i = 0; i < byteString.length; i++ ) {
          ia[i] = byteString.charCodeAt( i );
        }
        const blob = new Blob( [ab], { type: mimeString } );
        triggerDownload( blob, "workflow.png" );
        resolve();
      }, {
        copyStyles: true,
        padding: 20,
      } );
    } catch {
      // Canvas tainted or export not supported — fall back to SVG
      console.warn( "PNG export failed (canvas may be tainted). Falling back to SVG export." );
      graph.toSVG( ( svgString: string ) => {
        const blob = new Blob( [svgString], { type: "image/svg+xml;charset=utf-8" } );
        triggerDownload( blob, "workflow.svg" );
        resolve();
      } );
    }
  } );
}

function triggerDownload( blob: Blob, filename: string ): void {
  const url = URL.createObjectURL( blob );
  const a = document.createElement( "a" );
  a.href = url;
  a.download = filename;
  document.body.appendChild( a );
  a.click();
  document.body.removeChild( a );
  URL.revokeObjectURL( url );
}
