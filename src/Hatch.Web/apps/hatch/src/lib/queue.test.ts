import { describe, expect, it } from 'vitest';
import { anyAboveNormal, anyClaimed, queueCard, queueMarker, queueTally, queueWords } from './queue';
import type { BuildCheck, Issue, IssueCard, IssueClaim, MergeCheck, QueueEntry, Status } from '../types';

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
  express: false,
  mergeChecks: [],
  buildChecks: [],
  wipLimit: null,
  ...over,
});

const claim = (over: Partial<IssueClaim> = {}): IssueClaim => ({
  claimedBy: 'hatch',
  runner: 'Jeff Winger',
  claimedAt: new Date().toISOString(),
  heartbeatAt: new Date().toISOString(),
  chatter: null,
  chatterAt: null,
  ttlSeconds: 300,
  ...over,
});

const mergeCheck = (over: Partial<MergeCheck> = {}): MergeCheck => ({
  remote: 'git@forge.example:owner/repo.git',
  canonical: 'forge.example/owner/repo',
  trunk: 'main',
  trunkSha: '1'.repeat(40),
  verdict: 'conflicted',
  branch: 'aer-1-thing',
  branchSha: '2'.repeat(40),
  files: ['a.cs'],
  holdsTrunk: false,
  checkedAt: '2026-09-09T12:00:00Z',
  runner: 'box:/work/repo',
  checkedBy: 'runner',
  ...over,
});

const buildCheck = (over: Partial<BuildCheck> = {}): BuildCheck => ({
  remote: 'git@forge.example:owner/repo.git',
  canonical: 'forge.example/owner/repo',
  branch: 'aer-1-thing',
  sha: '2'.repeat(40),
  shaSince: '2026-09-09T12:00:00Z',
  verdict: 'failed',
  failing: [{ name: 'api', url: null }],
  pushedByIncrement: false,
  checkedAt: '2026-09-09T12:00:00Z',
  runner: 'box:/work/repo',
  checkedBy: 'runner',
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
  ...over,
});

const card = (over: Partial<IssueCard> = {}): IssueCard => ({
  key: 'AER-1',
  projectKey: 'AER',
  type: 'task',
  title: 'A task',
  statusId: 1,
  rank: 0,
  parentKey: null,
  readyAt: null,
  dueAt: null,
  openQuestions: 0,
  assignee: null,
  claim: null,
  expedited: false,
  priority: 'normal',
  priorityOwn: 'normal',
  priorityFrom: null,
  express: false,
  ...over,
});

describe('queueWords', () => {
  it('says the block, verbatim, before anything else', () => {
    const row = entry({
      blocked: 'waiting on a dependency',
      clearNote: 'should never be read',
      kind: 'conflicts',
    });

    expect(queueWords(row)).toBe('waiting on a dependency');
  });

  it('says a clear note with a plain hyphen, not an em dash', () => {
    const row = entry({ clearNote: 'something cleared' });

    expect(queueWords(row)).toBe('clear for To Do -> In Progress - something cleared');
  });

  it('pins the real lapsed-stall wording the server sends', () => {
    const row = entry({ clearNote: 'its stall question lapsed after 5 minutes untouched' });

    expect(queueWords(row)).toBe(
      'clear for To Do -> In Progress - its stall question lapsed after 5 minutes untouched',
    );
  });

  it('names the trunk of a conflict dispatch', () => {
    const row = entry({
      kind: 'conflicts',
      issue: issue({ mergeChecks: [mergeCheck({ trunk: 'release/2026.09' })] }),
    });

    expect(queueWords(row)).toBe('resolving conflicts with release/2026.09');
  });

  it('says "the trunk" for a conflict dispatch with no conflicted check named', () => {
    const row = entry({ kind: 'conflicts', issue: issue({ mergeChecks: [] }) });

    expect(queueWords(row)).toBe('resolving conflicts with the trunk');
  });

  it('names the failing checks of a build dispatch', () => {
    const row = entry({
      kind: 'build',
      issue: issue({
        buildChecks: [buildCheck({ failing: [{ name: 'api', url: null }, { name: 'CI', url: null }] })],
      }),
    });

    expect(queueWords(row)).toBe('fixing its failing build (api, CI)');
  });

  it('says the bare sentence for a build dispatch with nothing named', () => {
    const row = entry({
      kind: 'build',
      issue: issue({ buildChecks: [buildCheck({ failing: [] })] }),
    });

    expect(queueWords(row)).toBe('fixing its failing build');
  });

  it('says express for a hop with no hopKind named', () => {
    const row = entry({ hop: true, hopKind: null });

    expect(queueWords(row)).toBe('-> In Progress  (express, no session)');
  });

  it('says parent pulled for a parent hop', () => {
    const row = entry({ hop: true, hopKind: 'parent' });

    expect(queueWords(row)).toBe('-> In Progress  (parent pulled, no session)');
  });

  it('says epic for an epic hop', () => {
    const row = entry({ hop: true, hopKind: 'epic' });

    expect(queueWords(row)).toBe('-> In Progress  (epic, no session)');
  });

  it('says under the epic for an under hop', () => {
    const row = entry({ hop: true, hopKind: 'under', hopUnder: 'HA-86' });

    expect(queueWords(row)).toBe('-> In Progress  (under HA-86, no session)');
  });

  it('says a plain arrow for an ordinary advance', () => {
    const row = entry();

    expect(queueWords(row)).toBe('-> In Progress');
  });

  it('says "?" where there is nowhere to go', () => {
    const row = entry({ toStatus: null });

    expect(queueWords(row)).toBe('-> ?');
  });
});

