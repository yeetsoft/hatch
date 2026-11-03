import { describe, expect, it } from 'vitest';
import {
  activityParts,
  activityWords,
  boundsChanged,
  boundsOf,
  boundsProblem,
  boundsRequest,
  budgetTone,
  dangerousLabel,
  drivesLabel,
  formRefusal,
  groupByHost,
  groupedTokens,
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
  topRefusal,
  tokenBudget,
  tokenFieldOf,
  tokensWire,
  TOKEN_WARN_PERCENT,
  ACTIVITY_TONES,
} from './runners';
import type { Runner } from '../types';
import type { RunnerBounds } from './runners';

const NOW = new Date('2026-09-08T12:00:00Z');

const at = (msFromNow: number) => new Date(NOW.getTime() + msFromNow).toISOString();

const GONE_AFTER = 90;

const runner = (over: Partial<Runner> = {}): Runner => ({
  name: 'here:/checkouts/one',
  kind: 'loop',
  firstSeenAt: at(-3_600_000),
  lastSeenAt: at(0),
  claimKey: null,
  claimStatus: null,
  line: null,
  lineAt: null,
  state: 'running',
  under: null,
  repositories: [],
  tags: [],
  projects: [],
  refusesDangerous: false,
  drives: [],
  clones: null,
  mine: null,
  where: 'here:/checkouts/one',
  agent: null,
  maxRuns: null,
  maxSpend: null,
  maxTokens: null,
  maxTokensPerHour: null,
  maxTokensSource: 'none',
  maxTokensPerHourSource: 'none',
  tokensUsed: null,
  tokensLastHour: null,
  throttledUntil: null,
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

describe('a throttled runner', () => {
  const GONE = { lastSeenAt: at(-GONE_AFTER * 1000 - 1000) };

  it('is throttled while its throttle is ahead of now', () => {
    expect(runnerActivity(runner({ throttledUntil: at(1_800_000) }), NOW)).toBe('throttled');
    expect(runnerActivity(runner({ throttledUntil: at(1_800_000), claimKey: 'AER-12' }), NOW)).toBe('throttled');
  });

  it('is gone rather than throttled or exhausted, because silence beats everything', () => {
    expect(runnerActivity(runner({ ...GONE, throttledUntil: at(1_800_000) }), NOW)).toBe('gone');
    expect(runnerActivity(runner({ ...GONE, throttledUntil: at(1_800_000), exhaustedUntil: at(3_600_000) }), NOW)).toBe('gone');
  });

  it('is exhausted before it is throttled, because being out of usage is the larger fact', () => {
    expect(runnerActivity(runner({ throttledUntil: at(1_800_000), exhaustedUntil: at(3_600_000) }), NOW)).toBe('exhausted');
  });

  it('is throttled before what it was asked to do', () => {
    expect(runnerActivity(runner({ throttledUntil: at(1_800_000), state: 'paused' }), NOW)).toBe('throttled');
    expect(runnerActivity(runner({ throttledUntil: at(1_800_000), state: 'stopping' }), NOW)).toBe('throttled');
  });

  it('is not throttled once the throttle has passed', () => {
    const passed = { throttledUntil: at(-1_800_000) };

    expect(runnerActivity(runner(passed), NOW)).toBe('idle');
    expect(runnerActivity(runner({ ...passed, state: 'paused' }), NOW)).toBe('paused');
    expect(runnerActivity(runner({ throttledUntil: at(0) }), NOW)).toBe('idle');
  });

  it('names the cap and the clock in the words on the row', () => {
    expect(activityWords(runner({ throttledUntil: at(3_600_000), maxTokensPerHour: 500_000 }), NOW)).toMatch(
      /^Over 500k tokens\/hour until \d{1,2}:\d{2}(am|pm)$/,
    );
  });

  it('says its hourly cap without a figure when the cap is not known', () => {
    expect(activityWords(runner({ throttledUntil: at(3_600_000), maxTokensPerHour: null }), NOW)).toMatch(
      /^Over its hourly token cap until \d{1,2}:\d{2}(am|pm)$/,
    );
  });

  it('is drawn as a danger and keeps its words whole in the strip', () => {
    expect(ACTIVITY_TONES.throttled).toBe('danger');
    expect(activityParts(runner({ throttledUntil: at(3_600_000), maxTokensPerHour: 1_000_000 }), NOW)).toMatchObject({
      words: expect.stringMatching(/^Over 1M tokens\/hour until /),
      claimKey: null,
    });
  });
});

describe('budgetTone', () => {
  it('turns to a warning at exactly the warn percent and to danger at the cap', () => {
    expect(TOKEN_WARN_PERCENT).toBe(80);
    expect(budgetTone(79.9)).toBe('success');
    expect(budgetTone(80)).toBe('warn');
    expect(budgetTone(99.9)).toBe('warn');
    expect(budgetTone(100)).toBe('danger');
    expect(budgetTone(0)).toBe('success');
  });
});

describe('tokenBudget', () => {
  it('is empty when neither cap is set, even where a total was reported', () => {
    expect(tokenBudget(runner())).toEqual([]);
    expect(tokenBudget(runner({ tokensUsed: 300_000, tokensLastHour: 100_000 }))).toEqual([]);
  });

  it('says the night and then the hour, each against its cap', () => {
    const lines = tokenBudget(runner({ maxTokens: 1_000_000, tokensUsed: 600_000, maxTokensPerHour: 500_000, tokensLastHour: 350_000 }));

    expect(lines).toEqual([
      { text: '600k / 1M tokens', percent: 60, tone: 'success' },
      { text: '350k / 500k per hour', percent: 70, tone: 'success' },
    ]);
  });

  it('warns at exactly 80 percent and not at 79.9', () => {
    expect(tokenBudget(runner({ maxTokens: 1000, tokensUsed: 800 }))[0]).toMatchObject({ percent: 80, tone: 'warn' });
    expect(tokenBudget(runner({ maxTokens: 1000, tokensUsed: 799 }))[0]).toMatchObject({ percent: 79.9, tone: 'success' });
  });

  it('takes the percent from the raw numbers, not the compact words', () => {
    // 1,249,999 / 1,562,500 is 79.99999%: it reads as 1.2M / 1.6M and must stay a success.
    expect(tokenBudget(runner({ maxTokens: 1_562_500, tokensUsed: 1_249_999 }))[0]?.tone).toBe('success');
  });

  it('is danger at the cap and over it, with the percent held at 100', () => {
    expect(tokenBudget(runner({ maxTokens: 1000, tokensUsed: 1000 }))[0]).toMatchObject({ percent: 100, tone: 'danger' });
    expect(tokenBudget(runner({ maxTokens: 1000, tokensUsed: 3000 }))[0]).toMatchObject({
      text: '3k / 1k tokens',
      percent: 100,
      tone: 'danger',
    });
  });

  it('says only the window that has a cap when the other has none and nothing was reported', () => {
    expect(tokenBudget(runner({ maxTokens: 1_000_000, tokensUsed: 250_000 }))).toEqual([
      { text: '250k / 1M tokens', percent: 25, tone: 'success' },
    ]);
    expect(tokenBudget(runner({ maxTokensPerHour: 500_000, tokensLastHour: 50_000 }))).toEqual([
      { text: '50k / 500k per hour', percent: 10, tone: 'success' },
    ]);
  });

  it('says a reported figure with no cap plainly, with no percent and no tone', () => {
    const lines = tokenBudget(runner({ maxTokens: 1_000_000, tokensUsed: 250_000, tokensLastHour: 40_000 }));

    expect(lines[1]).toEqual({ text: '40k per hour, no cap', percent: null, tone: null });

    const night = tokenBudget(runner({ maxTokensPerHour: 500_000, tokensLastHour: 50_000, tokensUsed: 900_000 }));

    expect(night[0]).toEqual({ text: '900k tokens, no cap', percent: null, tone: null });
    expect(night[1]).toMatchObject({ text: '50k / 500k per hour' });
  });

  it('counts a figure nobody reported as nothing under a cap', () => {
    expect(tokenBudget(runner({ maxTokens: 1_000_000 }))).toEqual([{ text: '0 / 1M tokens', percent: 0, tone: 'success' }]);
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

describe('dangerousLabel', () => {
  it('says nothing for a runner that takes dangerous work', () => {
    expect(dangerousLabel(runner())).toBeNull();
  });

  it('says the phrase for a runner that refuses it', () => {
    expect(dangerousLabel(runner({ refusesDangerous: true }))).toBe('Refuses dangerous work');
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
  const bounds = (over: Partial<RunnerBounds> = {}): RunnerBounds => ({
    under: '',
    maxRuns: '',
    maxSpend: '',
    maxTokens: '',
    maxTokensPerHour: '',
    untilAt: '',
    tags: '',
    projects: '',
    refusesDangerous: false,
    ...over,
  });

  it('shows an unbounded runner as empty boxes rather than as zeroes', () => {
    expect(boundsOf(runner())).toEqual(bounds());
  });

  it('round-trips what the row holds', () => {
    const bounded = runner({
      under: 'AER-930',
      maxRuns: 12,
      maxSpend: 40,
      maxTokens: 2_000_000,
      maxTokensPerHour: 500_000,
      untilAt: '2026-09-09T06:00:00Z',
      tags: ['gpu', 'fast'],
      projects: ['HA', 'AER'],
      refusesDangerous: true,
    });

    expect(boundsOf(bounded)).toEqual({
      under: 'AER-930',
      maxRuns: '12',
      maxSpend: '40',
      maxTokens: '2,000,000',
      maxTokensPerHour: '500,000',
      untilAt: '2026-09-09T06:00:00Z',
      tags: 'gpu\nfast',
      projects: 'HA\nAER',
      refusesDangerous: true,
    });
    expect(boundsChanged(boundsOf(bounded), bounded)).toBe(false);
  });

  it('notices a cap being taken off', () => {
    const bounded = runner({ maxRuns: 12 });

    expect(boundsChanged({ ...boundsOf(bounded), maxRuns: '' }, bounded)).toBe(true);
  });

  it('reads a token box by its count, so a typed equivalent is no change and a cleared box is', () => {
    const bounded = runner({ maxTokens: 2_000_000, maxTokensPerHour: 500_000 });

    expect(boundsChanged(boundsOf(bounded), bounded)).toBe(false);
    expect(boundsChanged({ ...boundsOf(bounded), maxTokens: '2M' }, bounded)).toBe(false);
    expect(boundsChanged({ ...boundsOf(bounded), maxTokensPerHour: ' 500k ' }, bounded)).toBe(false);
    expect(boundsChanged({ ...boundsOf(bounded), maxTokens: '3M' }, bounded)).toBe(true);
    expect(boundsChanged({ ...boundsOf(bounded), maxTokens: '' }, bounded)).toBe(true);
    expect(boundsChanged({ ...boundsOf(runner()), maxTokensPerHour: '500k' }, runner())).toBe(true);
  });

  it('compares a token box that does not parse as the text it holds', () => {
    const bounded = runner({ maxTokens: 2_000_000 });

    expect(boundsChanged({ ...boundsOf(bounded), maxTokens: 'abc' }, bounded)).toBe(true);
    expect(boundsChanged({ ...boundsOf(runner()), maxTokens: 'abc' }, runner())).toBe(true);
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

  it('notices the refuse-dangerous box flipping, and only that', () => {
    const open = runner();
    const refusing = runner({ refusesDangerous: true });

    expect(boundsChanged(boundsOf(open), open)).toBe(false);
    expect(boundsChanged({ ...boundsOf(open), refusesDangerous: true }, open)).toBe(true);
    expect(boundsChanged({ ...boundsOf(refusing), refusesDangerous: false }, refusing)).toBe(true);
  });

  it('sends refusesDangerous either way, so an unticked box can turn it off', () => {
    const base = boundsOf(runner());

    expect(boundsRequest({ ...base, refusesDangerous: true }).refusesDangerous).toBe(true);
    expect(boundsRequest({ ...base, refusesDangerous: false }).refusesDangerous).toBe(false);
  });

  it('sends every field, so a cleared box clears the cap', () => {
    // The tri-state, from the other end: a field left out of the request means
    // "leave it alone", so an emptied box that was not sent would silently keep
    // the cap it was clearing.
    expect(boundsRequest(bounds({ under: ' AER-930 ', maxSpend: '40', tags: 'gpu\n\nfast ', projects: 'HA\n\nAER ', refusesDangerous: true }))).toEqual({
      under: 'AER-930',
      maxRuns: '',
      maxSpend: '40',
      maxTokens: '',
      maxTokensPerHour: '',
      untilAt: '',
      tags: ['gpu', 'fast'],
      projects: ['HA', 'AER'],
      refusesDangerous: true,
    });
  });

  it('sends a token box as the wire reads it, with the on-blur commas taken out', () => {
    expect(boundsRequest(bounds({ maxTokens: '2M', maxTokensPerHour: ' 500k ' }))).toMatchObject({ maxTokens: '2M', maxTokensPerHour: '500k' });
    expect(boundsRequest(bounds({ maxTokens: '2,000,000', maxTokensPerHour: '500,000' }))).toMatchObject({
      maxTokens: '2000000',
      maxTokensPerHour: '500000',
    });
  });

  it('says an until needs an hour before the server has to', () => {
    expect(boundsProblem(bounds({ untilAt: '2026-09-09' }))).toContain('an hour');
    expect(boundsProblem(bounds({ untilAt: '2026-09-09T06:00:00Z' }))).toBeNull();
    expect(boundsProblem(bounds())).toBeNull();
  });

  it('says a token box needs a count before the server has to', () => {
    expect(boundsProblem(bounds({ maxTokens: 'abc' }))).toBe('Max tokens this run takes a token count, like 500k or 2M.');
    expect(boundsProblem(bounds({ maxTokensPerHour: '1.25M' }))).toBe('Max tokens per hour takes a token count, like 500k or 2M.');
    expect(boundsProblem(bounds({ maxTokens: '2,5' }))).not.toBeNull();
  });

  it('lets an empty token box, a grouped count and a short count through', () => {
    expect(boundsProblem(bounds({ maxTokens: '', maxTokensPerHour: '' }))).toBeNull();
    expect(boundsProblem(bounds({ maxTokens: '2,000,000' }))).toBeNull();
    expect(boundsProblem(bounds({ maxTokens: '2M', maxTokensPerHour: '500k' }))).toBeNull();
  });

  it('names the until problem before a token one', () => {
    expect(boundsProblem(bounds({ untilAt: '2026-09-09', maxTokens: 'abc' }))).toContain('an hour');
  });
});

describe('tokensWire', () => {
  it('trims, and strips commas only from an exact grouping', () => {
    expect(tokensWire(' 2,000,000 ')).toBe('2000000');
    expect(tokensWire('999')).toBe('999');
    expect(tokensWire('2M')).toBe('2M');
    expect(tokensWire('2,5')).toBe('2,5');
    expect(tokensWire('1,00')).toBe('1,00');
    expect(tokensWire(',000')).toBe(',000');
  });
});

describe('tokenFieldOf', () => {
  it('names the box a refusal is about, asking for the longer flag first', () => {
    expect(tokenFieldOf('--max-tokens takes a token count, like 500k or 2M - not "x"')).toBe('maxTokens');
    expect(tokenFieldOf('--max-tokens-per-hour takes a token count, like 500k or 2M - not "x"')).toBe('maxTokensPerHour');
  });

  it('is null for any other refusal, and for none', () => {
    expect(tokenFieldOf('--max-runs takes a whole number')).toBeNull();
    expect(tokenFieldOf(null)).toBeNull();
  });
});

describe('groupedTokens', () => {
  it('groups a count the way the box shows it once left', () => {
    expect(groupedTokens('2M')).toBe('2,000,000');
    expect(groupedTokens('500k')).toBe('500,000');
    expect(groupedTokens(' 2M ')).toBe('2,000,000');
  });

  it('leaves a grouped count, an empty box and an unparseable one as typed', () => {
    expect(groupedTokens('2,000,000')).toBe('2,000,000');
    expect(groupedTokens('')).toBe('');
    expect(groupedTokens('abc')).toBe('abc');
    expect(groupedTokens('2,5')).toBe('2,5');
  });
});

describe('refusal routing', () => {
  const perHour = '--max-tokens-per-hour takes a token count, like 500k or 2M - not "x"';
  const failure = (text: string, over: { runner?: string; routed?: boolean } = {}) => ({
    runner: 'a',
    text,
    routed: true,
    ...over,
  });

  it('routes a bounds save naming a token flag to that field on that runner, and off the top', () => {
    const f = failure(perHour);
    expect(topRefusal(f)).toBeNull();
    expect(formRefusal(f, 'a')).toEqual({ text: perHour, field: 'maxTokensPerHour' });
    expect(formRefusal(failure('--max-tokens takes a token count'), 'a')?.field).toBe('maxTokens');
  });

  it('gives another runner\'s form nothing', () => {
    expect(formRefusal(failure(perHour), 'b')).toBeNull();
  });

  it('keeps any other refusal at the top, with no form refusal', () => {
    const f = failure('--max-runs takes a whole number');
    expect(topRefusal(f)).toBe('--max-runs takes a whole number');
    expect(formRefusal(f, 'a')).toBeNull();
  });

  it('keeps a token-flag refusal of a press other than a bounds save at the top', () => {
    const f = failure(perHour, { routed: false });
    expect(topRefusal(f)).toBe(perHour);
    expect(formRefusal(f, 'a')).toBeNull();
  });

  it('is null both ways for no refusal', () => {
    expect(topRefusal(null)).toBeNull();
    expect(formRefusal(null, 'a')).toBeNull();
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

describe('groupByHost', () => {
  it('gathers a host at the place of its first member and sums runs and spend, null as zero', () => {
    const groups = groupByHost([
      runner({ name: 'a', host: 'box', runs: 2, spent: 1 }),
      runner({ name: 'x', host: 'other', runs: 1, spent: 5 }),
      runner({ name: 'b', host: 'box', runs: null, spent: 0.5 }),
    ]);
    expect(groups.map((g) => [g.host, g.runners.map((r) => r.name)])).toEqual([
      ['box', ['a', 'b']],
      ['other', ['x']],
    ]);
    expect(groups[0]).toMatchObject({ totalRuns: 2, totalSpent: 1.5 });
  });

  it('leaves each runner with no host alone, with a null host', () => {
    const groups = groupByHost([runner({ name: 'a', host: null }), runner({ name: 'b', host: '' })]);
    expect(groups.map((g) => [g.host, g.runners.length])).toEqual([
      [null, 1],
      [null, 1],
    ]);
  });
});
