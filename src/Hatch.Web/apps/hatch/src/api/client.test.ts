import { afterEach, describe, expect, it, vi } from 'vitest';
import type { Issue, Runner, Status } from '../types';
import {
  createRunner,
  getActivity,
  getAllRunners,
  getAttachments,
  getAuthMe,
  getChangelogReport,
  getPlan,
  getQueue,
  getTimeline,
  getWorkLogHistory,
  getWorkLogSessions,
  liftRunnerOmission,
  omitRunner,
  orderRunner,
  orderRunnerSession,
  patchRunnerSession,
  pauseAllSessions,
  rotateRunner,
  revokeRunner,
  searchIssues,
  setRunnerOwner,
  stopAllSessions,
  suggestRunnerName,
  setStatusRunnerTags,
  uploadAttachment,
  withdrawOrder,
  withdrawSessionOrder,
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

describe('getAuthMe', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('reads null off a 401, the wall-off shape, rather than the generic refusal every other call means it as', async () => {
    const fetchMock = vi.fn(async () => new Response(null, { status: 401 }));
    vi.stubGlobal('fetch', fetchMock);

    await expect(getAuthMe()).resolves.toBeNull();
    expect(fetchMock).toHaveBeenCalledWith('/api/auth/me', expect.anything());
  });

  it('reads the grant off a 200', async () => {
    const me = { grantId: 'g1', personId: null };
    const fetchMock = vi.fn(async () => new Response(JSON.stringify(me), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await expect(getAuthMe()).resolves.toEqual(me);
  });
});

describe('runners page calls', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const answering = (body: unknown) => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify(body), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);
    return fetchMock;
  };
  const empty = () => {
    const fetchMock = vi.fn(async () => new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);
    return fetchMock;
  };

  it('getAllRunners GETs /api/hatch/runners with no live flag', async () => {
    const rows = [{ name: 'Gone' }] as Runner[];
    const fetchMock = answering(rows);

    await expect(getAllRunners()).resolves.toEqual(rows);

    expect(fetchMock).toHaveBeenCalledWith('/api/hatch/runners', expect.objectContaining({}));
  });

  it('suggestRunnerName GETs suggest-name and reads the bare string', async () => {
    const fetchMock = answering('Data');

    await expect(suggestRunnerName()).resolves.toBe('Data');

    expect(fetchMock).toHaveBeenCalledWith('/api/hatch/runners/suggest-name', expect.objectContaining({}));
  });

  it('createRunner POSTs the request and returns the minted runner', async () => {
    const minted = { runner: { name: 'Data' }, secret: 'hatch_ak_x' };
    const fetchMock = answering(minted);

    await expect(createRunner({ name: 'Data', ownerPersonId: 'p1' })).resolves.toEqual(minted);

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/hatch/runners',
      expect.objectContaining({ method: 'POST', body: '{"name":"Data","ownerPersonId":"p1"}' }),
    );
  });

  it('rotateRunner POSTs to the named runner, escaped, and returns the minted runner', async () => {
    const minted = { runner: { name: 'here:/x' }, secret: 'hatch_ak_new' };
    const fetchMock = answering(minted);

    await expect(rotateRunner('here:/x')).resolves.toEqual(minted);

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/hatch/runners/here%3A%2Fx/rotate',
      expect.objectContaining({ method: 'POST' }),
    );
  });

  it('revokeRunner POSTs to the named runner, escaped', async () => {
    const fetchMock = empty();

    await revokeRunner('here:/x');

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/hatch/runners/here%3A%2Fx/revoke',
      expect.objectContaining({ method: 'POST' }),
    );
  });

  it('setRunnerOwner PUTs the owner, null clearing it', async () => {
    const fetchMock = empty();

    await setRunnerOwner('here:/x', null);

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/hatch/runners/here%3A%2Fx/owner',
      expect.objectContaining({ method: 'PUT', body: '{"personId":null}' }),
    );
  });

  it('patchRunnerSession PATCHes the one session and returns the runner', async () => {
    const runner = { name: 'here:/x' } as Runner;
    const fetchMock = answering(runner);

    await expect(patchRunnerSession('here:/x', 7, { state: 'paused' })).resolves.toEqual(runner);

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/hatch/runners/here%3A%2Fx/sessions/7',
      expect.objectContaining({ method: 'PATCH', body: '{"state":"paused"}' }),
    );
  });

  it('orderRunnerSession POSTs the order to the one session and returns the runner', async () => {
    const runner = { name: 'here:/x' } as Runner;
    const fetchMock = answering(runner);

    await expect(orderRunnerSession('here:/x', 7, { kind: 'work', key: 'HA-12' })).resolves.toEqual(runner);

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/hatch/runners/here%3A%2Fx/sessions/7/order',
      expect.objectContaining({ method: 'POST', body: '{"kind":"work","key":"HA-12"}' }),
    );
  });

  it('withdrawSessionOrder DELETEs the session order and tolerates the empty answer', async () => {
    const fetchMock = empty();

    await expect(withdrawSessionOrder('here:/x', 7)).resolves.toBeUndefined();

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/hatch/runners/here%3A%2Fx/sessions/7/order',
      expect.objectContaining({ method: 'DELETE' }),
    );
  });

  it('pauseAllSessions POSTs to pause-all', async () => {
    const fetchMock = empty();

    await expect(pauseAllSessions('here:/x')).resolves.toBeUndefined();

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/hatch/runners/here%3A%2Fx/pause-all',
      expect.objectContaining({ method: 'POST' }),
    );
  });

  it('stopAllSessions POSTs to stop-all', async () => {
    const fetchMock = empty();

    await expect(stopAllSessions('here:/x')).resolves.toBeUndefined();

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/hatch/runners/here%3A%2Fx/stop-all',
      expect.objectContaining({ method: 'POST' }),
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

describe('getActivity', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const urlOf = async (query: Parameters<typeof getActivity>[0]) => {
    const fetchMock = vi.fn(async () => new Response('{}', { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await getActivity(query);

    return (fetchMock.mock.calls[0] as unknown[])[0];
  };

  it('sends no query string when nothing is set', async () => {
    expect(await urlOf({})).toBe('/api/hatch/activity');
  });

  it('keeps limit and offset as they were, and repeats projectId once per id after them', async () => {
    expect(await urlOf({ limit: 20, offset: 40, projectId: [3, 5] })).toBe(
      '/api/hatch/activity?limit=20&offset=40&projectId=3&projectId=5',
    );
    expect(await urlOf({ projectId: [3, 5] })).toBe('/api/hatch/activity?projectId=3&projectId=5');
  });

  it('treats an empty or null projectId as none', async () => {
    expect(await urlOf({ projectId: [] })).toBe('/api/hatch/activity');
    expect(await urlOf({ projectId: null })).toBe('/api/hatch/activity');
    expect(await urlOf({ limit: 20, projectId: null })).toBe('/api/hatch/activity?limit=20');
  });
});

describe('getWorkLogSessions', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const urlOf = async (query: Parameters<typeof getWorkLogSessions>[0]) => {
    const fetchMock = vi.fn(async () => new Response('{}', { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await getWorkLogSessions(query);

    return (fetchMock.mock.calls[0] as unknown[])[0];
  };

  const range = { from: '2026-10-01T00:00:00.000Z', to: '2026-11-01T00:00:00.000Z' };
  const base = '/api/hatch/work-log/sessions?from=2026-10-01T00%3A00%3A00.000Z&to=2026-11-01T00%3A00%3A00.000Z';

  it('builds today\'s URL without a projectId, leaving out null fields', async () => {
    expect(await urlOf(range)).toBe(base);
    expect(await urlOf({ ...range, ancestorKey: null, sort: 'tokens', limit: 5 })).toBe(base + '&sort=tokens&limit=5');
  });

  it('repeats projectId once per id', async () => {
    expect(await urlOf({ ...range, projectId: [3, 5] })).toBe(base + '&projectId=3&projectId=5');
  });

  it('treats an empty or null projectId as none', async () => {
    expect(await urlOf({ ...range, projectId: [] })).toBe(base);
    expect(await urlOf({ ...range, projectId: null })).toBe(base);
  });
});

describe('getWorkLogHistory', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const urlOf = async (query: Parameters<typeof getWorkLogHistory>[0]) => {
    const fetchMock = vi.fn(async () => new Response('{}', { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await getWorkLogHistory(query);

    return (fetchMock.mock.calls[0] as unknown[])[0];
  };

  const range = { from: '2026-10-01T00:00:00.000Z', to: '2026-11-01T00:00:00.000Z', offsetMinutes: -300 };
  const base =
    '/api/hatch/work-log/history?from=2026-10-01T00%3A00%3A00.000Z&to=2026-11-01T00%3A00%3A00.000Z&offsetMinutes=-300';

  it('builds today\'s URL without a projectId, leaving out null fields', async () => {
    expect(await urlOf(range)).toBe(base);
    expect(await urlOf({ ...range, ancestorKey: null })).toBe(base);
  });

  it('repeats projectId once per id, after the other fields', async () => {
    expect(await urlOf({ ...range, projectId: [3, 5] })).toBe(base + '&projectId=3&projectId=5');
  });

  it('treats an empty or null projectId as none', async () => {
    expect(await urlOf({ ...range, projectId: [] })).toBe(base);
    expect(await urlOf({ ...range, projectId: null })).toBe(base);
  });
});

describe('getPlan', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const urlOf = async (projectId?: number | number[]) => {
    const fetchMock = vi.fn(async () => new Response('{}', { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await getPlan(projectId);

    return (fetchMock.mock.calls[0] as unknown[])[0];
  };

  it('sends no query string with no projectId', async () => {
    expect(await urlOf()).toBe('/api/hatch/plan');
  });

  it('sends one projectId, as a single value', async () => {
    expect(await urlOf(3)).toBe('/api/hatch/plan?projectId=3');
  });

  it('repeats projectId once per id, given a list', async () => {
    expect(await urlOf([3, 5])).toBe('/api/hatch/plan?projectId=3&projectId=5');
  });

  it('treats an empty list as none', async () => {
    expect(await urlOf([])).toBe('/api/hatch/plan');
  });
});

describe('getTimeline', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const urlOf = async (query: Parameters<typeof getTimeline>[0]) => {
    const fetchMock = vi.fn(async () => new Response('{}', { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await getTimeline(query);

    return (fetchMock.mock.calls[0] as unknown[])[0];
  };

  it('sends no query string when nothing is set', async () => {
    expect(await urlOf({})).toBe('/api/hatch/reports/timeline');
  });

  it('sends one projectId, as a single value', async () => {
    expect(await urlOf({ projectId: 3 })).toBe('/api/hatch/reports/timeline?projectId=3');
  });

  it('repeats projectId once per id, given a list', async () => {
    expect(await urlOf({ projectId: [3, 5] })).toBe('/api/hatch/reports/timeline?projectId=3&projectId=5');
  });

  it('treats an empty or null projectId as none', async () => {
    expect(await urlOf({ projectId: [] })).toBe('/api/hatch/reports/timeline');
    expect(await urlOf({ projectId: null })).toBe('/api/hatch/reports/timeline');
  });
});

describe('searchIssues', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const urlOf = async (filter: Parameters<typeof searchIssues>[0]) => {
    const fetchMock = vi.fn(async () => new Response('[]', { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await searchIssues(filter);

    return (fetchMock.mock.calls[0] as unknown[])[0];
  };

  it('sends no query string with no filter set', async () => {
    expect(await urlOf({})).toBe('/api/hatch/issues');
  });

  it('sends one projectId, as a single value', async () => {
    expect(await urlOf({ projectId: 3 })).toBe('/api/hatch/issues?projectId=3');
  });

  it('repeats projectId once per id, given a list', async () => {
    expect(await urlOf({ projectId: [3, 5] })).toBe('/api/hatch/issues?projectId=3&projectId=5');
  });

  it('treats an empty or null projectId as none', async () => {
    expect(await urlOf({ projectId: [] })).toBe('/api/hatch/issues');
    expect(await urlOf({ projectId: null })).toBe('/api/hatch/issues');
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

describe('getAttachments', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('GETs the comma-joined ids and returns the parsed body', async () => {
    const found = [{ id: 'a', fileName: 'a.png', contentType: 'image/png', size: 1, createdAt: '2026-11-01T00:00:00Z' }];
    const fetchMock = vi.fn(async (_path: string, _init?: RequestInit) => new Response(JSON.stringify(found), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    const result = await getAttachments(['a', 'b']);

    expect(fetchMock.mock.calls[0][0]).toBe('/api/hatch/attachments?ids=a,b');
    expect(result).toEqual(found);
  });
});

describe('getQueue', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('asks for takers only where told to', async () => {
    const fetchMock = vi.fn(async () => new Response('[]', { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await getQueue();
    await getQueue(null, true);

    const urls = fetchMock.mock.calls.map((call) => String((call as unknown[])[0]));
    expect(urls[0]).not.toContain('takers');
    expect(urls[1]).toContain('takers=true');
  });
});
