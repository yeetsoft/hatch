import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { bundle, type Box } from '../lib/bundle';
import type { ChainEdge } from '../lib/chains';

interface Frame {
  boardRect: DOMRect;
  boxes: Map<string, Box>;
}

/**
 * The hovered card's dependency chains, drawn over the board.
 *
 * `lib/chains.ts` decides which edges exist and `lib/bundle.ts` decides every
 * control point; this measures the cards, hands `bundle` their boxes, and
 * places what it returns. Nothing about the curves is decided here.
 *
 * The overlay is the board's own size and sits over it, with no `viewBox`, so
 * one SVG unit is one CSS pixel and the board-relative boxes are the path
 * coordinates unchanged. The board is found by `querySelector` rather than
 * handed down as a ref: the props are the edges and nothing else.
 *
 * Re-measured whenever the board or any column scrolls, or the window or the
 * board resizes. `scroll` does not bubble, so the listener is a capturing one
 * on `document` - a window listener would see neither the board's sideways
 * scroll nor any column's own. A card scrolled out of its column's viewport is
 * dropped: an arrow ending in empty air above a column heading is worse than
 * none.
 */
export function FlowArrows({ edges }: { edges: readonly ChainEdge[] }) {
  const [frame, setFrame] = useState<Frame | null>(null);

  // The mount-once listeners below read the latest edges through this.
  const edgesRef = useRef(edges);
  edgesRef.current = edges;

  const measure = () => {
    const board = document.querySelector<HTMLElement>('.hatch-board');
    if (!board) {
      setFrame(null);
      return;
    }

    const boardRect = board.getBoundingClientRect();
    const boxes = new Map<string, Box>();
    for (const key of new Set(edgesRef.current.flatMap((e) => [e.from, e.to]))) {
      const el = document.querySelector<HTMLElement>(`[data-issue-key="${CSS.escape(key)}"]`);
      if (!el) continue;

      // HA-1560: an epic carrying stories has `data-issue-key` on its face, a
      // child of the whole card now rather than the card itself - climb back
      // up so the arrow still meets the card's full height, strip and lines
      // included.
      const card = el.closest('.hatch-card') ?? el;
      const cardRect = card.getBoundingClientRect();
      const port = card.closest('.hatch-column-cards');
      if (port) {
        const portRect = port.getBoundingClientRect();
        if (cardRect.bottom <= portRect.top || cardRect.top >= portRect.bottom) continue;
      }

      boxes.set(key, {
        left: cardRect.left - boardRect.left,
        top: cardRect.top - boardRect.top,
        width: cardRect.width,
        height: cardRect.height,
      });
    }
    setFrame({ boardRect, boxes });
  };
  const measureRef = useRef(measure);
  measureRef.current = measure;

  // The first measure, synchronous with the hover that produced the edges, so
  // the arrows are not a frame behind.
  useLayoutEffect(() => {
    measureRef.current();
  }, [edges]);

  useEffect(() => {
    let frameId = 0;
    const schedule = () => {
      cancelAnimationFrame(frameId);
      frameId = requestAnimationFrame(() => measureRef.current());
    };

    document.addEventListener('scroll', schedule, true);
    window.addEventListener('resize', schedule);
    const board = document.querySelector<HTMLElement>('.hatch-board');
    const observer = board ? new ResizeObserver(schedule) : null;
    if (board) observer?.observe(board);

    return () => {
      document.removeEventListener('scroll', schedule, true);
      window.removeEventListener('resize', schedule);
      observer?.disconnect();
      cancelAnimationFrame(frameId);
    };
  }, []);

  if (edges.length === 0 || !frame) return null;

  const { strokes, heads } = bundle(edges, frame.boxes);
  if (strokes.length === 0 && heads.length === 0) return null;

  const { left, top, width, height } = frame.boardRect;
  return (
    <svg className="hatch-flow-arrows" aria-hidden="true" style={{ left, top, width, height }}>
      {strokes.map((s, i) => (
        <path key={`s${i}`} d={s.d} className={`hatch-flow-arrow-stroke hatch-flow-arrow-${s.flow}`} />
      ))}
      {heads.map((h, i) => (
        <path key={`h${i}`} d={h.d} className={`hatch-flow-arrow-head hatch-flow-arrow-${h.flow}`} />
      ))}
    </svg>
  );
}