describe('queueMarker', () => {
  it('marks an emergency row', () => {
    expect(queueMarker(entry({ issue: issue({ priority: 'emergency' }) }))).toBe('!!');
  });

  it('marks an expedited row', () => {
    expect(queueMarker(entry({ issue: issue({ priority: 'expedited' }) }))).toBe('!');
  });

  it('marks a low row', () => {
    expect(queueMarker(entry({ issue: issue({ priority: 'low' }) }))).toBe('-');
  });

  it('marks an economy row', () => {
    expect(queueMarker(entry({ issue: issue({ priority: 'economy' }) }))).toBe('~');
  });

  it('marks a normal row with nothing', () => {
    expect(queueMarker(entry({ issue: issue({ priority: 'normal' }) }))).toBeNull();
  });
});

describe('anyAboveNormal', () => {
  it('is false when every row is normal', () => {
    expect(anyAboveNormal([entry(), entry()])).toBe(false);
  });

  it('is true when any row carries a marker', () => {
    expect(anyAboveNormal([entry(), entry({ issue: issue({ priority: 'expedited' }) })])).toBe(true);
  });
});

describe('anyClaimed', () => {
  it('is false when no row\'s issue is claimed', () => {
    expect(anyClaimed([entry(), entry()])).toBe(false);
  });

  it('is true when any row\'s own issue is claimed', () => {
    expect(anyClaimed([entry(), entry({ issue: issue({ claim: claim() }) })])).toBe(true);
  });

  it('is false for a row folded by a relative\'s claim, since the issue\'s own claim is still null', () => {
    expect(anyClaimed([entry({ blocked: 'HA-12, above this, is working this' })])).toBe(false);
  });
});

describe('queueTally', () => {
  it('counts the whole pass and the rows clear of a block', () => {
    const tally = queueTally([entry(), entry({ blocked: 'waiting' }), entry(), entry({ blocked: 'waiting' })]);

    expect(tally).toEqual({ total: 4, clear: 2 });
  });
});

describe('queueCard', () => {
  it('finds the board card sharing the row\'s key', () => {
    const row = entry({ issue: issue({ key: 'AER-2' }) });
    const found = card({ key: 'AER-2' });

    expect(queueCard(row, [card({ key: 'AER-1' }), found])).toBe(found);
  });

  it('is undefined for a row the board is not holding', () => {
    const row = entry({ issue: issue({ key: 'AER-9' }) });

    expect(queueCard(row, [card({ key: 'AER-1' })])).toBeUndefined();
  });
});
