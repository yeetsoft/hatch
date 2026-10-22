import { describe, expect, it } from 'vitest';
import { chains } from './chains';
import { spotlight } from './spotlight';
import type { Issue, IssueCard, QueueEntry, Status } from '../types';

const TODO = 1;

// Verbatim from spotlight.test.ts.
const status = (over: Partial<Status> = {}): Status => ({
  id: 1,
  name: 'To Do',
  sortOrder: 0,
  isTerminal: false,
  isDeferred: false,
  isWip: true,
  expressSkips: false,
  parentPulls: false,
  agentFiles: false,
  isImplementation: false,
  isProtected: false,
  mergesPullRequest: false,
  color: '#888888',
  ...over,
});

const issue = (over: Partial<Issue> = {}): Issue => ({
  key: 'AER-1',
  projectId: 1,
  projectKey: 'AER',
  type: 'task',
  title: 'A task',
  description: '',
  statusId: 1,
  rank: 0,
  parentKey: null,
  childKeys: [],
  dependsOnKeys: [],
  dependentKeys: [],
  readyAt: null,
  dueAt: null,
  pullRequestUrl: null,
  onePr: false,
  onePrOwn: null,
  onePrFrom: null,
  releaseNote: null,
  modelOverride: null,
  effortOverride: null,
  assignee: null,
  createdBy: 'Britta Perry',
  createdAt: '2026-09-09T12:00:00Z',
  updatedAt: '2026-09-09T12:00:00Z',
  claim: null,
  expedited: false,
  priority: 'normal',
  priorityOwn: 'normal',
  priorityFrom: null,
  preApproval: 'none',
  preApprovalOwn: 'none',
  preApprovalFrom: null,
  runnerTags: [],
  runnerTagsOwn: [],
  runnerTagsFrom: [],
  express: false,
  stalledAt: null,
  stalledWhy: null,
  held: false,
  stallResumeSeconds: 0,
  mergeChecks: [],
  buildChecks: [],
  wipLimit: null,
  ...over,
});

const entry = (over: Partial<QueueEntry> = {}): QueueEntry => ({
  issue: issue(),
  fromStatus: status(),
  toStatus: status({ id: 2, name: 'In Progress' }),
  blocked: null,
  kind: 'advance',
  hop: false,
  hopKind: null,
  hopUnder: null,
  clearNote: null,
  model: null,
  deniedTools: null,
  blockedKind: null,
  deadlocked: false,
  ...over,
});

const card = (key: string, statusId: number, parentKey: string | null, rank = 1024): IssueCard => ({
  key,
  projectKey: 'AER',
  type: 'task',
  title: `${key} title`,
  statusId,
  rank,
  parentKey,
  readyAt: null,
  dueAt: null,
  openQuestions: 0,
  assignee: null,
  claim: null,
  expedited: false,
  priority: 'normal',
  priorityOwn: 'normal',
  priorityFrom: null,
  preApproval: 'none',
  preApprovalOwn: 'none',
  preApprovalFrom: null,
  runnerTags: [],
  runnerTagsOwn: [],
  runnerTagsFrom: [],
  express: false,
  stalledAt: null,
  stalledWhy: null,
  held: false,
  pullRequestUrl: null,
  onePr: false,
  onePrOwn: null,
  onePrFrom: null,
});

// A queue row for `key` waiting on `dependsOn`.
const row = (key: string, dependsOn: string[] = [], over: Partial<QueueEntry> = {}): QueueEntry =>
  entry({ issue: issue({ key, dependsOnKeys: dependsOn }), ...over });

// A queue row for `key` held by `dependents` - chains reads `dependentKeys`
// directly, never inferring it from scanning other rows' `dependsOnKeys`.
const holds = (key: string, dependents: string[], over: Partial<QueueEntry> = {}): QueueEntry =>
  entry({ issue: issue({ key, dependentKeys: dependents }), ...over });

const flat = (...keys: string[]) => keys.map((k) => card(k, TODO, null));

