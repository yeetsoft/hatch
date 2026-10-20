import { describe, expect, it } from 'vitest';
import { spotlight } from './spotlight';
import type { Issue, IssueCard, QueueEntry, Status } from '../types';

const TODO = 1;

// Verbatim from queue.test.ts.
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

// Verbatim from lineage.test.ts, same defaults.
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
  express: false,
  stalledAt: null,
  stalledWhy: null,
  held: false,
  pullRequestUrl: null,
  onePr: false,
  onePrOwn: null,
  onePrFrom: null,
});

// A queue row for `key` waiting on `dependsOn`, so each test reads as a graph.
const row = (key: string, dependsOn: string[] = [], over: Partial<QueueEntry> = {}): QueueEntry =>
  entry({ issue: issue({ key, dependsOnKeys: dependsOn }), ...over });

const flat = (...keys: string[]) => keys.map((k) => card(k, TODO, null));
const asObject = (m: Map<string, string>) => Object.fromEntries(m);

describe('spotlight', () => {
  it('returns an empty map for a null hover', () => {
    expect(spotlight(null, flat('A', 'B'), [row('A'), row('B')]).size).toBe(0);
  });

  it('returns an empty map for a hovered key no card carries', () => {
    expect(spotlight('GONE', flat('A', 'B'), [row('A'), row('GONE')]).size).toBe(0);
  });

  it('gives every other card an entry and the hovered card none', () => {
    const cards = flat('A', 'B', 'C');

    const result = spotlight('B', cards, []);

    expect(result.size).toBe(cards.length - 1);
    expect(result.has('B')).toBe(false);
  });

  it('lights a chain at depth red: A waits on B, which waits on C', () => {
    const result = spotlight('A', flat('A', 'B', 'C', 'D'), [row('A', ['B']), row('B', ['C']), row('C'), row('D')]);

    expect(result.get('B')).toBe('dependency');
    expect(result.get('C')).toBe('dependency');
    expect(result.get('D')).toBe('dim');
  });

  it('lights nothing for a prerequisite with no row, and the walk stops there', () => {
    // D is met (terminal column, no row); E is behind it and must not be reached.
    const cards = flat('H', 'B', 'D', 'E');
    const rows = [row('H', ['B']), row('B', ['D']), row('E')];

    const result = spotlight('H', cards, rows);

    expect(result.get('B')).toBe('dependency');
    expect(result.get('D')).toBe('dim');
    expect(result.get('E')).toBe('dim');
  });

  it("lights an ancestor's edge red", () => {
    const cards = [card('P', TODO, null), card('H', TODO, 'P'), card('X', TODO, null)];

    const result = spotlight('H', cards, [row('P', ['X']), row('H'), row('X')]);

    expect(result.get('X')).toBe('dependency');
  });

  it("lights the edge of a prerequisite's ancestor red, without lighting that ancestor", () => {
    const cards = [card('H', TODO, null), card('Y', TODO, 'Q'), card('Q', TODO, null), card('Z', TODO, null)];
    const rows = [row('H', ['Y']), row('Y'), row('Q', ['Z']), row('Z')];

    const result = spotlight('H', cards, rows);

    expect(result.get('Y')).toBe('dependency');
    expect(result.get('Z')).toBe('dependency');
    expect(result.get('Q')).not.toBe('dependency');
  });

  it('carries a chain on through a middle card the board is not drawing', () => {
    const cards = [card('H', TODO, null), card('M', TODO, null), card('Z', TODO, null)];

    const result = spotlight('H', cards, [row('H', ['M']), row('M', ['Z']), row('Z')]);

    expect(result.get('Z')).toBe('dependency');
  });

  it('lights rows before the hovered card yellow and rows after it dim', () => {
    const cards = flat('A', 'B', 'H', 'C');

    const result = spotlight('H', cards, [row('A'), row('B'), row('H'), row('C')]);

    expect(asObject(result)).toEqual({ A: 'turn', B: 'turn', C: 'dim' });
  });

  it('lights a folded row before the hovered card yellow, the same as a clear one', () => {
    const result = spotlight('H', flat('A', 'H'), [row('A', [], { blocked: 'waiting on a person' }), row('H')]);

    expect(result.get('A')).toBe('turn');
  });

  it('never lights a card with no row yellow', () => {
    const result = spotlight('H', flat('A', 'H'), [row('H')]);

    expect(result.get('A')).toBe('dim');
  });

  it('lights nothing yellow when the hovered card has no row', () => {
    const result = spotlight('H', flat('A', 'B', 'H'), [row('A'), row('B')]);

    expect(result.get('A')).toBe('dim');
    expect(result.get('B')).toBe('dim');
  });

  it('places a card at its first row when the queue holds two for it', () => {
    const result = spotlight('H', flat('A', 'H'), [row('A'), row('H'), row('A')]);

    expect(result.get('A')).toBe('turn');
  });

  it('marks ancestors and descendants at depth', () => {
    const cards = [
      card('G', TODO, null),
      card('P', TODO, 'G'),
      card('H', TODO, 'P'),
      card('C', TODO, 'H'),
      card('GC', TODO, 'C'),
      card('S', TODO, 'P'),
    ];

    const result = spotlight('H', cards, []);

    expect(asObject(result)).toEqual({ G: 'ancestor', P: 'ancestor', C: 'descendant', GC: 'descendant', S: 'dim' });
  });

  it('prefers dependency over turn for a card both waited on and earlier in the queue', () => {
    const result = spotlight('H', flat('A', 'H'), [row('A'), row('H', ['A'])]);

    expect(result.get('A')).toBe('dependency');
  });

  it('prefers turn over ancestor for an ancestor earlier in the queue', () => {
    const cards = [card('P', TODO, null), card('H', TODO, 'P')];

    const result = spotlight('H', cards, [row('P'), row('H')]);

    expect(result.get('P')).toBe('turn');
  });

  it('terminates on a parentKey cycle, hovering a card under it', () => {
    const cards = [card('A', TODO, 'B'), card('B', TODO, 'A'), card('C', TODO, 'A')];

    const result = spotlight('C', cards, []);

    expect(result.has('C')).toBe(false);
    expect(result.get('A')).toBe('ancestor');
    expect(result.get('B')).toBe('ancestor');
  });

  it('terminates on a parentKey cycle, hovering a member of it', () => {
    const cards = [card('A', TODO, 'B'), card('B', TODO, 'A')];

    const result = spotlight('A', cards, []);

    expect(result.has('A')).toBe(false);
    expect(result.get('B')).toBe('ancestor');
  });

  it('terminates on a dependency cycle and never lights the hovered card', () => {
    const result = spotlight('H', flat('H', 'B'), [row('H', ['B']), row('B', ['H'])]);

    expect(result.has('H')).toBe(false);
    expect(result.get('B')).toBe('dependency');
  });
});
