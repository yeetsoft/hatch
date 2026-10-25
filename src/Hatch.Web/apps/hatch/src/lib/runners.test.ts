import { describe, expect, it } from 'vitest';
import {
  activityParts,
  activityWords,
  boundsChanged,
  boundsOf,
  boundsProblem,
  boundsRequest,
  drivesLabel,
  isIssueKeyShape,
  orderSentence,
  isControllable,
  MAX_SPEND_HINT,
  mineLabel,
  repositoriesLabel,
  runnerActivity,
  runnerBuildLabel,
  runnerStrip,
  tagsLabel,
  projectsLabel,
} from './runners';
import type { Runner } from '../types';

const NOW = new Date('2026-09-08T12:00:00Z');

const at = (msFromNow: number) => new Date(NOW.getTime() + msFromNow).toISOString();

const GONE_AFTER = 90;

const runner = (over: Partial<Runner> = {}): Runner => ({
  name: 'here:/checkouts/one',
  kind: 'loop',
  firstSeenAt: at(-3_600_000),
  lastSeenAt: at(0),
  claimKey: null,
  line: null,
  lineAt: null,
  state: 'running',
  under: null,
  repositories: [],
  tags: [],
  projects: [],
  drives: [],
  clones: null,
  mine: null,
  where: 'here:/checkouts/one',
  agent: null,
  maxRuns: null,
  maxSpend: null,
  untilAt: null,
  goneAfterSeconds: GONE_AFTER,
  exhaustedUntil: null,
  for: null,
  canControl: true,
  yours: true,
  runs: null,
  spent: null,
  host: null,
  order: null,
  revision: null,
  sequence: null,
  builtAt: null,
  boardDrift: 'Unknown',
  sourceDrift: 'Unknown',
  ...over,
});

describe('runnerActivity', () => {
  it('is idle between increments', () => {
    expect(runnerActivity(runner(), NOW)).toBe('idle');
  });

  it('is working while it holds a ticket', () => {
    expect(runnerActivity(runner({ claimKey: 'AER-12' }), NOW)).toBe('working');
  });

  it('is here exactly at the horizon and gone one second past it', () => {
    expect(runnerActivity(runner({ lastSeenAt: at(-GONE_AFTER * 1000) }), NOW)).toBe('idle');
    expect(runnerActivity(runner({ lastSeenAt: at(-GONE_AFTER * 1000 - 1000) }), NOW)).toBe('gone');
  });

  it('is gone whatever it was asked to do, because silence is a fact and a pause is a request', () => {
    const quiet = { lastSeenAt: at(-GONE_AFTER * 1000 - 1000) };

    expect(runnerActivity(runner({ ...quiet, state: 'paused' }), NOW)).toBe('gone');
    expect(runnerActivity(runner({ ...quiet, state: 'stopping' }), NOW)).toBe('gone');
    expect(runnerActivity(runner({ ...quiet, claimKey: 'AER-12' }), NOW)).toBe('gone');
  });

  it('says what it was asked to do before what it is holding', () => {
    // A loop finishing its last increment: the ticket is true, and "this is the
    // last one" is what somebody watching needs.
    expect(runnerActivity(runner({ state: 'stopping', claimKey: 'AER-12' }), NOW)).toBe('stopping');
    expect(runnerActivity(runner({ state: 'paused' }), NOW)).toBe('paused');
  });

  it('reads a heartbeat it cannot parse as gone rather than as fine', () => {
    expect(runnerActivity(runner({ lastSeenAt: 'the other day' }), NOW)).toBe('gone');
  });

  it('names the ticket in the words on the row', () => {
    expect(activityWords(runner({ claimKey: 'AER-12' }), NOW)).toBe('Working AER-12');
    expect(activityWords(runner(), NOW)).toBe('Idle');
  });

  it('is exhausted when the runner says its account is out of Claude usage', () => {
    expect(runnerActivity(runner({ exhaustedUntil: at(3_600_000) }), NOW)).toBe('exhausted');
  });

  it('is exhausted before what it was asked to do, because a fact beats a request', () => {
    expect(runnerActivity(runner({ exhaustedUntil: at(3_600_000), state: 'paused' }), NOW)).toBe('exhausted');
    expect(runnerActivity(runner({ exhaustedUntil: at(3_600_000), state: 'stopping' }), NOW)).toBe('exhausted');
  });

  it('is gone rather than exhausted once it has stopped heartbeating, because silence beats everything', () => {
    expect(runnerActivity(runner({ exhaustedUntil: at(3_600_000), lastSeenAt: at(-GONE_AFTER * 1000 - 1000) }), NOW)).toBe(
      'gone',
    );
  });

  it('names the reset time in the words on the row', () => {
    expect(activityWords(runner({ exhaustedUntil: '2026-09-08T23:40:00Z' }), NOW)).toMatch(/^Out of usage until /);
  });

  it('is not exhausted once the reset has passed, because a reset that has passed is not a fact about the account any more', () => {
    const passed = { exhaustedUntil: at(-3_600_000) };

    expect(runnerActivity(runner(passed), NOW)).toBe('idle');
    expect(runnerActivity(runner({ ...passed, claimKey: 'AER-12' }), NOW)).toBe('working');
    expect(runnerActivity(runner({ ...passed, state: 'paused' }), NOW)).toBe('paused');
  });

  it('names just the clock for a reset later today', () => {
    const words = activityWords(runner({ exhaustedUntil: at(3_600_000) }), NOW);
    expect(words).toMatch(/^Out of usage until \d{1,2}:\d{2}(am|pm)$/);
  });

  it('names the weekday for a reset tomorrow', () => {
    const words = activityWords(runner({ exhaustedUntil: at(26 * 3_600_000) }), NOW);
    expect(words).toMatch(/^Out of usage until [A-Za-z]+ at \d{1,2}:\d{2}(am|pm)$/);
  });
});

