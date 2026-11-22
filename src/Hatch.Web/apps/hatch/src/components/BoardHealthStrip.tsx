import { useState } from 'react';
import { HEALTH_WINDOWS, axisSentence, flowHref, hasStrip, readout } from '../lib/boardHealth';
import type { HealthWindow } from '../lib/boardHealth';
import { readStripWindow, writeStripWindow } from '../lib/boardStrip';
import { useBoardHealth } from '../lib/useBoardHealth';
import { useScope } from '../lib/useScope';
import type { BoardHealth } from '../types';
import { InFlightTile, MovedTile, OldestTile, WaitingTile } from './BoardHealthTiles';

/* The four tiles above the board, one window and one hovered moment between
   them. Two parts: the view is pure - its answer, window and marked bar come in
   as props - so it can be read as static markup; the container owns what the
   view is handed. */

export function BoardHealthStripView({
  answer,
  window: chosen,
  onWindow,
  index,
  onIndex,
  scopeKeys,
}: {
  answer: BoardHealth;
  window: HealthWindow;
  onWindow: (choice: HealthWindow) => void;
  index: number | null;
  onIndex: (index: number | null) => void;
  /** The tab's project scope, for the In flight tile's link; empty when unscoped. */
  scopeKeys: string[];
}) {
  /* No history, nothing drawn: no placeholder, no error. */
  if (!hasStrip(answer)) return null;

  const readouts = readout(answer, index);
  const tile = { answer, index, onIndex };

  return (
    <div className="hatch-health-strip">
      <div className="hatch-health-control">
        <div className="hatch-health-windows" role="group" aria-label="Window">
          {HEALTH_WINDOWS.map((choice) => (
            <button
              key={choice}
              type="button"
              className={`hatch-health-window${choice === chosen ? ' active' : ''}`}
              aria-pressed={choice === chosen}
              onClick={() => onWindow(choice)}
            >
              {choice}
            </button>
          ))}
        </div>
        <p className="hatch-health-axis">{axisSentence(answer)}</p>
      </div>
      <MovedTile {...tile} readout={readouts.moved} />
      <InFlightTile {...tile} readout={readouts.inFlight} href={flowHref(answer, scopeKeys)} />
      <OldestTile {...tile} readout={readouts.oldest} />
      <WaitingTile {...tile} readout={readouts.waiting} />
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
  const answer = useBoardHealth(chosen, scopeKeys);
  if (!answer) return null;

  return (
    <BoardHealthStripView
      answer={answer}
      window={chosen}
      onWindow={(choice) => {
        setChosen(choice);
        setIndex(null);
        writeStripWindow(choice);
      }}
      index={index}
      onIndex={setIndex}
      scopeKeys={scopeKeys}
    />
  );
}
