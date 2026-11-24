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
  getRunners: vi.fn(),
  getAllRunners: vi.fn(),
}));

import { getRunners } from '../api/client';
import { POLL_MS, useRunners } from './useRunners';

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

describe('useRunners', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    cleanups = [];
    vi.mocked(getRunners).mockReset();
    vi.mocked(getRunners).mockResolvedValue([] as never);
  });
  afterEach(() => {
    cleanups.forEach((c) => c());
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('sends nothing while hidden, however long it stays hidden', () => {
    stubDocument('hidden');
    useRunners(true);
    vi.advanceTimersByTime(HOUR);
    expect(getRunners).not.toHaveBeenCalled();
  });

  it('reads once on return, then keeps its interval', () => {
    const d = stubDocument('hidden');
    useRunners(true);
    vi.advanceTimersByTime(HOUR);
    d.set('visible');
    expect(getRunners).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(POLL_MS - 1);
    expect(getRunners).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(1);
    expect(getRunners).toHaveBeenCalledTimes(2);
  });

  it('stops asking when hidden again', () => {
    const d = stubDocument('visible');
    useRunners(true);
    expect(getRunners).toHaveBeenCalledTimes(1);
    d.set('hidden');
    vi.advanceTimersByTime(HOUR);
    expect(getRunners).toHaveBeenCalledTimes(1);
  });

  it('lets an explicit reload ask while hidden', async () => {
    stubDocument('hidden');
    const { reload } = useRunners(true);
    await reload();
    expect(getRunners).toHaveBeenCalledTimes(1);
  });

  it('sends nothing when not enabled, even while visible', () => {
    stubDocument('visible');
    useRunners(false);
    vi.advanceTimersByTime(HOUR);
    expect(getRunners).not.toHaveBeenCalled();
  });

  it('reads at once when turned on while visible', () => {
    stubDocument('visible');
    useRunners(false);
    expect(getRunners).not.toHaveBeenCalled();
    useRunners(true);
    expect(getRunners).toHaveBeenCalledTimes(1);
  });
});
