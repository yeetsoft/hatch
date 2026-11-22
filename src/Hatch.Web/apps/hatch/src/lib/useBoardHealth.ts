import { useCallback } from 'react';
import { getBoardHealth } from '../api/client';
import type { BoardHealth } from '../types';
import type { HealthWindow } from './boardHealth';
import { POLL_MS } from './refresh';
import { useLoaded } from './useLoaded';

/**
 * The board health strip's one read, kept fresh with the board.
 *
 * `scopeKeys` is the tab's project scope, empty when unscoped. Changing the
 * window or the scope swaps `load`, which reads at once. The error is never
 * drawn: `useLoaded` leaves `data` as it was when a read fails, so the last
 * answer stays, and a first failure leaves `data` null, which draws nothing.
 *
 * The keys are compared joined, so a new array holding the same keys does not
 * read again.
 */
export function useBoardHealth(window: HealthWindow, scopeKeys: string[]): BoardHealth | null {
  const keysKey = scopeKeys.join(',');
  const load = useCallback(
    () => getBoardHealth({ window, project: keysKey === '' ? [] : keysKey.split(',') }),
    [window, keysKey],
  );
  const { data } = useLoaded<BoardHealth>(load, { everyMs: POLL_MS });

  return data;
}
