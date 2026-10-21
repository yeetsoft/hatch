/* What a page does with its `.hatch-docs-mermaid` placeholders: load mermaid's
   chunk, theme it from the tokens, draw each placeholder on its own, sanitise
   the SVG, and fall back to the source under one line.

   It is plain DOM, and imports only `dompurify`, the type of `./mermaid`, and
   `./mermaid` itself dynamically - no React, no router, no `@hatch/ui`, no
   logger - so a page that ships none of those can call it. The app's docs page
   and the public site both do, which is what keeps their diagrams the same.
   `mermaidDraw.test.ts` fails on any other import. */

import DOMPurify from 'dompurify';
import type * as Mermaid from './mermaid';

type MermaidModule = typeof Mermaid;

// Kept once it has arrived, the same shape as MarkdownEditor.tsx's Monaco
// loader - asked for once, kept for the next page or pass, cleared on
// rejection so a failed fetch is retryable (Chrome never retries a failed
// dynamic import in place).
let loadedMermaid: MermaidModule | null = null;
let loadingMermaid: Promise<MermaidModule> | null = null;

function loadMermaid(): Promise<MermaidModule> {
  if (loadedMermaid) return Promise.resolve(loadedMermaid);
  loadingMermaid ??= import('./mermaid').then((m) => (loadedMermaid = m));
  loadingMermaid.catch(() => (loadingMermaid = null));
  return loadingMermaid;
}

/** The one line a reader sees above a diagram's source when it could not be
    drawn - mermaid refused it, or the chunk never arrived. Created once per
    container and reused, so a second failed pass does not stack a second
    line. */
function showMermaidError(container: Element, source: HTMLElement, message: string): void {
  source.hidden = false;
  container.querySelector('svg')?.remove();
  let error = container.querySelector<HTMLParagraphElement>('.hatch-docs-mermaid-error');
  if (!error) {
    error = document.createElement('p');
    error.className = 'hatch-docs-mermaid-error';
    container.insertBefore(error, source);
  }
  error.textContent = message;
}

/** Writes a drawn diagram's sanitised SVG markup into the container, beside
    the (now hidden) source `<pre>`, replacing a previous draw's SVG rather
    than leaving two behind. */
function showMermaidSvg(container: Element, source: HTMLElement, svgMarkup: string): void {
  const wrapper = document.createElement('div');
  wrapper.innerHTML = svgMarkup;
  const svg = wrapper.firstElementChild;
  if (!svg) return;
  const existing = container.querySelector('svg');
  if (existing) existing.replaceWith(svg);
  else container.appendChild(svg);
  container.querySelector('.hatch-docs-mermaid-error')?.remove();
  source.hidden = true;
}

/**
 * Draws (and, called again after a theme switch, redraws) every
 * `.hatch-docs-mermaid` under `root`. A root with none asks for nothing, so
 * mermaid's chunk is never fetched for it. The source `<pre>` is never
 * removed, only hidden, so a theme-only pass can read it again.
 *
 * `onError` hears each failure - a diagram mermaid refused, or the chunk that
 * never arrived - for the caller to log; the reader sees the same one line
 * either way. The function returned cancels the pass: whatever has not yet
 * been drawn is left unwritten.
 */
export function drawMermaid(
  root: ParentNode,
  onError?: (what: string, error: unknown) => void,
): () => void {
  const containers = Array.from(root.querySelectorAll<HTMLElement>('.hatch-docs-mermaid'));
  if (containers.length === 0) return () => {};
  let live = true;

  const fail = (message: string) => {
    for (const container of containers) {
      const source = container.querySelector<HTMLElement>('.hatch-docs-mermaid-source');
      if (source) showMermaidError(container, source, message);
    }
  };

  loadMermaid().then(
    (mermaid) => {
      if (!live) return;
      mermaid.applyMermaidTheme();
      for (const container of containers) {
        const source = container.querySelector<HTMLElement>('.hatch-docs-mermaid-source');
        if (!source) continue;
        mermaid.renderMermaidDiagram(source.textContent ?? '').then(
          (svg) => {
            if (live) showMermaidSvg(container, source, DOMPurify.sanitize(svg));
          },
          (e: unknown) => {
            onError?.('A mermaid diagram failed to draw', e);
            if (live) showMermaidError(container, source, 'This diagram could not be drawn.');
          },
        );
      }
    },
    (e: unknown) => {
      onError?.('The mermaid chunk failed to load', e);
      if (live) fail('This diagram could not be drawn.');
    },
  );

  return () => {
    live = false;
  };
}
