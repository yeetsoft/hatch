import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { RecentSessions } from './RecentSessions';
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
  os: 'macOS',
  exhaustedUntil: null,
  throttledUntil: null,
  ...over,
});

describe('RecentSessions', () => {
  it('draws nothing for a runner with no recent sessions', () => {
    expect(renderToStaticMarkup(<RecentSessions runner={runner()} now={NOW} />)).toBe('');
  });

  it('excludes a live session', () => {
    const html = renderToStaticMarkup(<RecentSessions runner={runner({ sessions: [session({ id: 1 })] })} now={NOW} />);
    expect(html).toBe('');
  });

  it('draws newest first', () => {
    const older = session({ id: 1, startedAt: at(-7_200_000), endedAt: at(-7_200_000), where: 'Mac:/older' });
    const newer = session({ id: 2, startedAt: at(-3_600_000), endedAt: at(-3_600_000), where: 'Mac:/newer' });
    const html = renderToStaticMarkup(<RecentSessions runner={runner({ sessions: [older, newer] })} now={NOW} />);

    expect(html.indexOf('Mac:/newer')).toBeLessThan(html.indexOf('Mac:/older'));
  });
});
