import { describe, expect, it } from 'vitest';
import {
  activityParts,
  activityWords,
  boundsChanged,
  boundsOf,
  boundsProblem,
  boundsRequest,
  budgetTone,
  ceilingsChanged,
  ceilingsOf,
  ceilingsRequest,
  declaredDangerous,
  declaredProjects,
  sessionTotals,
  settingsChanged,
  settingsOf,
  settingsRequest,
  dangerousLabel,
  detailVariant,
  drivesLabel,
  groupedTokens,
  isIssueKeyShape,
  orderSentence,
  isControllable,
  liveLoopSessions,
  runnerActionable,
  sessionControllable,
  sessionOrder,
  MAX_SPEND_HINT,
  mineLabel,
  repositoriesLabel,
  liveSessions,
  recentSessions,
  runnerActivity,
  sessionActivity,
  sessionRepositoriesLabel,
  sessionsByStart,
  sessionTokenBudget,
  runnerBuildLabel,
  runnerStrip,
  tagsLabel,
  projectsLabel,
  modelsLabel,
  hasLabel,
  rowSecondLine,
  rowThirdLine,
  runnerGlyphState,
  runnerLastSeen,
  sortRunners,
  speakingSession,
  tokenBudget,
  tokenFieldHint,
  tokenFieldOf,
  tokensWire,
  TOKEN_WARN_PERCENT,
  ACTIVITY_TONES,
} from './runners';
import type { Runner, RunnerSession } from '../types';
import type { RunnerBounds } from './runners';
import { agoPhrase } from './claim';
import { TOKEN_FIELD_HINT } from './workLog';

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
  models: [],
  has: [],
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

  it('marks a cap inherited from the site default, in either window', () => {
    const night = tokenBudget(runner({ maxTokens: 1_000_000, tokensUsed: 600_000, maxTokensSource: 'default' }));
    expect(night[0].text).toBe('600k / 1M tokens (default)');

    const hour = tokenBudget(
      runner({ maxTokensPerHour: 500_000, tokensLastHour: 350_000, maxTokensPerHourSource: 'default' }),
    );
    expect(hour[0].text).toBe('350k / 500k per hour (default)');
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
    expect(isControllable(runner({ retiredAt: '2026-09-08T12:00:00Z' }))).toBe(false);
  });

  it('is false for a once runner even where canControl is true', () => {
    expect(isControllable(runner({ kind: 'once', canControl: true }))).toBe(false);
  });
});

describe('sessionControllable', () => {
  it('is a loop session on a runner this caller controls', () => {
    expect(sessionControllable(runner(), session())).toBe(true);
  });

  it('is false for a once session, since nothing would ever read an instruction off it', () => {
    expect(sessionControllable(runner(), session({ kind: 'once' }))).toBe(false);
  });

  it('is false for a viewer, a revoked runner and a retired one', () => {
    expect(sessionControllable(runner({ canControl: false }), session())).toBe(false);
    expect(sessionControllable(runner({ revokedAt: '2026-09-08T12:00:00Z' }), session())).toBe(false);
    expect(sessionControllable(runner({ retiredAt: '2026-09-08T12:00:00Z' }), session())).toBe(false);
  });
});

describe('sessionOrder', () => {
  it('is null for a session holding no order', () => {
    expect(sessionOrder(session())).toBeNull();
    expect(sessionOrder(session({ orderKind: 'work', orderKey: 'HA-1', orderedAt: null }))).toBeNull();
  });

  it('is the order in the shape orderSentence takes', () => {
    const held = sessionOrder(
      session({ orderKind: 'work', orderKey: 'HA-12', orderedBy: 'Ada', orderedAt: '2026-09-08T11:59:00Z' }),
    );

    expect(held).toEqual({ kind: 'work', key: 'HA-12', by: 'Ada', at: '2026-09-08T11:59:00Z' });
  });

  it('carries a pass with no key', () => {
    const held = sessionOrder(session({ orderKind: 'pass', orderedAt: '2026-09-08T11:59:00Z' }));

    expect(held).toEqual({ kind: 'pass', key: null, by: null, at: '2026-09-08T11:59:00Z' });
  });
});

describe('liveLoopSessions', () => {
  it('keeps the loops and drops a once session', () => {
    const live = [session({ id: 1 }), session({ id: 2, kind: 'once' }), session({ id: 3 })];

    expect(liveLoopSessions(live).map((s) => s.id)).toEqual([1, 3]);
  });
});

