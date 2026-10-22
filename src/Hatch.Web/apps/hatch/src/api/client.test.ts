import { afterEach, describe, expect, it, vi } from 'vitest';
import type { Status } from '../types';
import { setStatusRunnerTags } from './client';

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