describe('activityParts', () => {
  it('gives a working row the key apart from the words, which say Working alone', () => {
    expect(activityParts(runner({ claimKey: 'AER-12' }), NOW)).toEqual({ words: 'Working', claimKey: 'AER-12' });
  });

  it('gives any other row the full words and no key', () => {
    expect(activityParts(runner(), NOW)).toEqual({ words: 'Idle', claimKey: null });
    expect(activityParts(runner({ state: 'paused' }), NOW)).toEqual({ words: 'Paused', claimKey: null });
  });
});

describe('runnerStrip', () => {
  it('is empty for an empty list', () => {
    expect(runnerStrip([], NOW)).toEqual([]);
  });

  it('keeps the order given, not sorted', () => {
    const a = runner({ name: 'alfa' });
    const b = runner({ name: 'bravo' });

    expect(runnerStrip([b, a], NOW).map((row) => row.name)).toEqual(['bravo', 'alfa']);
  });

  it('gives a working row its claim key and line, Working, success and gone false', () => {
    const [row] = runnerStrip([runner({ claimKey: 'AER-12', line: 'reading the brief' })], NOW);

    expect(row).toMatchObject({
      words: 'Working',
      tone: 'success',
      claimKey: 'AER-12',
      line: 'reading the brief',
      gone: false,
    });
  });

  it('drops the line from an idle row even when the runner still has one', () => {
    const [row] = runnerStrip([runner({ line: 'an old line' })], NOW);

    expect(row.line).toBeNull();
    expect(row.claimKey).toBeNull();
  });

  it('is gone with no claim key three hours after the last heartbeat', () => {
    const [row] = runnerStrip([runner({ lastSeenAt: at(-3 * 3_600_000), claimKey: 'AER-12' })], NOW);

    expect(row).toMatchObject({
      gone: true,
      words: 'Gone',
      tone: 'danger',
      ago: '3 hours ago',
      claimKey: null,
    });
  });

  it('keeps Paused on a paused row', () => {
    const [row] = runnerStrip([runner({ state: 'paused' })], NOW);

    expect(row.words).toBe('Paused');
  });
});

