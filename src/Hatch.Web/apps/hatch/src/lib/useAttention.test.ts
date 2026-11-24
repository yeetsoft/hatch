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
  getAttention: vi.fn(),
}));

import { getAttention } from '../api/client';
import { POLL_MS, useAttention } from './useAttention';

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

describe('useAttention', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    cleanups = [];
    vi.mocked(getAttention).mockReset();
    vi.mocked(getAttention).mockResolvedValue({ items: [] } as never);
  });
  afterEach(() => {
    cleanups.forEach((c) => c());
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('sends nothing while hidden, however long it stays hidden', () => {
    stubDocument('hidden');
    useAttention();
    vi.advanceTimersByTime(HOUR);
    expect(getAttention).not.toHaveBeenCalled();
  });

  it('reads once on return, then keeps its interval', () => {
    const d = stubDocument('hidden');
    useAttention();
    vi.advanceTimersByTime(HOUR);
    d.set('visible');
    expect(getAttention).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(POLL_MS - 1);
    expect(getAttention).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(1);
    expect(getAttention).toHaveBeenCalledTimes(2);
  });

  it('stops asking when hidden again', () => {
    const d = stubDocument('visible');
    useAttention();
    expect(getAttention).toHaveBeenCalledTimes(1);
    d.set('hidden');
    vi.advanceTimersByTime(HOUR);
    expect(getAttention).toHaveBeenCalledTimes(1);
  });

  it('lets an explicit reload ask while hidden', async () => {
    stubDocument('hidden');
    const { reload } = useAttention();
    await reload();
    expect(getAttention).toHaveBeenCalledTimes(1);
  });
});