describe('detailVariant', () => {
  it('is admin whenever the caller is admin, owner otherwise, viewer when neither', () => {
    expect(detailVariant(true, runner())).toBe('admin');
    expect(detailVariant(false, runner({ canControl: true }))).toBe('owner');
    expect(detailVariant(false, runner({ canControl: false }))).toBe('viewer');
  });

  it('is still admin on a revoked runner', () => {
    expect(detailVariant(true, runner({ revokedAt: at(-1000) }))).toBe('admin');
  });
});

describe('runnerActionable', () => {
  it('is true with no revokedAt and false once set', () => {
    expect(runnerActionable(runner())).toBe(true);
    expect(runnerActionable(runner({ revokedAt: at(-1000) }))).toBe(false);
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

describe('modelsLabel', () => {
  it('is null for a runner that has reported none', () => {
    expect(modelsLabel(runner())).toBeNull();
  });

  it('prefixes the models with the word, since a bare list reads as nothing', () => {
    expect(modelsLabel(runner({ models: ['haiku', 'sonnet', 'opus'] }))).toBe('models haiku, sonnet, opus');
  });
});

describe('hasLabel', () => {
  it('is null for a runner that has reported none', () => {
    expect(hasLabel(runner())).toBeNull();
  });

  it('draws a versioned entry with the version as a separate word', () => {
    expect(hasLabel(runner({ has: ['node@22.11.0'] }))).toBe('has node 22.11.0');
  });

  it('joins a mix of versioned and bare entries', () => {
    expect(hasLabel(runner({ has: ['dotnet@10.0.100', 'pwsh'] }))).toBe('has dotnet 10.0.100, pwsh');
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
      maxTokensSource: 'runner',
      maxTokensPerHourSource: 'runner',
      untilAt: '2026-09-09T06:00:00Z',
      tags: ['gpu', 'fast'],
    });

    expect(boundsOf(bounded)).toEqual({
      under: 'AER-930',
      maxRuns: '12',
      maxSpend: '40',
      maxTokens: '2,000,000',
      maxTokensPerHour: '500,000',
      untilAt: '2026-09-09T06:00:00Z',
      tags: 'gpu\nfast',
    });
    expect(boundsChanged(boundsOf(bounded), bounded)).toBe(false);
  });

  it('shows a default-sourced cap as an empty box, not the inherited number', () => {
    const inherited = runner({
      maxTokens: 1_000_000,
      maxTokensPerHour: 500_000,
      maxTokensSource: 'default',
      maxTokensPerHourSource: 'default',
    });

    expect(boundsOf(inherited)).toMatchObject({ maxTokens: '', maxTokensPerHour: '' });
  });

  it('notices a cap being taken off', () => {
    const bounded = runner({ maxRuns: 12 });

    expect(boundsChanged({ ...boundsOf(bounded), maxRuns: '' }, bounded)).toBe(true);
  });

  it('reads a token box by its count, so a typed equivalent is no change and a cleared box is', () => {
    const bounded = runner({
      maxTokens: 2_000_000,
      maxTokensPerHour: 500_000,
      maxTokensSource: 'runner',
      maxTokensPerHourSource: 'runner',
    });

    expect(boundsChanged(boundsOf(bounded), bounded)).toBe(false);
    expect(boundsChanged({ ...boundsOf(bounded), maxTokens: '2M' }, bounded)).toBe(false);
    expect(boundsChanged({ ...boundsOf(bounded), maxTokensPerHour: ' 500k ' }, bounded)).toBe(false);
    expect(boundsChanged({ ...boundsOf(bounded), maxTokens: '3M' }, bounded)).toBe(true);
    expect(boundsChanged({ ...boundsOf(bounded), maxTokens: '' }, bounded)).toBe(true);
    expect(boundsChanged({ ...boundsOf(runner()), maxTokensPerHour: '500k' }, runner())).toBe(true);
  });

  it('compares a token box that does not parse as the text it holds', () => {
    const bounded = runner({ maxTokens: 2_000_000, maxTokensSource: 'runner' });

    expect(boundsChanged({ ...boundsOf(bounded), maxTokens: 'abc' }, bounded)).toBe(true);
    expect(boundsChanged({ ...boundsOf(runner()), maxTokens: 'abc' }, runner())).toBe(true);
  });

  it('notices tags changing, order-sensitively rather than as trimmed text', () => {
    const bounded = runner({ tags: ['gpu', 'fast'] });

    expect(boundsChanged({ ...boundsOf(bounded), tags: 'gpu\nfast\n' }, bounded)).toBe(false);
    expect(boundsChanged({ ...boundsOf(bounded), tags: 'fast\ngpu' }, bounded)).toBe(true);
  });

  it('sends every field, so a cleared box clears the cap', () => {
    // The tri-state, from the other end: a field left out of the request means
    // "leave it alone", so an emptied box that was not sent would silently keep
    // the cap it was clearing.
    const req = boundsRequest(bounds({ under: ' AER-930 ', maxSpend: '40', tags: 'gpu\n\nfast ' }));
    expect(req).toEqual({
      under: 'AER-930',
      maxRuns: '',
      maxSpend: '40',
      maxTokens: '',
      maxTokensPerHour: '',
      untilAt: '',
      tags: ['gpu', 'fast'],
    });
    expect(req).not.toHaveProperty('projects');
    expect(req).not.toHaveProperty('refusesDangerous');
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

describe('tokenFieldHint', () => {
  it('gives the shared wording for a runner-owned cap or no cap at all', () => {
    expect(tokenFieldHint(runner({ maxTokensSource: 'runner' }), 'maxTokens')).toBe(TOKEN_FIELD_HINT);
    expect(tokenFieldHint(runner({ maxTokensSource: 'none' }), 'maxTokens')).toBe(TOKEN_FIELD_HINT);
    expect(tokenFieldHint(runner({ maxTokensPerHourSource: 'runner' }), 'maxTokensPerHour')).toBe(TOKEN_FIELD_HINT);
    expect(tokenFieldHint(runner({ maxTokensPerHourSource: 'none' }), 'maxTokensPerHour')).toBe(TOKEN_FIELD_HINT);
  });

  it('names the inherited figure for a default-sourced cap, in each field\'s own words', () => {
    expect(tokenFieldHint(runner({ maxTokens: 1_000_000, maxTokensSource: 'default' }), 'maxTokens')).toBe(
      'Empty inherits the default: 1M tokens.',
    );
    expect(
      tokenFieldHint(runner({ maxTokensPerHour: 500_000, maxTokensPerHourSource: 'default' }), 'maxTokensPerHour'),
    ).toBe('Empty inherits the default: 500k per hour.');
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

  it('takes a session-shaped object the same way, undefined read as null', () => {
    expect(runnerBuildLabel(session({ revision: SHA, builtAt: at(-3 * 60 * 60_000) }), NOW)).toBe(
      `${SHA.slice(0, 7)} · 3 hours ago`,
    );
    expect(runnerBuildLabel(session({ revision: undefined }), NOW)).toBeNull();
  });
});

const session = (over: Partial<RunnerSession> = {}): RunnerSession => ({
  id: 1,
  where: 'here:/checkouts/one',
  kind: 'loop',
  host: null,
  state: 'running',
  startedAt: at(-3_600_000),
  lastSeenAt: at(0),
  endedAt: null,
  claimKey: null,
  agent: 'claude',
  os: 'Mac',
  exhaustedUntil: null,
  throttledUntil: null,
  ...over,
});

describe('sessionActivity', () => {
  it('is working with a claim and idle without one', () => {
    expect(sessionActivity(session({ claimKey: 'AER-12' }), runner(), NOW)).toBe('working');
    expect(sessionActivity(session(), runner(), NOW)).toBe('idle');
  });

  it('is exhausted while its own reset is ahead', () => {
    expect(sessionActivity(session({ exhaustedUntil: at(3_600_000) }), runner(), NOW)).toBe('exhausted');
    expect(sessionActivity(session({ exhaustedUntil: at(-1000) }), runner(), NOW)).toBe('idle');
  });

  it('judges silence against its runner\'s horizon, not the runner\'s own heartbeat', () => {
    expect(sessionActivity(session({ lastSeenAt: at(-GONE_AFTER * 1000) }), runner(), NOW)).toBe('idle');
    expect(sessionActivity(session({ lastSeenAt: at(-GONE_AFTER * 1000 - 1000) }), runner(), NOW)).toBe('gone');
  });

  it('agrees with the row\'s own rule on every state the row can be in', () => {
    const cases: [Partial<Runner>, Partial<RunnerSession>][] = [
      [{ claimKey: 'AER-1' }, { claimKey: 'AER-1' }],
      [{ state: 'paused' }, { state: 'paused' }],
      [{ state: 'stopping', claimKey: 'AER-1' }, { state: 'stopping', claimKey: 'AER-1' }],
      [{ exhaustedUntil: at(1000), state: 'paused' }, { exhaustedUntil: at(1000), state: 'paused' }],
      [{ throttledUntil: at(1000) }, { throttledUntil: at(1000) }],
    ];
    for (const [r, s] of cases) {
      expect(sessionActivity(session(s), runner(), NOW)).toBe(runnerActivity(runner(r), NOW));
    }
  });
});

describe('liveSessions', () => {
  it('keeps two live sessions in the server\'s order', () => {
    const a = session({ id: 1, where: 'Mac:/one' });
    const b = session({ id: 2, where: 'Mac:/two' });
    expect(liveSessions(runner({ sessions: [a, b] }), NOW).map((s) => s.where)).toEqual(['Mac:/one', 'Mac:/two']);
  });

  it('drops an ended session', () => {
    const ended = session({ id: 2, endedAt: at(-1000) });
    expect(liveSessions(runner({ sessions: [session(), ended] }), NOW).map((s) => s.id)).toEqual([1]);
  });

  it('keeps a session exactly at the horizon and drops one a second past it', () => {
    const edge = session({ id: 1, lastSeenAt: at(-GONE_AFTER * 1000) });
    const past = session({ id: 2, lastSeenAt: at(-GONE_AFTER * 1000 - 1000) });
    expect(liveSessions(runner({ sessions: [edge, past] }), NOW).map((s) => s.id)).toEqual([1]);
  });

  it('is empty for a runner that carries no sessions', () => {
    expect(liveSessions(runner(), NOW)).toEqual([]);
    expect(liveSessions(runner({ sessions: [] }), NOW)).toEqual([]);
  });
});

describe('runnerGlyphState', () => {
  it('is gone when no session is live, even if their facts would otherwise rank lower', () => {
    const quiet = at(-GONE_AFTER * 1000 - 1000);
    const r = runner({
      sessions: [
        session({ id: 1, lastSeenAt: quiet, exhaustedUntil: at(1000) }),
        session({ id: 2, lastSeenAt: quiet, throttledUntil: at(1000) }),
        session({ id: 3, lastSeenAt: quiet, state: 'paused' }),
        session({ id: 4, lastSeenAt: quiet, state: 'stopping' }),
        session({ id: 5, lastSeenAt: quiet, claimKey: 'AER-1' }),
        session({ id: 6, lastSeenAt: quiet }),
      ],
    });
    expect(runnerGlyphState(r, NOW)).toBe('gone');
  });

  it('is exhausted when its highest-ranked live session is exhausted', () => {
    const r = runner({
      sessions: [
        session({ id: 1, exhaustedUntil: at(1000) }),
        session({ id: 2, throttledUntil: at(1000) }),
        session({ id: 3, state: 'paused' }),
        session({ id: 4, state: 'stopping' }),
        session({ id: 5, claimKey: 'AER-1' }),
        session({ id: 6 }),
      ],
    });
    expect(runnerGlyphState(r, NOW)).toBe('exhausted');
  });

  it('is throttled when its highest-ranked live session is throttled', () => {
    const r = runner({
      sessions: [
        session({ id: 1, throttledUntil: at(1000) }),
        session({ id: 2, state: 'paused' }),
        session({ id: 3, state: 'stopping' }),
        session({ id: 4, claimKey: 'AER-1' }),
        session({ id: 5 }),
      ],
    });
    expect(runnerGlyphState(r, NOW)).toBe('throttled');
  });

  it('is paused when its highest-ranked live session is paused', () => {
    const r = runner({
      sessions: [session({ id: 1, state: 'paused' }), session({ id: 2, claimKey: 'AER-1' }), session({ id: 3 })],
    });
    expect(runnerGlyphState(r, NOW)).toBe('paused');
  });

  it('is stopping when its highest-ranked live session is stopping', () => {
    const r = runner({
      sessions: [session({ id: 1, state: 'stopping' }), session({ id: 2, claimKey: 'AER-1' }), session({ id: 3 })],
    });
    expect(runnerGlyphState(r, NOW)).toBe('stopping');
  });

  it('paused and stopping tie - either alone reads as itself', () => {
    expect(runnerGlyphState(runner({ sessions: [session({ id: 1, state: 'paused' })] }), NOW)).toBe('paused');
    expect(runnerGlyphState(runner({ sessions: [session({ id: 1, state: 'stopping' })] }), NOW)).toBe('stopping');
  });

  it('is working when its highest-ranked live session is working', () => {
    const r = runner({ sessions: [session({ id: 1, claimKey: 'AER-1' }), session({ id: 2 })] });
    expect(runnerGlyphState(r, NOW)).toBe('working');
  });

  it('is idle when its only live session is idle', () => {
    expect(runnerGlyphState(runner({ sessions: [session({ id: 1 })] }), NOW)).toBe('idle');
  });

  it('is none with no sessions field, and with an empty sessions array - never gone', () => {
    expect(runnerGlyphState(runner(), NOW)).toBe('none');
    expect(runnerGlyphState(runner({ sessions: [] }), NOW)).toBe('none');
  });

  it('reads silence over usage: a live idle session beats an exhausted one that has gone quiet', () => {
    const r = runner({
      sessions: [
        session({ id: 1 }),
        session({ id: 2, exhaustedUntil: at(1000), lastSeenAt: at(-GONE_AFTER * 1000 - 1000) }),
      ],
    });
    expect(runnerGlyphState(r, NOW)).toBe('idle');
  });

  it('is gone when every session has ended or gone quiet', () => {
    const ended = session({ id: 1, endedAt: at(-1000) });
    const quiet = session({ id: 2, lastSeenAt: at(-GONE_AFTER * 1000 - 1000) });
    expect(runnerGlyphState(runner({ sessions: [ended, quiet] }), NOW)).toBe('gone');
  });
});

describe('speakingSession', () => {
  it('is null with no live session', () => {
    expect(speakingSession(runner(), NOW)).toBeNull();
  });

  it('picks the newest among the winning rank, not the newest overall', () => {
    const working = session({ id: 1, claimKey: 'AER-1', lastSeenAt: at(-60_000) });
    const idle = session({ id: 2, lastSeenAt: at(0) });
    expect(speakingSession(runner({ sessions: [working, idle] }), NOW)).toBe(working);
  });

  it('breaks a tie within the winning rank by lastSeenAt, earlier server order on an exact tie', () => {
    const older = session({ id: 1, claimKey: 'AER-1', lastSeenAt: at(-60_000) });
    const newer = session({ id: 2, claimKey: 'AER-2', lastSeenAt: at(0) });
    expect(speakingSession(runner({ sessions: [older, newer] }), NOW)).toBe(newer);

    const tie1 = session({ id: 3, claimKey: 'AER-3', lastSeenAt: at(0) });
    const tie2 = session({ id: 4, claimKey: 'AER-4', lastSeenAt: at(0) });
    expect(speakingSession(runner({ sessions: [tie1, tie2] }), NOW)).toBe(tie1);
  });
});

describe('rowSecondLine', () => {
  it('working with a line names the key and the line', () => {
    const r = runner({ sessions: [session({ id: 1, claimKey: 'AER-1', line: 'Reading the ticket' })] });
    expect(rowSecondLine(r, NOW)).toBe('AER-1 - Reading the ticket');
  });

  it('working with no line names the key alone', () => {
    const r = runner({ sessions: [session({ id: 1, claimKey: 'AER-1', line: null })] });
    expect(rowSecondLine(r, NOW)).toBe('AER-1');
  });

  it('working with another working session counts it', () => {
    const r = runner({
      sessions: [
        session({ id: 1, claimKey: 'AER-1', line: 'Reading the ticket' }),
        session({ id: 2, claimKey: 'AER-2' }),
      ],
    });
    expect(rowSecondLine(r, NOW)).toBe('AER-1 - Reading the ticket +1 more');
  });

  it('idle names when it was last seen', () => {
    const r = runner({ sessions: [session({ id: 1, lastSeenAt: at(-60_000) })] });
    expect(rowSecondLine(r, NOW)).toBe(`Idle - seen ${agoPhrase(at(-60_000), NOW)}`);
    expect(rowSecondLine(r, NOW)).toBe('Idle - seen 1 minute ago');
  });

  it('paused draws its words', () => {
    expect(rowSecondLine(runner({ sessions: [session({ id: 1, state: 'paused' })] }), NOW)).toBe('Paused');
  });

  it('stopping draws its words', () => {
    expect(rowSecondLine(runner({ sessions: [session({ id: 1, state: 'stopping' })] }), NOW)).toBe('Stopping');
  });

  it('throttled names the cap and reset', () => {
    const r = runner({ sessions: [session({ id: 1, throttledUntil: at(3_600_000) })] });
    expect(rowSecondLine(r, NOW)).toMatch(/^Over /);
  });

  it('exhausted names the reset', () => {
    const r = runner({ sessions: [session({ id: 1, exhaustedUntil: at(3_600_000) })] });
    expect(rowSecondLine(r, NOW)).toMatch(/^Out of usage until /);
  });

  it('gone names when it was last seen', () => {
    const quiet = at(-GONE_AFTER * 1000 - 1000);
    const r = runner({ sessions: [session({ id: 1, lastSeenAt: quiet })] });
    expect(rowSecondLine(r, NOW)).toBe(`Gone - last seen ${agoPhrase(quiet, NOW)}`);
  });

  it('none says it has not started', () => {
    expect(rowSecondLine(runner(), NOW)).toBe('Not started');
  });
});

describe('rowThirdLine', () => {
  it('names the owner and the one live host', () => {
    const r = runner({ for: 'Ada', sessions: [session({ id: 1, host: 'Mac' })] });
    expect(rowThirdLine(r, NOW)).toBe('Ada · Mac');
  });

  it('names no owner when none is set', () => {
    const r = runner({ for: null, sessions: [session({ id: 1, host: 'Mac' })] });
    expect(rowThirdLine(r, NOW)).toBe('no owner · Mac');
  });

  it('counts machines when live sessions span more than one host', () => {
    const r = runner({ for: 'Ada', sessions: [session({ id: 1, host: 'Mac' }), session({ id: 2, host: 'Linux' })] });
    expect(rowThirdLine(r, NOW)).toBe('Ada · 2 machines');
  });

  it('names the host once when every live session shares it', () => {
    const r = runner({ for: 'Ada', sessions: [session({ id: 1, host: 'Mac' }), session({ id: 2, host: 'Mac' })] });
    expect(rowThirdLine(r, NOW)).toBe('Ada · Mac');
  });

  it('is the owner alone with no sessions', () => {
    expect(rowThirdLine(runner({ for: 'Ada' }), NOW)).toBe('Ada');
  });
});

describe('runnerLastSeen', () => {
  it('is the newest lastSeenAt across live and ended sessions', () => {
    const r = runner({
      sessions: [
        session({ id: 1, lastSeenAt: at(-3_600_000) }),
        session({ id: 2, lastSeenAt: at(-60_000), endedAt: at(-60_000) }),
      ],
    });
    expect(runnerLastSeen(r)).toBe(at(-60_000));
  });

  it('is null with no sessions', () => {
    expect(runnerLastSeen(runner())).toBeNull();
    expect(runnerLastSeen(runner({ sessions: [] }))).toBeNull();
  });
});

describe('sortRunners', () => {
  it('puts a live runner before one seen more recently but not live', () => {
    const live = runner({ name: 'b', apiKeyId: 'k1', sessions: [session({ id: 1, lastSeenAt: at(-30_000) })] });
    const notLive = runner({
      name: 'a',
      apiKeyId: 'k2',
      sessions: [session({ id: 2, lastSeenAt: at(0), endedAt: at(0) })],
    });
    expect(sortRunners([notLive, live], NOW).shown.map((r) => r.name)).toEqual(['b', 'a']);
  });

  it('orders by lastSeenAt descending, with null last', () => {
    const newer = runner({
      name: 'a',
      apiKeyId: 'k1',
      sessions: [session({ id: 1, lastSeenAt: at(-60_000), endedAt: at(-60_000) })],
    });
    const older = runner({
      name: 'b',
      apiKeyId: 'k2',
      sessions: [session({ id: 2, lastSeenAt: at(-3_600_000), endedAt: at(-3_600_000) })],
    });
    const never = runner({ name: 'c', apiKeyId: 'k3' });
    expect(sortRunners([older, never, newer], NOW).shown.map((r) => r.name)).toEqual(['a', 'b', 'c']);
  });

  it('breaks a tie by name', () => {
    const b = runner({ name: 'b', apiKeyId: 'k1' });
    const a = runner({ name: 'a', apiKeyId: 'k2' });
    expect(sortRunners([b, a], NOW).shown.map((r) => r.name)).toEqual(['a', 'b']);
  });

  it('splits out revoked runners and orders them the same way', () => {
    const revokedLive = runner({ name: 'b', apiKeyId: 'k1', revokedAt: at(-1000), sessions: [session({ id: 1 })] });
    const revokedQuiet = runner({ name: 'a', apiKeyId: 'k2', revokedAt: at(-1000) });
    const live = runner({ name: 'c', apiKeyId: 'k3', sessions: [session({ id: 2 })] });
    const result = sortRunners([revokedQuiet, live, revokedLive], NOW);
    expect(result.shown.map((r) => r.name)).toEqual(['c']);
    expect(result.revoked.map((r) => r.name)).toEqual(['b', 'a']);
  });

  it('drops retired and keyless rows from both lists', () => {
    const retired = runner({ name: 'a', apiKeyId: 'k1', retiredAt: at(-1000) });
    const keyless = runner({ name: 'b', apiKeyId: null });
    const revokedKeyless = runner({ name: 'c', apiKeyId: undefined, revokedAt: at(-1000) });
    const kept = runner({ name: 'd', apiKeyId: 'k2' });
    const result = sortRunners([retired, keyless, revokedKeyless, kept], NOW);
    expect(result.shown.map((r) => r.name)).toEqual(['d']);
    expect(result.revoked).toEqual([]);
  });

  it('does not mutate the input array', () => {
    const b = runner({ name: 'b', apiKeyId: 'k1' });
    const a = runner({ name: 'a', apiKeyId: 'k2' });
    const input = [b, a];
    sortRunners(input, NOW);
    expect(input).toEqual([b, a]);
  });
});

describe('sessionTokenBudget', () => {
  it('is empty when neither cap is set, even where a total was reported', () => {
    expect(sessionTokenBudget(session())).toEqual([]);
    expect(sessionTokenBudget(session({ tokensUsed: 300_000, tokensLastHour: 100_000 }))).toEqual([]);
  });

  it('says the night against its cap, with a percent and a tone, never (default)', () => {
    const lines = sessionTokenBudget(session({ maxTokens: 1_000_000, tokensUsed: 800_000 }));
    expect(lines).toEqual([{ text: '800k / 1M tokens', percent: 80, tone: 'warn' }]);
  });

  it('says the hour alone when only the hourly cap is set', () => {
    const lines = sessionTokenBudget(session({ maxTokensPerHour: 500_000, tokensLastHour: 50_000 }));
    expect(lines).toEqual([{ text: '50k / 500k per hour', percent: 10, tone: 'success' }]);
  });
});

describe('sessionsByStart', () => {
  it('orders live sessions by startedAt ascending', () => {
    const earlier = session({ id: 1, startedAt: at(-7_200_000) });
    const later = session({ id: 2, startedAt: at(-3_600_000) });
    expect(sessionsByStart([later, earlier]).map((s) => s.id)).toEqual([1, 2]);
  });

  it('breaks a tie on startedAt by where, null last', () => {
    const b = session({ id: 1, where: 'Mac:/b' });
    const a = session({ id: 2, where: 'Mac:/a' });
    const none = session({ id: 3, where: null });
    expect(sessionsByStart([b, none, a]).map((s) => s.id)).toEqual([2, 1, 3]);
  });

  it('does not mutate the input array', () => {
    const b = session({ id: 1, startedAt: at(-3_600_000) });
    const a = session({ id: 2, startedAt: at(-7_200_000) });
    const input = [b, a];
    sessionsByStart(input);
    expect(input).toEqual([b, a]);
  });
});

describe('recentSessions', () => {
  it('excludes a live session', () => {
    const live = session({ id: 1 });
    expect(recentSessions(runner({ sessions: [live] }), NOW)).toEqual([]);
  });

  it('includes an open session that has gone quiet, which liveSessions drops', () => {
    const quiet = session({ id: 1, lastSeenAt: at(-GONE_AFTER * 1000 - 1000) });
    expect(recentSessions(runner({ sessions: [quiet] }), NOW).map((s) => s.id)).toEqual([1]);
  });

  it('includes an ended session, newest startedAt first', () => {
    const older = session({ id: 1, endedAt: at(-7_200_000), startedAt: at(-7_200_000) });
    const newer = session({ id: 2, endedAt: at(-3_600_000), startedAt: at(-3_600_000) });
    expect(recentSessions(runner({ sessions: [older, newer] }), NOW).map((s) => s.id)).toEqual([2, 1]);
  });

  it('caps at 20', () => {
    const sessions = Array.from({ length: 25 }, (_, i) =>
      session({ id: i, endedAt: at(-i * 1000), startedAt: at(-i * 1000) }),
    );
    expect(recentSessions(runner({ sessions }), NOW)).toHaveLength(20);
  });
});

describe('sessionRepositoriesLabel', () => {
  it('is null for a session that has named none', () => {
    expect(sessionRepositoriesLabel(session())).toBeNull();
  });

  it('joins one or more remotes', () => {
    expect(sessionRepositoriesLabel(session({ remotes: ['example.com/owner/repo'] }))).toBe(
      'example.com/owner/repo',
    );
    expect(sessionRepositoriesLabel(session({ remotes: ['example.com/owner/one', 'example.com/owner/two'] }))).toBe(
      'example.com/owner/one, example.com/owner/two',
    );
  });

  it('names clones what it lacks only when the session says it does', () => {
    expect(sessionRepositoriesLabel(session({ remotes: ['example.com/owner/repo'], clones: true }))).toBe(
      'example.com/owner/repo, clones what it lacks',
    );
    expect(sessionRepositoriesLabel(session({ remotes: ['example.com/owner/repo'], clones: false }))).toBe(
      'example.com/owner/repo',
    );
  });
});

describe('the runner detail\'s two forms', () => {
  const held = () =>
    runner({
      under: 'AER-930',
      tags: ['gpu', 'fast'],
      maxRuns: 12,
      maxSpend: 40,
      maxTokens: 2_000_000,
      maxTokensPerHour: 500_000,
      maxTokensSource: 'runner',
      maxTokensPerHourSource: 'runner',
      untilAt: '2026-09-09T06:00:00Z',
    });

  it('seeds the settings from tags and under alone', () => {
    expect(settingsOf(held())).toEqual({ tags: 'gpu\nfast', under: 'AER-930' });
    expect(settingsOf(runner())).toEqual({ tags: '', under: '' });
  });

  it('seeds the ceilings from the five fields alone', () => {
    expect(ceilingsOf(held())).toEqual({
      maxRuns: '12',
      maxSpend: '40',
      maxTokens: '2,000,000',
      maxTokensPerHour: '500,000',
      untilAt: '2026-09-09T06:00:00Z',
    });
  });

  it('sends exactly tags and under from the settings form', () => {
    const req = settingsRequest({ tags: 'gpu\n\nfast ', under: ' AER-930 ' });
    expect(Object.keys(req).sort()).toEqual(['tags', 'under']);
    expect(req).toEqual({ tags: ['gpu', 'fast'], under: 'AER-930' });
    expect(settingsRequest({ tags: '', under: '' })).toEqual({ tags: [], under: '' });
  });

  it('sends exactly the five ceilings from the bounds form, the empty ones too', () => {
    const req = ceilingsRequest({ ...ceilingsOf(runner()), maxSpend: ' 40 ', maxTokens: '2,000,000' });
    expect(Object.keys(req).sort()).toEqual(['maxRuns', 'maxSpend', 'maxTokens', 'maxTokensPerHour', 'untilAt']);
    expect(req).toEqual({ maxRuns: '', maxSpend: '40', maxTokens: '2000000', maxTokensPerHour: '', untilAt: '' });
  });

  it('notices a ceiling changing, and a tags or under change is not one', () => {
    const r = held();
    expect(ceilingsChanged(ceilingsOf(r), r)).toBe(false);
    expect(ceilingsChanged({ ...ceilingsOf(r), maxRuns: '' }, r)).toBe(true);
    expect(ceilingsChanged({ ...ceilingsOf(r), maxTokens: '2M' }, r)).toBe(false);
  });

  it('notices tags or under changing, and a ceiling is not one', () => {
    const r = held();
    expect(settingsChanged(settingsOf(r), r)).toBe(false);
    expect(settingsChanged({ ...settingsOf(r), under: 'AER-1' }, r)).toBe(true);
    expect(settingsChanged({ ...settingsOf(r), under: ' AER-930 ' }, r)).toBe(false);
    expect(settingsChanged({ ...settingsOf(r), tags: 'fast\ngpu' }, r)).toBe(true);
    expect(settingsChanged({ ...settingsOf(r), tags: 'gpu\nfast\n' }, r)).toBe(false);
    expect(settingsChanged(settingsOf(runner()), runner())).toBe(false);
  });

  it('judges a bounds problem from the three fields it reads', () => {
    expect(boundsProblem({ untilAt: '2026-09-09', maxTokens: '', maxTokensPerHour: '' })).toMatch(/hour/);
    expect(boundsProblem({ untilAt: '', maxTokens: '', maxTokensPerHour: '' })).toBeNull();
  });
});

describe('what a runner\'s live sessions add up to and declare', () => {
  it('sums runs, spend and tokens, counting an unreported figure as none', () => {
    expect(sessionTotals([])).toEqual({ runs: 0, spent: 0, tokens: 0, tokensLastHour: 0 });
    expect(
      sessionTotals([
        session({ runs: 2, spent: 1.5, tokensUsed: 1000, tokensLastHour: 400 }),
        session({ runs: 3, spent: null, tokensUsed: undefined, tokensLastHour: 100 }),
        session({}),
      ]),
    ).toEqual({ runs: 5, spent: 1.5, tokens: 1000, tokensLastHour: 500 });
  });

  it('unions the declared projects, sorted and once each', () => {
    expect(declaredProjects([])).toEqual([]);
    expect(
      declaredProjects([session({ projects: ['b', 'a'] }), session({ projects: ['a', 'c'] }), session({})]),
    ).toEqual(['a', 'b', 'c']);
  });

  it('reads --dangerous as a Yes where the session does not refuse it, and skips one that never said', () => {
    expect(
      declaredDangerous([
        session({ where: 'Mac:/one', refusesDangerous: false }),
        session({ where: 'Mac:/two', refusesDangerous: true }),
        session({ where: 'Mac:/three', refusesDangerous: null }),
        session({ where: 'Mac:/four' }),
      ]),
    ).toEqual([
      { where: 'Mac:/one', dangerous: true },
      { where: 'Mac:/two', dangerous: false },
    ]);
  });
});
