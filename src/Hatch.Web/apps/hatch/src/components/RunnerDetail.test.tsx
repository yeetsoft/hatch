import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { RunnersLayout, RunnersIndex } from './RunnersLayout';
import { RunnerDetail, IdentityCard } from './RunnerDetail';
import type { Runner, RunnerSession } from '../types';

const NOW = new Date('2026-09-08T12:00:00Z');

const at = (msFromNow: number) => new Date(NOW.getTime() + msFromNow).toISOString();

const GONE_AFTER = 90;

// Copied from RunnersLayout.test.tsx's own factory, per the brief: a test
// never imports a factory out of another test file.
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
  apiKeyId: 'k1',
  prefix: 'hatch_ak_abc',
  createdAt: at(-7_200_000),
  lastUsedAt: null,
  rotatedAt: null,
  revokedAt: null,
  ...over,
});

// Never imported from runners.test.ts, per the brief: a test factory stays local.
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

const state = vi.hoisted(() => ({ read: { runners: null as Runner[] | null, error: null as string | null }, phone: false }));
vi.mock('../lib/useRunners', () => ({ useRunners: () => state.read }));
vi.mock('../lib/viewport', () => ({ usePhone: () => state.phone }));
vi.mock('../lib/useMe', () => ({ useMe: () => ({ me: { kind: 'person', name: 'a', role: 'admin' }, isAdmin: true }) }));

// Nested the same way App.tsx nests the runners routes - see RunnersLayout.test.tsx.
function renderAt(path: string, phone = false): string {
  state.phone = phone;
  return renderToStaticMarkup(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/runners" element={<RunnersLayout />}>
          <Route index element={<RunnersIndex />} />
          <Route path=":name" element={<RunnerDetail />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('RunnerDetail', () => {
  it('draws the identity facts: heading, name, prefix, created, last used', () => {
    state.read = { runners: [runner({ name: 'Quill' })], error: null };
    const html = renderAt('/runners/Quill');
    expect(html).toContain('Identity and key');
    expect(html).toContain('Quill');
    expect(html).toMatch(/<code>hatch_ak_abc<\/code>/);
    expect(html).toContain('Created');
    expect(html).toContain('Last used never');
  });

  it('omits rotated and revoked when unset, and draws them when set', () => {
    state.read = { runners: [runner({ name: 'Quill' })], error: null };
    expect(renderAt('/runners/Quill')).not.toContain('Rotated');
    expect(renderAt('/runners/Quill')).not.toContain('Revoked');

    state.read = { runners: [runner({ name: 'Quill', rotatedAt: at(-1000), revokedAt: at(-500) })], error: null };
    const html = renderAt('/runners/Quill');
    expect(html).toContain('Rotated');
    expect(html).toContain('Revoked');
  });

  it('draws Behind only when boardDrift is Behind', () => {
    state.read = { runners: [runner({ name: 'Quill', boardDrift: 'Behind' })], error: null };
    expect(renderAt('/runners/Quill')).toContain('Behind');

    state.read = { runners: [runner({ name: 'Quill', boardDrift: 'Unknown' })], error: null };
    expect(renderAt('/runners/Quill')).not.toContain('Behind');
  });

  it('draws Rebuild only when sourceDrift is Behind', () => {
    state.read = { runners: [runner({ name: 'Quill', sourceDrift: 'Behind' })], error: null };
    expect(renderAt('/runners/Quill')).toContain('Rebuild');

    state.read = { runners: [runner({ name: 'Quill', sourceDrift: 'Unknown' })], error: null };
    expect(renderAt('/runners/Quill')).not.toContain('Rebuild');
  });

  it('draws neither badge when both drifts are Unknown', () => {
    state.read = { runners: [runner({ name: 'Quill' })], error: null };
    const html = renderAt('/runners/Quill');
    expect(html).not.toContain('Behind');
    expect(html).not.toContain('Rebuild');
  });

  it('draws the owner as text: for {name}, or no owner', () => {
    state.read = { runners: [runner({ name: 'Quill', for: 'Ada' })], error: null };
    expect(renderAt('/runners/Quill')).toContain('for Ada');

    state.read = { runners: [runner({ name: 'Quill', for: null })], error: null };
    expect(renderAt('/runners/Quill')).toContain('no owner');
  });
});

describe('RunnerDetail: Live sessions', () => {
  // RunnerDetail reads the real clock rather than a fixture (its own "no now
  // of its own" rule), so a session meant to read as live is timed off
  // Date.now() instead of the fixed NOW above.
  const soon = (msFromNow: number) => new Date(Date.now() + msFromNow).toISOString();
  const live = (over: Partial<RunnerSession> = {}) => session({ lastSeenAt: soon(0), ...over });

  it('says No live sessions with none, naming hatch go-to-work', () => {
    state.read = { runners: [runner({ name: 'Quill' })], error: null };
    const html = renderAt('/runners/Quill');
    expect(html).toContain('No live sessions');
    expect(html).toContain('<code>hatch go-to-work</code>');
  });

  it('draws a multi-host note for live sessions spanning two or more distinct hosts', () => {
    state.read = {
      runners: [runner({ name: 'Quill', sessions: [live({ id: 1, host: 'Mac' }), live({ id: 2, host: 'Linux' })] })],
      error: null,
    };
    expect(renderAt('/runners/Quill')).toContain('2 machines');
  });

  it('draws no note for one host', () => {
    state.read = { runners: [runner({ name: 'Quill', sessions: [live({ id: 1, host: 'Mac' })] })], error: null };
    expect(renderAt('/runners/Quill')).not.toContain('hatch-runner-note');
  });

  it('draws no note for two sessions on one host', () => {
    state.read = {
      runners: [runner({ name: 'Quill', sessions: [live({ id: 1, host: 'Mac' }), live({ id: 2, host: 'Mac' })] })],
      error: null,
    };
    expect(renderAt('/runners/Quill')).not.toContain('hatch-runner-note');
  });

  it('draws live session blocks in startedAt order', () => {
    const later = live({ id: 1, where: 'Mac:/later', startedAt: soon(-60_000) });
    const earlier = live({ id: 2, where: 'Mac:/earlier', startedAt: soon(-3_600_000) });
    state.read = { runners: [runner({ name: 'Quill', sessions: [later, earlier] })], error: null };
    const html = renderAt('/runners/Quill');
    expect(html.indexOf('Mac:/earlier')).toBeLessThan(html.indexOf('Mac:/later'));
  });
});

describe('IdentityCard', () => {
  it('draws a refusal inside the card', () => {
    const html = renderToStaticMarkup(<IdentityCard runner={runner({ name: 'Quill' })} refusal="could not save" />);
    expect(html).toMatch(/text-danger[^>]*>could not save/);
  });
});
