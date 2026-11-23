import { useId, useState } from 'react';
import { axisSentence, flowHref, hasStrip, readout } from '../lib/boardHealth';
import type { HealthWindow, Tile } from '../lib/boardHealth';
import { readStripWindow, writeStripWindow } from '../lib/boardStrip';
import { useBoardHealth } from '../lib/useBoardHealth';
import { useScope } from '../lib/useScope';
import { usePhone } from '../lib/viewport';
import type { BoardHealth } from '../types';
import { BoardHealthDialog } from './BoardHealthDialog';
import { BoardHealthRow } from './BoardHealthRow';
import { InFlightTile, MovedTile, OldestTile, RunnersTile, WaitingTile } from './BoardHealthTiles';
import { HealthWindowControl } from './HealthWindowControl';

/* The five tiles above the board, one window and one hovered moment between
   them. Two parts: the view is pure - its answer, window and marked bar come in
   as props - so it can be read as static markup; the container owns what the
   view is handed.

   Pressing any of the five tiles opens its dialog. Which one is open is the
   view's own state - the marked bar and the window it shares with the dialog
   are the container's, so a refresh redraws the dialog in place. */

export function BoardHealthStripView({
  answer,
  window: chosen,
  onWindow,
  index,
  onIndex,
  scopeKeys,
  stacked = false,
}: {
  answer: BoardHealth;
  window: HealthWindow;
  onWindow: (choice: HealthWindow) => void;
  index: number | null;
  onIndex: (index: number | null) => void;
  /** The tab's project scope, for the In flight tile's link; empty when unscoped. */
  scopeKeys: string[];
  /** The phone's open strip: the same control and tiles, in one column. */
  stacked?: boolean;
}) {
  /* Declared above the early return, so the hooks run in the same order whether or not there is a strip. */
  const [open, setOpen] = useState<{ tile: Tile; pastOnly: boolean } | null>(null);

  /* No history, nothing drawn: no placeholder, no error. */
  if (!hasStrip(answer)) return null;

  const readouts = readout(answer, index);
  const tile = { answer, index, onIndex };
  const opener = (which: Tile) => () => setOpen({ tile: which, pastOnly: false });
  const href = flowHref(answer, scopeKeys);

  return (
    <>
      <div className={`hatch-health-strip${stacked ? ' hatch-health-strip--stacked' : ''}`}>
        <div className="hatch-health-control">
          <HealthWindowControl window={chosen} onWindow={onWindow} />
          <p className="hatch-health-axis">{axisSentence(answer)}</p>
        </div>
        <MovedTile {...tile} readout={readouts.moved} onOpen={opener('moved')} />
        <InFlightTile {...tile} readout={readouts.inFlight} href={href} onOpen={opener('inFlight')} />
        <OldestTile
          {...tile}
          readout={readouts.oldest}
          onOpen={opener('oldest')}
          onOpenPast={() => setOpen({ tile: 'oldest', pastOnly: true })}
        />
        <WaitingTile {...tile} readout={readouts.waiting} onOpen={opener('waiting')} />
        <RunnersTile {...tile} readout={readouts.runners} onOpen={opener('runners')} />
      </div>
      {open && (
        <BoardHealthDialog
          tile={open.tile}
          pastOnly={open.pastOnly}
          answer={answer}
          index={index}
          onIndex={onIndex}
          window={chosen}
          onWindow={onWindow}
          onClose={() => {
            setOpen(null);
            onIndex(null);
          }}
          href={href}
        />
      )}
    </>
  );
}

/**
 * The strip on a phone: one row of the four numbers, and under it, when the row
 * has been tapped, the stacked tiles with the window control above them. Pure,
 * as the view above is - `open` comes in as a prop.
 *
 * The region the row controls is always in the page, empty while closed, so
 * `aria-controls` never names an element that is not there.
 */
export function BoardHealthPhoneView({
  answer,
  open,
  onOpen,
  controls,
  ...strip
}: {
  answer: BoardHealth;
  open: boolean;
  onOpen: () => void;
  controls: string;
  window: HealthWindow;
  onWindow: (choice: HealthWindow) => void;
  index: number | null;
  onIndex: (index: number | null) => void;
  scopeKeys: string[];
}) {
  if (!hasStrip(answer)) return null;

  return (
    <div className="hatch-health-phone">
      <BoardHealthRow answer={answer} open={open} onToggle={onOpen} controls={controls} />
      <div id={controls} className="hatch-health-open">
        {open && <BoardHealthStripView answer={answer} stacked {...strip} />}
      </div>
    </div>
  );
}

/**
 * The strip as the board page mounts it. It reads only while it is mounted, so
 * hiding it - the page unmounts it - is also what stops the reads. It follows
 * the tab's project scope and nothing in the filter bar. A read that has not
 * landed, or has failed with nothing before it, draws nothing.
 *
 * Not read until the scope has loaded: before then the keys are not known, and
 * an unscoped read would draw every project for a moment.
 */
export function BoardHealthStrip() {
  const scope = useScope();

  return scope.loaded ? <ScopedStrip scopeKeys={scope.isScoped ? scope.keys : []} /> : null;
}

function ScopedStrip({ scopeKeys }: { scopeKeys: string[] }) {
  const [chosen, setChosen] = useState<HealthWindow>(readStripWindow);
  const [index, setIndex] = useState<number | null>(null);
  /* Open or closed is this mount's alone: not stored, and a phone starts closed. */
  const [open, setOpen] = useState(false);
  const controls = useId();
  const phone = usePhone();
  const answer = useBoardHealth(chosen, scopeKeys);
  if (!answer) return null;

  const shared = {
    answer,
    window: chosen,
    onWindow: (choice: HealthWindow) => {
      setChosen(choice);
      setIndex(null);
      writeStripWindow(choice);
    },
    index,
    onIndex: setIndex,
    scopeKeys,
  };

  return phone ? (
    <BoardHealthPhoneView {...shared} open={open} onOpen={() => setOpen((was) => !was)} controls={controls} />
  ) : (
    <BoardHealthStripView {...shared} />
  );
}
