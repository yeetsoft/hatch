import { afterEach, describe, expect, it, vi } from 'vitest';
import type { Issue, Runner, Status } from '../types';
import {
  getChangelogReport,
  liftRunnerOmission,
  omitRunner,
  orderRunner,
  setStatusRunnerTags,
  uploadAttachment,
  withdrawOrder,
} from './client';

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

describe('omitRunner', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('PUTs the reason, escaping a slash in the runner name', async () => {
    const issue = { key: 'HA-12' } as Issue;
    const fetchMock = vi.fn(async () => new Response(JSON.stringify(issue), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    const result = await omitRunner('HA-12', 'host:/checkouts/one', { reason: 'too risky' });

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/hatch/issues/HA-12/omitted-runners/host%3A%2Fcheckouts%2Fone',
      expect.objectContaining({ method: 'PUT', body: '{"reason":"too risky"}' }),
    );
    expect(result).toEqual(issue);
  });
});

describe('liftRunnerOmission', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('DELETEs the omission, escaping a slash in the runner name', async () => {
    const issue = { key: 'HA-12' } as Issue;
    const fetchMock = vi.fn(async () => new Response(JSON.stringify(issue), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    const result = await liftRunnerOmission('HA-12', 'host:/checkouts/one');

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/hatch/issues/HA-12/omitted-runners/host%3A%2Fcheckouts%2Fone',
      expect.objectContaining({ method: 'DELETE' }),
    );
    expect(result).toEqual(issue);
  });
});

describe('getChangelogReport', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const urlOf = async (query: Parameters<typeof getChangelogReport>[0]) => {
    const fetchMock = vi.fn(async () => new Response('{}', { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await getChangelogReport(query);

    return (fetchMock.mock.calls[0] as unknown[])[0];
  };

  it('sends the level and offset alone when nothing else is set', async () => {
    expect(await urlOf({ level: 'story', offsetMinutes: 0 })).toBe(
      '/api/hatch/reports/changelog?level=story&offsetMinutes=0',
    );
  });

  it('sends every field, repeating projectId once per id', async () => {
    const url = await urlOf({
      from: '2026-10-01T00:00:00.000Z',
      to: '2026-11-01T00:00:00.000Z',
      projectId: [3, 5],
      bucket: 'week',
      level: 'epic',
      loose: true,
      offsetMinutes: -300,
    });

    expect(url).toBe(
      '/api/hatch/reports/changelog?from=2026-10-01T00%3A00%3A00.000Z&to=2026-11-01T00%3A00%3A00.000Z' +
        '&projectId=3&projectId=5&bucket=week&level=epic&offsetMinutes=-300&loose=true',
    );
  });

  it('leaves off a false loose, an empty from and a null bucket', async () => {
    expect(
      await urlOf({ loose: false, from: '', bucket: null, projectId: [], level: 'story', offsetMinutes: 60 }),
    ).toBe('/api/hatch/reports/changelog?level=story&offsetMinutes=60');
  });
});

describe('uploadAttachment', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('POSTs the file as multipart under its name, with no content type of its own', async () => {
    const stored = { id: 'abc', fileName: 'shot.png', contentType: 'image/png', size: 1, createdAt: '2026-11-01T00:00:00Z' };
    const fetchMock = vi.fn(async (_path: string, _init?: RequestInit) => new Response(JSON.stringify(stored), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    const result = await uploadAttachment(new Blob(['x'], { type: 'image/png' }), 'shot.png');

    expect(fetchMock.mock.calls[0][0]).toBe('/api/hatch/attachments');
    const init = fetchMock.mock.calls[0][1]!;
    expect(init.method).toBe('POST');
    expect(init.body).toBeInstanceOf(FormData);
    const file = (init.body as FormData).get('file');
    expect(file).toBeInstanceOf(File);
    expect((file as File).name).toBe('shot.png');
    expect(init.headers).toEqual({ Accept: 'application/json' });
    expect(result).toEqual(stored);
  });
});