describe('isControllable', () => {
  it('is a loop only, because nothing would ever read an instruction off a single increment', () => {
    expect(isControllable(runner())).toBe(true);
    expect(isControllable(runner({ kind: 'once' }))).toBe(false);
  });

  it('is false for a loop this caller cannot control', () => {
    expect(isControllable(runner({ canControl: false }))).toBe(false);
  });

  it('is false for a once runner even where canControl is true', () => {
    expect(isControllable(runner({ kind: 'once', canControl: true }))).toBe(false);
  });
});

describe('repositoriesLabel', () => {
  it('is null for a runner that has named none', () => {
    expect(repositoriesLabel(runner())).toBeNull();
  });

  it('joins one or more repositories', () => {
    expect(repositoriesLabel(runner({ repositories: ['example.com/owner/repo'] }))).toBe('example.com/owner/repo');
    expect(repositoriesLabel(runner({ repositories: ['example.com/owner/one', 'example.com/owner/two'] }))).toBe(
      'example.com/owner/one, example.com/owner/two',
    );
  });

  it('names clones what it lacks only when the runner says it does', () => {
    expect(repositoriesLabel(runner({ repositories: ['example.com/owner/repo'], clones: true }))).toBe(
      'example.com/owner/repo, clones what it lacks',
    );
    expect(repositoriesLabel(runner({ repositories: ['example.com/owner/repo'], clones: false }))).toBe(
      'example.com/owner/repo',
    );
    expect(repositoriesLabel(runner({ repositories: ['example.com/owner/repo'], clones: null }))).toBe(
      'example.com/owner/repo',
    );
  });
});

describe('projectsLabel', () => {
  it('says nothing for a runner held to no project', () => {
    expect(projectsLabel(runner())).toBeNull();
  });

  it('prefixes the keys with the word, since a bare key reads as nothing', () => {
    expect(projectsLabel(runner({ projects: ['HA'] }))).toBe('projects HA');
    expect(projectsLabel(runner({ projects: ['HA', 'AER'] }))).toBe('projects HA, AER');
  });
});

describe('tagsLabel', () => {
  it('is null for a runner that has named none', () => {
    expect(tagsLabel(runner())).toBeNull();
  });

  it('joins one or more tags', () => {
    expect(tagsLabel(runner({ tags: ['gpu'] }))).toBe('gpu');
    expect(tagsLabel(runner({ tags: ['gpu', 'macos'] }))).toBe('gpu, macos');
  });
});

describe('drivesLabel', () => {
  it('says driving nothing for a runner that drives none', () => {
    expect(drivesLabel(runner())).toBe('driving nothing');
  });

  it('names one project', () => {
    expect(drivesLabel(runner({ drives: ['AER'] }))).toBe('driving AER');
  });

  it('joins two or more projects', () => {
    expect(drivesLabel(runner({ drives: ['AER', 'HA'] }))).toBe('driving AER, HA');
  });
});

describe('mineLabel', () => {
  it('says own tickets only for a runner started with --mine', () => {
    expect(mineLabel(runner({ mine: true }))).toBe('own tickets only');
  });

  it('says nothing for a runner without the flag', () => {
    expect(mineLabel(runner({ mine: false }))).toBeNull();
    expect(mineLabel(runner({ mine: null }))).toBeNull();
  });
});

describe('MAX_SPEND_HINT', () => {
  it('says the cap cannot bind an agent that reports no price', () => {
    expect(MAX_SPEND_HINT).toContain('cannot bind');
  });
});

