import { useCallback, useState } from 'react';
import { getQueue, getRunners } from '../api/client';
import { message } from './errors';
import { isRefusedRunner } from './queue';
import type { QueueEntry, Runner } from '../types';

export type QueueStatus = 'idle' | 'loading' | 'error' | 'ready';

export interface QueueState {
  status: QueueStatus;
  queue: QueueEntry[];
  error: string | null;
  /** Whose reading is on screen - `null` is board-wide, the default. */
  runner: string | null;
  /** The live runners offered in the picker. */
  runners: Runner[];
  /** Asks Hatch for the pass again, as whichever runner is currently chosen.
      Not called on its own - the modal that owns this hook calls it when it
      opens, not on a timer. */
  load: () => Promise<void>;
  /** Chooses a runner (or `null`, for board-wide) and reads its pass in one
      step. */
  choose: (runner: string | null) => Promise<void>;
}

/**
 * The one piece of state behind the queue control and its modal.
 *
 * Unlike {@link ../lib/useUtilization.useUtilization}, this never fetches on
 * its own: there is no effect here and no poll. The board behind the modal
 * already polls every 30s, and a queue read nobody asked to see is a request
 * for nothing - so this hook only exposes `load`, which `QueueControl` calls
 * the moment the modal opens, and which the modal's own "Refresh" button
 * calls again from inside.
 */
export function useQueue(): QueueState {
  const [status, setStatus] = useState<QueueStatus>('idle');
  const [queue, setQueue] = useState<QueueEntry[]>([]);
  const [runners, setRunners] = useState<Runner[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [runner, setRunner] = useState<string | null>(null);

  const readQueue = useCallback(async (asRunner: string | null) => {
    setStatus('loading');
    try {
      setQueue(await getQueue(asRunner));
      setStatus('ready');
    } catch (err) {
      if (isRefusedRunner(err, asRunner !== null)) setRunner(null);
      setError(message(err));
      setStatus('error');
    }
  }, []);

  const load = useCallback(async () => {
    // Swallowed on its own: a stale or empty runner list must not blank what
    // the queue read below is already drawing.
    try {
      setRunners(await getRunners());
    } catch {
      // leave `runners` as it was
    }
    await readQueue(runner);
  }, [readQueue, runner]);

  const choose = useCallback(
    (next: string | null) => {
      setRunner(next);
      return readQueue(next);
    },
    [readQueue],
  );

  return { status, queue, error, runner, runners, load, choose };
}
