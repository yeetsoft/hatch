/* mermaid, and only as much of it as rendering a diagram's source to an SVG
   string needs.

   THIS IS THE ONLY MODULE THAT MAY IMPORT `mermaid`, and it is only ever
   reached as `import('../lib/mermaid')`. That dynamic import is what makes
   Vite give mermaid a chunk of its own, so a board with no diagram on it
   never downloads one (HA-657, criterion 4). A second importer - static, or
   dynamic from somewhere else - would drag mermaid, and everything it pulls
   in (`d3`, `cytoscape`, `elkjs`, `katex`, `roughjs` and more), into whatever
   chunk it sits in. Nothing enforces this: oxlint has no rule for it, so it
   is said here. (`import type` is fine; it is erased.) */

import mermaid from 'mermaid';
import { MERMAID_TOKEN_NAMES, mermaidTheme, type MermaidTokens } from './mermaidTheme';

/**
 * Initialises mermaid with the house palette for the theme currently on
 * screen, reading the tokens off the page at the moment of the call - so the
 * caller must be at a point where `<html data-theme>` is current.
 */
export function applyMermaidTheme(): void {
  const style = getComputedStyle(document.documentElement);
  const tokens = Object.fromEntries(
    MERMAID_TOKEN_NAMES.map((name) => [name, style.getPropertyValue(name)]),
  ) as MermaidTokens;
  mermaid.initialize({ startOnLoad: false, ...mermaidTheme(tokens) });
}

let nextId = 0;

/**
 * Renders one diagram's mermaid source to an SVG string, or throws. Generates
 * its own id per call - `mermaid.render` wants one unique per call for its
 * own internal, transient DOM bookkeeping - so the caller's contract stays
 * source in, SVG out.
 */
export async function renderMermaidDiagram(source: string): Promise<string> {
  const { svg } = await mermaid.render(`mermaid-diagram-${nextId++}`, source);
  return svg;
}
