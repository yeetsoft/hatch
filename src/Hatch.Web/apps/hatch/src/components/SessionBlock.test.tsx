import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { SessionBlock } from './SessionBlock';
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
  host: 'Mac',
  state: 'running',
  startedAt: at(-3_600_000),
  lastSeenAt: at(0),
  endedAt: null,
  claimKey: 'HA-12',
  line: 'reading the brief',
  agent: 'claude',
  os: 'macOS',
  exhaustedUntil: null,
  throttledUntil: null,
  arch: 'arm64',
  agentVersion: '5.1',
  username: 'ada',
  ip: '10.0.0.1',
  container: true,
  processStartedAt: at(-3_600_000),
  revision: 'a1b2c3d4e5f60718293a4b5c6d7e8f9011121314',
  builtAt: at(-3 * 3_600_000),
  projects: ['HA'],
  refusesDangerous: false,
  remotes: ['example.com/owner/repo'],
  clones: false,
  runs: 3,
  spent: 1.5,
  ...over,
});

const draw = (session_: RunnerSession, runner_: Runner = runner()) =>
  renderToStaticMarkup(
    <MemoryRouter>
      <ul>
        <SessionBlock session={session_} runner={runner_} now={NOW} />
      </ul>
    </MemoryRouter>,
  );

describe('SessionBlock', () => {
  it('shows every Machine label and value for a full session', () => {
    const html = draw(session());

    expect(html).toContain('Host');
    expect(html).toContain('Mac');
    expect(html).toContain('OS');
    expect(html).toContain('macOS');
    expect(html).toContain('arm64');
    expect(html).toContain('Agent');
    expect(html).toContain('claude');
    expect(html).toContain('5.1');
    expect(html).toContain('CLI build');
    expect(html).toContain('Username');
    expect(html).toContain('ada');
    expect(html).toContain('IP');
    expect(html).toContain('10.0.0.1');
    expect(html).toContain('as the server saw it');
    expect(html).toContain('Container');
    expect(html).toContain('Started');
    expect(html).toContain('Projects');
    expect(html).toContain('HA');
    expect(html).toContain('Dangerous');
    expect(html).toContain('Repositories');
    expect(html).toContain('example.com/owner/repo');
    expect(html).toContain('Drives');
  });

  it('omits rows for an older CLI that never reported them, but still shows a set ip', () => {
    const html = draw(
      session({ os: null, arch: null, agentVersion: null, container: null, username: null, processStartedAt: null }),
    );

    expect(html).not.toContain('OS');
    expect(html).not.toContain('Username');
    expect(html).not.toContain('Container');
    expect(html).not.toContain('Started');
    expect(html).toContain('IP');
    expect(html).toContain('10.0.0.1');
  });

  it('draws No for a false container and no row at all for a null one', () => {
    expect(draw(session({ container: false }))).toMatch(/Container\s*No/);
    expect(draw(session({ container: null }))).not.toContain('Container');
  });

  it('draws the Machine details closed', () => {
    const html = draw(session());
    expect(html).toContain('<details>');
    expect(html).not.toContain('<details open');
  });

  it('links the claim key to its issue', () => {
    const html = draw(session({ claimKey: 'HA-12' }));
    expect(html).toMatch(/<a[^>]*href="\/issues\/HA-12"[^>]*>HA-12<\/a>/);
  });

  it('draws nothing yet with no claim', () => {
    const html = draw(session({ claimKey: null, line: null }));
    expect(html).toContain('nothing yet');
  });
});
