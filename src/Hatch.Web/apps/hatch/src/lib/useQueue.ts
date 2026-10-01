import { useCallback, useState } from 'react';
import { getQueue } from '../api/client';
import { message } from './errors';
import type { QueueEntry } from '../types';

export type QueueStatus = 'idle' | 'loading' | 'error' | 'ready';

export interface QueueState {
  status: QueueStatus;
  queue: QueueEntry[];
  error: string | null;
  /** Asks Hatch for the pass again. Not called on its own - the modal that
      owns this hook calls it when it opens, not on a timer. */
  load: () => Promise<void>;
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
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setStatus('loading');
    try {
      setQueue(await getQueue());
      setStatus('ready');
    } catch (err) {
      setError(message(err));
      setStatus('error');
    }
  }, []);

  return { status, queue, error, load };
}
