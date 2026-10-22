import { afterEach, describe, expect, it, vi } from 'vitest';
import type { Runner, Status } from '../types';
import { orderRunner, setStatusRunnerTags, withdrawOrder } from './client';

const status: Status = {
  id: 7,
  name: 'To Do',
  sortOrder: 1,
  isTerminal: false,
  isDeferred: false,
  isWip: false,
  expressSkips: false,
  parentPulls: false,
  agentFiles: false,
  isImplementation: false,
  isProtected: false,
  mergesPullRequest: false,
  runnerTags: ['breakdown', 'macos'],
  color: '#888888',
};

describe('setStatusRunnerTags', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('PUTs the tags to the column and returns the column', async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify(status), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    const result = await setStatusRunnerTags(7, { tags: ['breakdown', 'macos'] });

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/hatch/statuses/7/runner-tags',
      expect.objectContaining({ method: 'PUT', body: '{"tags":["breakdown","macos"]}' }),
    );
    expect(result).toEqual(status);
  });
});

describe('runner orders', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('POSTs the order to the runner and returns the runner', async () => {
    const held = { name: 'here:/checkouts/one' } as Runner;
    const fetchMock = vi.fn(async () => new Response(JSON.stringify(held), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    const result = await orderRunner('here:/checkouts/one', { kind: 'work', key: 'HA-12' });

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/hatch/runners/here%3A%2Fcheckouts%2Fone/order',
      expect.objectContaining({ method: 'POST', body: '{"kind":"work","key":"HA-12"}' }),
    );
    expect(result).toEqual(held);
  });

  it('DELETEs the order and tolerates the empty answer', async () => {
    const fetchMock = vi.fn(async () => new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);

    await expect(withdrawOrder('here:/checkouts/one')).resolves.toBeUndefined();

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/hatch/runners/here%3A%2Fcheckouts%2Fone/order',
      expect.objectContaining({ method: 'DELETE' }),
    );
  });
});
