import { Link } from 'react-router-dom';
import { Badge, Button, EmptyState, Field, Modal, Text } from '@hatch/ui';
import {
  anyAboveNormal,
  anyClaimed,
  mergeTrains,
  queueCard,
  queueMarker,
  queueTally,
  queueWords,
  trainDriverWords,
} from '../lib/queue';
import { runnerStrip } from '../lib/runners';
import { ClaimBadge } from './ClaimBadge';
import { Choice } from './Choice';
import { TypeBadge } from './TypeBadge';
import { StatusPill } from './StatusPill';
import type { IssueCard, QueueEntry, Runner } from '../types';
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
  runner,
  runners,
  onChoose,
  onRefresh,
  cards,
  onTake,
  now = new Date(),
}: {
  open: boolean;
  onClose: () => void;
  status: QueueStatus;
  queue: QueueEntry[];
  error: string | null;
  runner: string | null;
  /** Null until the first answer; the strip draws nothing for it. */
  runners: Runner[] | null;
  onChoose: (runner: string | null) => void;
  onRefresh: () => Promise<void>;
  cards: readonly IssueCard[];
  onTake: (card: IssueCard) => void;
  now?: Date;
}) {
  const showMarker = anyAboveNormal(queue);
  const showClaim = anyClaimed(queue);
  const tally = queueTally(queue);
  const list = runners ?? [];
  const trains = mergeTrains(queue, list);

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
          <Text tone="muted" className="hatch-queue-reading-note">
            {runner === null ? "Reading nobody's queue - the board-wide pass." : `Reading ${runner}'s own queue.`}
          </Text>
          <Button variant="secondary" disabled={status === 'loading'} onClick={onRefresh}>
            {status === 'loading' ? 'Reading…' : 'Refresh'}
          </Button>
        </div>
      }
    >
      <Field
        label="Reading as"
        hint="Board-wide is the default. Choosing a runner re-reads its own pass."
        className="hatch-queue-reading-field"
      >
        <Choice
          value={runner ?? ''}
          options={list.map((r) => r.name)}
          placeholder="Board-wide"
          onChange={(v) => onChoose(v || null)}
        />
      </Field>

      {runners !== null && runners.length === 0 && (
        <Text tone="muted">No runner has been heard from lately.</Text>
      )}

      {runners !== null && runners.length > 0 && (
        <ul className="hatch-queue-runners" aria-label="Runners">
          {runnerStrip(runners, now).map((row) => (
            <li key={row.name} className={'hatch-queue-runner' + (row.gone ? ' hatch-queue-runner--gone' : '')}>
              <strong className="hatch-queue-runner-name">{row.name}</strong>
              {row.where && <Text tone="muted">{row.where}</Text>}
              <Badge tone={row.tone}>{row.words}</Badge>
              {row.claimKey && (
                <Link className="hatch-card-key" to={`/issues/${row.claimKey}`} onClick={onClose}>
                  {row.claimKey}
                </Link>
              )}
              {row.line && <span className="hatch-queue-runner-line">{row.line}</span>}
              {row.gone && <Text tone="muted">heard from {row.ago}</Text>}
            </li>
          ))}
        </ul>
      )}

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
                  className={`hatch-queue-row ${entry.blocked === null ? 'hatch-queue-row--clear' : 'hatch-queue-row--folded'}${entry.deadlocked ? ' hatch-queue-row--deadlocked' : ''}`}
                  disabled={!card}
                  onClick={() => {
                    onClose();
                    if (card) onTake(card);
                  }}
                >
                  <span className="hatch-queue-ordinal">{at + 1}</span>
                  {showMarker && (
                    <span
                      className={`hatch-queue-marker${marker === '!!' ? ' emergency' : marker === '-' ? ' low' : marker === '~' ? ' economy' : ''}`}
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

      {status === 'ready' && trains.length > 0 && (
        <div className="hatch-queue-trains">
          {trains.map((train) => (
            <div className="hatch-queue-train" key={train.projectKey}>
              <div className="hatch-queue-train-title">
                <span className="hatch-queue-train-project">{train.projectKey}</span>
                <Text tone="muted" className="hatch-queue-train-driver">
                  {trainDriverWords(train.driver)}
                </Text>
              </div>
              <ul className="hatch-queue-train-rows">
                {train.rows.map((row) => (
                  <li key={row.issue.key} className="hatch-queue-train-row">
                    <span className="hatch-card-key">{row.issue.key}</span>
                    {row.issue.key === train.headKey && (
                      <Text tone="muted" className="hatch-queue-train-head-word">
                        Head
                      </Text>
                    )}
                    <span className="hatch-queue-words">{queueWords(row)}</span>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>
      )}
    </Modal>
  );
}
