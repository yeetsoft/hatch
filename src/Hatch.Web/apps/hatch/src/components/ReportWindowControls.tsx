import { facetClass } from '../lib/facet';
import {
  WINDOW_PRESETS,
  parsePreset,
  presetOf,
  resizeWindow,
  shiftWindow,
  windowEndingToday,
  type ReportWindow,
  type WindowPreset,
} from '../lib/reportWindow';
import { Facet, type FacetOption } from './Facet';

const CUSTOM = 'custom';

/** A report's window controls: a step either way, back to today, and a
    length. It reports the window it would move to and writes nothing - nor does
    it decide whether that differs from the current one; the page that owns the
    URL does. `defaultPreset` is the length the report opens at, and the one the
    Length facet is unlit at. */
export function ReportWindowControls({
  defaultPreset,
  window,
  now,
  offsetMinutes,
  onMove,
}: {
  defaultPreset: WindowPreset;
  window: ReportWindow;
  now: Date;
  offsetMinutes: number;
  onMove: (next: ReportWindow) => void;
}) {
  const preset = presetOf(window);
  const key = preset ?? CUSTOM;
  // Nothing is earlier than all of it, and Later would only push the end
  // into the future.
  const atAll = preset === 'all';
  // A window of some other length (a pasted link) is shown as Custom rather
  // than as the nearest preset.
  const options: FacetOption[] = [
    ...WINDOW_PRESETS.map((p) => ({ value: p.key, text: p.label })),
    ...(preset === null ? [{ value: CUSTOM, text: 'Custom' }] : []),
  ];

  return (
    <div className="hatch-timeline-controls" role="group" aria-label="Window">
      <button
        type="button"
        className={facetClass(false)}
        disabled={atAll}
        onClick={() => onMove(shiftWindow(window, -1))}
      >
        Earlier
      </button>
      <button
        type="button"
        className={facetClass(false)}
        onClick={() => onMove(windowEndingToday(window, now, offsetMinutes))}
      >
        Today
      </button>
      <button
        type="button"
        className={facetClass(false)}
        disabled={atAll}
        onClick={() => onMove(shiftWindow(window, 1))}
      >
        Later
      </button>
      <Facet
        label="Length"
        lit={key !== defaultPreset}
        value={key}
        options={options}
        onChange={(value) => {
          const chosen = WINDOW_PRESETS.find((p) => p.key === parsePreset(value));
          if (chosen) onMove(resizeWindow(window, chosen, now));
        }}
      />
    </div>
  );
}
