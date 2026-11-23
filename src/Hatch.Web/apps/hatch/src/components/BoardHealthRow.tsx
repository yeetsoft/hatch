import { compactReadout, readout, tileLabel } from '../lib/boardHealth';
import type { Tile } from '../lib/boardHealth';
import type { BoardHealth } from '../types';

/* The board health strip on a phone, closed: the four numbers in one row, as
   one button that opens the full tiles. It draws from the same answer as the
   tiles and shows the same figures - only the second line under each is
   shortened (see `compactReadout`). No chart: a sparkline this narrow says
   nothing, and the tiles hold them one tap away. */

/** The cells in the order the tiles are drawn, with the short name each wears
    above its number. */
const CELLS: { tile: Tile; label: string }[] = [
  { tile: 'moved', label: 'Moved' },
  { tile: 'inFlight', label: 'In flight' },
  { tile: 'oldest', label: 'Oldest' },
  { tile: 'waiting', label: 'Waiting' },
];

export function BoardHealthRow({
  answer,
  open,
  onToggle,
  controls,
}: {
  answer: BoardHealth;
  open: boolean;
  onToggle: () => void;
  /** The id of the region this button opens. */
  controls: string;
}) {
  const compact = compactReadout(answer);
  const full = readout(answer, null);

  return (
    <button
      type="button"
      className={`hatch-health-row${open ? ' open' : ''}`}
      aria-expanded={open}
      aria-controls={controls}
      onClick={onToggle}
    >
      {CELLS.map(({ tile, label }) => (
        /* The spoken name is the full tile's sentence; what is drawn is the short pair. */
        <span
          key={tile}
          className={`hatch-health-cell hatch-health-cell--${compact[tile].tone}`}
          data-tile={tile}
          aria-label={tileLabel(tile, full[tile])}
        >
          <span className="hatch-health-cell-label" aria-hidden="true">
            {label}
          </span>
          <span className="hatch-health-cell-number" data-part="value" aria-hidden="true">
            {compact[tile].value}
          </span>
          <span className="hatch-health-cell-second" data-part="second" aria-hidden="true">
            {compact[tile].second}
          </span>
        </span>
      ))}
      {/* Drawn from two borders in CSS: the row holds no SVG at all. */}
      <span className="hatch-health-chevron" aria-hidden="true" />
    </button>
  );
}