describe('chains', () => {
  it('returns an empty array for a null hover', () => {
    expect(chains(null, flat('A', 'B'), [row('A'), row('B')])).toEqual([]);
  });

  it('returns an empty array for a hovered card with no edges either way', () => {
    expect(chains('A', flat('A', 'B'), [row('A'), row('B')])).toEqual([]);
  });

  it('returns an empty array for a hovered key no card carries', () => {
    expect(chains('GONE', flat('A', 'B'), [row('A'), row('GONE')])).toEqual([]);
  });

  it('walks an upstream chain at depth: A waits on B, which waits on C', () => {
    const result = chains('A', flat('A', 'B', 'C'), [row('A', ['B']), row('B', ['C']), row('C')]);

    expect(result).toEqual(
      expect.arrayContaining([
        { from: 'B', to: 'A', flow: 'upstream' },
        { from: 'C', to: 'B', flow: 'upstream' },
      ]),
    );
    expect(result).toHaveLength(2);
  });

  it('ends the upstream walk at a prerequisite with no row', () => {
    // D is met (terminal column, no row); E is behind it and must not be reached.
    const cards = flat('H', 'B', 'D', 'E');
    const rows = [row('H', ['B']), row('B', ['D']), row('E')];

    const result = chains('H', cards, rows);

    expect(result).toEqual([{ from: 'B', to: 'H', flow: 'upstream' }]);
  });

  it("returns an ancestor's edge as an edge to the ancestor, not to the hovered card", () => {
    const cards = [card('P', TODO, null), card('H', TODO, 'P'), card('X', TODO, null)];

    const result = chains('H', cards, [row('P', ['X']), row('H'), row('X')]);

    expect(result).toEqual([{ from: 'X', to: 'P', flow: 'upstream' }]);
  });

  it("walks a downstream chain at depth, carrying it on through a dependent's descendants", () => {
    // H holds Y; Y's descendant Z is held by Y's own dependent-chain partner W.
    const cards = [card('H', TODO, null), card('Y', TODO, null), card('Z', TODO, 'Y'), card('W', TODO, null)];
    const rows = [holds('H', ['Y']), holds('Y', []), holds('Z', ['W']), holds('W', [])];

    const result = chains('H', cards, rows);

    expect(result).toEqual(
      expect.arrayContaining([
        { from: 'H', to: 'Y', flow: 'downstream' },
        { from: 'Z', to: 'W', flow: 'downstream' },
      ]),
    );
    expect(result).toHaveLength(2);
  });

  it('ends the downstream walk at a dependent with no row', () => {
    // E has no row at all (met, terminal column); the walk must not pretend it does.
    const cards = flat('H', 'B', 'E');
    const rows = [holds('H', ['B']), holds('B', ['E'])];

    const result = chains('H', cards, rows);

    expect(result).toEqual([{ from: 'H', to: 'B', flow: 'downstream' }]);
  });

  it('returns an edge reachable both ways once, as upstream', () => {
    // H's ancestor P depends on B, and B in turn depends on H - so B->P and
    // H->B are each found by both the upstream walk (via P as H's ancestor,
    // then recursing into B) and the downstream walk (via H's own dependent
    // B, then B's own dependent P).
    const cards = [card('P', TODO, null), card('H', TODO, 'P'), card('B', TODO, null)];
    const rows = [
      entry({ issue: issue({ key: 'P', dependsOnKeys: ['B'] }) }),
      entry({ issue: issue({ key: 'H', dependentKeys: ['B'] }) }),
      entry({ issue: issue({ key: 'B', dependsOnKeys: ['H'], dependentKeys: ['P'] }) }),
    ];

    const result = chains('H', cards, rows);

    expect(result).toEqual(
      expect.arrayContaining([
        { from: 'B', to: 'P', flow: 'upstream' },
        { from: 'H', to: 'B', flow: 'upstream' },
      ]),
    );
    expect(result).toHaveLength(2);
  });

  it('terminates on a parentKey cycle', () => {
    const cards = [card('A', TODO, 'B'), card('B', TODO, 'A'), card('H', TODO, 'A'), card('X', TODO, null)];

    const result = chains('H', cards, [row('A', ['X']), row('B'), row('H'), row('X')]);

    expect(result).toEqual(expect.arrayContaining([{ from: 'X', to: 'A', flow: 'upstream' }]));
  });

  it('terminates on a dependency cycle', () => {
    const result = chains('H', flat('H', 'B'), [row('H', ['B']), row('B', ['H'])]);

    expect(result).toEqual(
      expect.arrayContaining([
        { from: 'B', to: 'H', flow: 'upstream' },
        { from: 'H', to: 'B', flow: 'upstream' },
      ]),
    );
    expect(result).toHaveLength(2);
  });

  it("matches spotlight's dependency set against the upstream edges' from keys", () => {
    const cards = [card('P', TODO, null), card('H', TODO, 'P'), card('X', TODO, null), card('Y', TODO, null)];
    const rows = [row('P', ['X']), row('H', ['X', 'Y']), row('X'), row('Y')];

    const upstreamFrom = new Set(
      chains('H', cards, rows)
        .filter((edge) => edge.flow === 'upstream')
        .map((edge) => edge.from),
    );
    const dependencySpots = new Set(
      [...spotlight('H', cards, rows).entries()].filter(([, spot]) => spot === 'dependency').map(([key]) => key),
    );

    expect(dependencySpots).toEqual(upstreamFrom);
  });
});