describe('the bounds form', () => {
  it('shows an unbounded runner as empty boxes rather than as zeroes', () => {
    expect(boundsOf(runner())).toEqual({ under: '', maxRuns: '', maxSpend: '', untilAt: '', tags: '', projects: '' });
  });

  it('round-trips what the row holds', () => {
    const bounded = runner({
      under: 'AER-930',
      maxRuns: 12,
      maxSpend: 40,
      untilAt: '2026-09-09T06:00:00Z',
      tags: ['gpu', 'fast'],
      projects: ['HA', 'AER'],
    });

    expect(boundsOf(bounded)).toEqual({
      under: 'AER-930',
      maxRuns: '12',
      maxSpend: '40',
      untilAt: '2026-09-09T06:00:00Z',
      tags: 'gpu\nfast',
      projects: 'HA\nAER',
    });
    expect(boundsChanged(boundsOf(bounded), bounded)).toBe(false);
  });

  it('notices a cap being taken off', () => {
    const bounded = runner({ maxRuns: 12 });

    expect(boundsChanged({ ...boundsOf(bounded), maxRuns: '' }, bounded)).toBe(true);
  });

  it('notices tags changing, order-sensitively rather than as trimmed text', () => {
    const bounded = runner({ tags: ['gpu', 'fast'] });

    expect(boundsChanged({ ...boundsOf(bounded), tags: 'gpu\nfast\n' }, bounded)).toBe(false);
    expect(boundsChanged({ ...boundsOf(bounded), tags: 'fast\ngpu' }, bounded)).toBe(true);
  });

  it('notices projects changing, order-sensitively rather than as trimmed text', () => {
    const bounded = runner({ projects: ['HA', 'AER'] });

    expect(boundsChanged({ ...boundsOf(bounded), projects: 'HA\nAER\n' }, bounded)).toBe(false);
    expect(boundsChanged({ ...boundsOf(bounded), projects: 'AER\nHA' }, bounded)).toBe(true);
  });

  it('sends every field, so a cleared box clears the cap', () => {
    // The tri-state, from the other end: a field left out of the request means
    // "leave it alone", so an emptied box that was not sent would silently keep
    // the cap it was clearing.
    expect(boundsRequest({ under: ' AER-930 ', maxRuns: '', maxSpend: '40', untilAt: '', tags: 'gpu\n\nfast ', projects: 'HA\n\nAER ' })).toEqual({
      under: 'AER-930',
      maxRuns: '',
      maxSpend: '40',
      untilAt: '',
      tags: ['gpu', 'fast'],
      projects: ['HA', 'AER'],
    });
  });

  it('says an until needs an hour before the server has to', () => {
    expect(boundsProblem({ under: '', maxRuns: '', maxSpend: '', untilAt: '2026-09-09', tags: '', projects: '' })).toContain('an hour');
    expect(boundsProblem({ under: '', maxRuns: '', maxSpend: '', untilAt: '2026-09-09T06:00:00Z', tags: '', projects: '' })).toBeNull();
    expect(boundsProblem({ under: '', maxRuns: '', maxSpend: '', untilAt: '', tags: '', projects: '' })).toBeNull();
  });
});

describe('orderSentence', () => {
  const ago = new Date(NOW.getTime() - 120_000).toISOString();

  it('names a work order ticket and who gave it', () => {
    expect(orderSentence({ kind: 'work', key: 'HA-12', by: 'Ada', at: ago }, NOW)).toBe(
      'ordered to work HA-12 by Ada, 2 minutes ago',
    );
  });

  it('leaves the key out of a pass and says somebody when nobody is named', () => {
    expect(orderSentence({ kind: 'pass', key: null, by: null, at: ago }, NOW)).toBe(
      'ordered to pass by somebody, 2 minutes ago',
    );
  });
});

describe('isIssueKeyShape', () => {
  it.each(['HA-12', 'ha-12', ' HA-12 ', 'AER2-7'])('accepts %j', (text) => {
    expect(isIssueKeyShape(text)).toBe(true);
  });

  it.each(['', 'HA', '12', 'HA-', 'HA-12 x'])('refuses %j', (text) => {
    expect(isIssueKeyShape(text)).toBe(false);
  });
});

describe('runnerBuildLabel', () => {
  const SHA = 'a1b2c3d4e5f60718293a4b5c6d7e8f9011121314';

  it('draws the short sha and how long ago for a stamped build', () => {
    expect(runnerBuildLabel(runner({ revision: SHA, builtAt: at(-3 * 60 * 60_000) }), NOW)).toBe(
      `${SHA.slice(0, 7)} · 3 hours ago`,
    );
  });

  it('says dev build for an unstamped revision', () => {
    expect(runnerBuildLabel(runner({ revision: 'dev', builtAt: null }), NOW)).toBe('dev build');
  });

  it('says nothing for a runner that has never reported a build', () => {
    expect(runnerBuildLabel(runner({ revision: null }), NOW)).toBeNull();
  });
});
