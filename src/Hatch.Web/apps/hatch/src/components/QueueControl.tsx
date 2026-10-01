import { useState } from 'react';
import { useQueue } from '../lib/useQueue';
import { QueueModal } from './QueueModal';
import type { IssueCard } from '../types';

/**
 * The button and its modal, and the one place they meet - `NavUtilization`'s
 * shape, for the queue instead of the battery. Owns `useQueue()` and whether
 * the modal is open, so the board mounts one element and knows nothing about
 * either piece of state.
 *
 * The read happens on open, not on mount and not on a timer: the board
 * behind this already polls every 30s, and a queue nobody has asked to see
 * is a request for nothing.
 */
export function QueueControl({ cards, onTake }: { cards: readonly IssueCard[]; onTake: (card: IssueCard) => void }) {
  const { status, queue, error, load } = useQueue();
  const [open, setOpen] = useState(false);

  return (
    <>
      <button
        type="button"
        className="hatch-queue-control"
        aria-haspopup="dialog"
        title="Open the work queue - the order the dispatcher would take the board in"
        aria-label="Open the work queue - the order the dispatcher would take the board in"
        onClick={() => {
          setOpen(true);
          void load();
        }}
      >
        <svg className="hatch-queue-glyph" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
          <rect x="3" y="5" width="18" height="2" rx="1" />
          <rect x="3" y="11" width="14" height="2" rx="1" />
          <rect x="3" y="17" width="10" height="2" rx="1" />
        </svg>
      </button>
      <QueueModal
        open={open}
        onClose={() => setOpen(false)}
        status={status}
        queue={queue}
        error={error}
        onRefresh={load}
        cards={cards}
        onTake={onTake}
      />
    </>
  );
}
