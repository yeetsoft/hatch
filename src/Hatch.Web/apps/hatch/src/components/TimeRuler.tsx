import { useEffect, useState } from 'react';
import type { PointerEvent as ReactPointerEvent } from 'react';
import type { ReportWindow } from '../lib/reportWindow';
import { brushBox, brushWindow, ticks, type Scale } from '../lib/timeline';

/** A drag in progress: where it was pressed, where the pointer is now, and the
    ruler's px width when it began. */
interface Brush {
  x1: number;
  x2: number;
  width: number;
}

/**
 * The ruler above a plot, and the gesture that frames a window on it: press
 * and drag across it, release, and `onFrame` is handed the two instants. Escape,
 * or a release off the pane, cancels; a drag too short to mean anything is a
 * click and does nothing. `brushWindow` and `brushBox` are the only places those
 * rules live; this just feeds them pixels.
 *
 * Returns a fragment so the highlight can be a sibling of the ruler, a direct
 * child of the track that holds both the ruler and the plot - inside the ruler
 * it would cover only the ruler.
 */
export function TimeRuler({
  scale,
  offsetMinutes,
  locale,
  onFrame,
}: {
  scale: Scale;
  offsetMinutes: number;
  locale?: string;
  onFrame: (window: ReportWindow) => void;
}) {
  const [brush, setBrush] = useState<Brush | null>(null);
  const brushing = brush !== null;

  useEffect(() => {
    if (!brushing) return;

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setBrush(null);
    };

    window.addEventListener('keydown', onKeyDown);

    return () => window.removeEventListener('keydown', onKeyDown);
  }, [brushing]);

  const onPointerDown = (event: ReactPointerEvent<HTMLDivElement>) => {
    if (event.button !== 0) return;

    const rect = event.currentTarget.getBoundingClientRect();
    const x = event.clientX - rect.left;

    event.currentTarget.setPointerCapture(event.pointerId);
    setBrush({ x1: x, x2: x, width: rect.width });
  };

  const onPointerMove = (event: ReactPointerEvent<HTMLDivElement>) => {
    if (!brush) return;

    const x = event.clientX - event.currentTarget.getBoundingClientRect().left;

    setBrush({ ...brush, x2: x });
  };

  const onPointerUp = (event: ReactPointerEvent<HTMLDivElement>) => {
    if (!brush) return;

    /* Capture keeps the release arriving here from anywhere, so whether it
       landed on the pane is judged on where it was. */
    const pane = event.currentTarget.closest('.hatch-timeline-pane')?.getBoundingClientRect();
    const inside =
      pane !== undefined &&
      event.clientX >= pane.left &&
      event.clientX <= pane.right &&
      event.clientY >= pane.top &&
      event.clientY <= pane.bottom;
    const x = event.clientX - event.currentTarget.getBoundingClientRect().left;
    const framed = inside ? brushWindow(scale, brush.x1, x, brush.width) : null;

    setBrush(null);
    if (framed) onFrame(framed);
  };

  const box = brush ? brushBox(brush.x1, brush.x2, brush.width) : null;

  return (
    <>
      <div
        className="hatch-timeline-ruler hatch-timeline-ruler--brushable"
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerUp}
        onPointerCancel={() => setBrush(null)}
      >
        {ticks(scale, offsetMinutes, locale).map((tick) => (
          <span key={tick.x} className="hatch-timeline-tick" style={{ left: `${tick.x}%` }}>
            {tick.label}
          </span>
        ))}
      </div>

      {box && <div className="hatch-timeline-brush" aria-hidden style={{ left: `${box.left}%`, width: `${box.width}%` }} />}
    </>
  );
}
