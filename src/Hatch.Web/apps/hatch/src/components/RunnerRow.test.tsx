import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { RunnerRow } from './RunnerRow';
import type { Runner, RunnerSession } from '../types';

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
  refusedModels: [],
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
  apiKeyId: 'k1',
  ...over,
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

const draw = (r: Runner, selected = false) =>
  renderToStaticMarkup(
    <MemoryRouter>
      <RunnerRow runner={r} selected={selected} now={NOW} />
    </MemoryRouter>,
  );

describe('RunnerRow', () => {
  it('links to the runner by its encoded name', () => {
    const html = draw(runner({ name: 'mac:/a b/c', sessions: [session()] }));

    expect(html).toContain(`href="/runners/${encodeURIComponent('mac:/a b/c')}"`);
    expect(html).toContain('href="/runners/mac%3A%2Fa%20b%2Fc"');
  });

  it('is the current page only when selected', () => {
    expect(draw(runner({ sessions: [session()] }), true)).toContain('aria-current="page"');
    expect(draw(runner({ sessions: [session()] }), false)).not.toContain('aria-current');
  });

  it('badges the count of live sessions when there is more than one', () => {
    const two = draw(runner({ sessions: [session({ id: 1 }), session({ id: 2 })] }));
    const one = draw(runner({ sessions: [session({ id: 1 })] }));

    expect(two).toContain('>x2</span>');
    expect(one).not.toMatch(/>x\d+</);
  });

  it('badges a revoked runner, and only that one', () => {
    expect(draw(runner({ revokedAt: at(-1000), sessions: [session()] }))).toContain('>revoked</span>');
    expect(draw(runner({ sessions: [session()] }))).not.toContain('revoked');
  });

  it('labels the glyph with what the speaking session is doing', () => {
    expect(draw(runner({ sessions: [session({ claimKey: 'AER-1' })] }))).toContain('aria-label="Working"');
    expect(draw(runner({ sessions: [session({ state: 'paused' })] }))).toContain('aria-label="Paused"');
  });

  it('labels a runner that went missing Gone, and one that never started Not started', () => {
    const quiet = session({ lastSeenAt: at(-GONE_AFTER * 1000 - 1000) });

    expect(draw(runner({ sessions: [quiet] }))).toContain('aria-label="Gone"');
    expect(draw(runner({ sessions: [] }))).toContain('aria-label="Not started"');
    expect(draw(runner({}))).toContain('aria-label="Not started"');
  });

  it('says what the runner is doing on line two, with the same words in its title', () => {
    const html = draw(runner({ sessions: [session({ claimKey: 'AER-1', line: 'reading the brief' })] }));

    expect(html).toContain('title="AER-1 - reading the brief"');
    expect(html).toContain('>AER-1 - reading the brief</span>');
  });

  it('says whose it is and where it runs on line three, with the same words in its title', () => {
    const html = draw(runner({ for: 'sam', sessions: [session({ host: 'mac-mini' })] }));

    expect(html).toContain('title="sam · mac-mini"');
    expect(html).toContain('>sam · mac-mini</span>');
  });

  it('names the image it was built from on line three', () => {
    const html = draw(runner({ for: 'sam', sessions: [session({ host: 'mac-mini', image: 'ghcr.io/o/r:1' })] }));

    expect(html).toContain('sam · mac-mini · ghcr.io/o/r:1');
  });
});
