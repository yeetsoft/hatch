import { getBoard, getQueue } from '../api/client';
import { message } from './errors';
import type { Board, QueueEntry } from '../types';

/** The board, extended with flow-viz's own queue read landed beside it under
    one ledger. `queue`/`queueError` are optional so a plain board answer
    (flow-viz off) satisfies this shape carrying neither. */
export type FlowBoard = Board & { queue?: QueueEntry[] | null; queueError?: string | null };

/**
 * The board and the work queue, read together and landed in one answer.
 *
 * Both reads run at once with `Promise.all`. A board read that fails still
 * throws - there is nothing to draw without it. A queue read that fails is
 * caught here instead: the board still lands, `queue` is null (never the
 * last good queue) and the failure's sentence is `queueError`, for
 * `BoardPage` to show beside the board's own error line.
 *
 * `getQueue` is called with no runner, with takers - the board-wide read
 * `useQueue` defaults to, not a per-runner one; `FlowVizControl` offers no
 * runner picker. Takers lets `flowBand` tell a clear or in-line row no live
 * runner can take from one some runner can.
 */
export async function readFlowBoard(
  getBoard: () => Promise<Board>,
  getQueue: () => Promise<QueueEntry[]>,
): Promise<Board & { queue: QueueEntry[] | null; queueError: string | null }> {
  const [board, queueAnswer] = await Promise.all([
    getBoard(),
    getQueue().then(
      (queue): { queue: QueueEntry[] | null; queueError: string | null } => ({ queue, queueError: null }),
      (err: unknown): { queue: QueueEntry[] | null; queueError: string | null } => ({
        queue: null,
        queueError: message(err),
      }),
    ),
  ]);

  return { ...board, ...queueAnswer };
}

/** Stable across renders, so `useLoaded`'s effect does not re-run on every
    one of them - see `lib/useLoaded.ts`'s own doc comment on `load`. */
export const getFlowBoard = () => readFlowBoard(getBoard, () => getQueue(null, true));
