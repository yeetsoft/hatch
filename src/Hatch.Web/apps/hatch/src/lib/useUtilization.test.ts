import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

/* No DOM test environment exists, so `react` is stubbed just far enough to run
   the hook as a function: state is inert, a callback is itself, and an effect
   runs at once with its cleanup kept. The document is a stand-in whose
   visibility a test sets. */
let cleanups: Array<() => void> = [];
vi.mock('react', () => ({
  useState: (initial: unknown) => [initial, vi.fn()],
  useCallback: (fn: unknown) => fn,
  useEffect: (fn: () => void | (() => void)) => {
    const cleanup = fn();
    if (cleanup) cleanups.push(cleanup);
  },
}));

vi.mock('../api/client', () => ({
  getUtilization: vi.fn(),
}));

import { getUtilization } from '../api/client';
import { POLL_MS, useUtilization } from './useUtilization';

function stubDocument(state: string) {
  const listeners = new Set<() => void>();
  const doc = {
    visibilityState: state,
    addEventListener: (_t: string, l: () => void) => void listeners.add(l),
    removeEventListener: (_t: string, l: () => void) => void listeners.delete(l),
  };
  vi.stubGlobal('document', doc);
  return {
    set(next: string) {
      doc.visibilityState = next;
      listeners.forEach((l) => l());
    },
  };
}

const HOUR = 60 * 60 * 1000;

describe('useUtilization', () => {
  it('polls every thirty seconds', () => {
    expect(POLL_MS).toBe(30000);
  });

  beforeEach(() => {
    vi.useFakeTimers();
    cleanups = [];
    vi.mocked(getUtilization).mockReset();
    vi.mocked(getUtilization).mockResolvedValue({ windows: [] } as never);
  });
  afterEach(() => {
    cleanups.forEach((c) => c());
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('sends nothing while hidden, however long it stays hidden', () => {
    stubDocument('hidden');
    useUtilization();
    vi.advanceTimersByTime(HOUR);
    expect(getUtilization).not.toHaveBeenCalled();
  });

  it('reads once on return, then keeps its interval', () => {
    const d = stubDocument('hidden');
    useUtilization();
    vi.advanceTimersByTime(HOUR);
    d.set('visible');
    expect(getUtilization).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(POLL_MS - 1);
    expect(getUtilization).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(1);
    expect(getUtilization).toHaveBeenCalledTimes(2);
  });

  it('stops asking when hidden again', () => {
    const d = stubDocument('visible');
    useUtilization();
    expect(getUtilization).toHaveBeenCalledTimes(1);
    d.set('hidden');
    vi.advanceTimersByTime(HOUR);
    expect(getUtilization).toHaveBeenCalledTimes(1);
  });

  it('lets an explicit refresh ask while hidden', async () => {
    stubDocument('hidden');
    const { refresh } = useUtilization();
    await refresh();
    expect(getUtilization).toHaveBeenCalledTimes(1);
  });
});
