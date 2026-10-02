import { Button, EmptyState, Modal, Text } from '@hatch/ui';
import { anyAboveNormal, anyClaimed, queueCard, queueMarker, queueTally, queueWords } from '../lib/queue';
import { ClaimBadge } from './ClaimBadge';
import { TypeBadge } from './TypeBadge';
import { StatusPill } from './StatusPill';
import type { IssueCard, QueueEntry } from '../types';
import type { QueueStatus } from '../lib/useQueue';

/**
 * The dispatcher's own pass, read when the modal opens rather than polled -
 * see `useQueue`. Rows are the server's, in the server's order: nothing here
 * sorts or groups, because the order *is* the dispatcher's decision and
 * second-guessing it would show the operator a queue that is not the one a
 * runner would actually take.
 */
export function QueueModal({
  open,
  onClose,
  status,
  queue,
  error,
  onRefresh,
  cards,
  onTake,
}: {
  open: boolean;
  onClose: () => void;
  status: QueueStatus;
  queue: QueueEntry[];
  error: string | null;
  onRefresh: () => Promise<void>;
  cards: readonly IssueCard[];
  onTake: (card: IssueCard) => void;
}) {
  const showMarker = anyAboveNormal(queue);
  const showClaim = anyClaimed(queue);
  const tally = queueTally(queue);

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="The work queue"
      footer={
        <div className="hatch-queue-foot">
          {status === 'ready' && (
            <Text tone="muted">
              {tally.total} issue{tally.total === 1 ? '' : 's'} in the pass, {tally.clear} clear
            </Text>
          )}
          <Text tone="muted" className="hatch-queue-checkout-note">
            A browser holds no checkouts, so nothing here is folded for a repository a runner does not
            have - a runner's own queue may be shorter than this.
          </Text>
          <Button variant="secondary" disabled={status === 'loading'} onClick={onRefresh}>
            {status === 'loading' ? 'Reading…' : 'Refresh'}
          </Button>
        </div>
      }
    >
      {(status === 'idle' || status === 'loading') && <Text tone="muted">Reading the queue…</Text>}

      {status === 'error' && (
        <>
          <p className="text-danger">{error}</p>
          <Button variant="secondary" onClick={onRefresh}>
            Try again
          </Button>
        </>
      )}

      {status === 'ready' && queue.length === 0 && (
        <EmptyState message="Nothing on the dispatcher's path right now." />
      )}

      {status === 'ready' && queue.length > 0 && (
        <ul className="hatch-queue-rows">
          {queue.map((entry, at) => {
            const marker = queueMarker(entry);
            const card = queueCard(entry, cards);
            return (
              <li key={entry.issue.key}>
                <button
                  type="button"
                  className={`hatch-queue-row ${entry.blocked === null ? 'hatch-queue-row--clear' : 'hatch-queue-row--folded'}`}
                  disabled={!card}
                  onClick={() => {
                    onClose();
                    if (card) onTake(card);
                  }}
                >
                  <span className="hatch-queue-ordinal">{at + 1}</span>
                  {showMarker && (
                    <span
                      className={`hatch-queue-marker${marker === '!!' ? ' emergency' : marker === '~' ? ' economy' : ''}`}
                    >
                      {marker}
                    </span>
                  )}
                  {showClaim && <ClaimBadge claim={entry.issue.claim} />}
                  <span className="hatch-card-key">{entry.issue.key}</span>
                  <TypeBadge type={entry.issue.type} />
                  <StatusPill status={entry.fromStatus} />
                  <span className="hatch-queue-words">{queueWords(entry)}</span>
                </button>
              </li>
            );
          })}
        </ul>
      )}
    </Modal>
  );
}
