import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { RunnersLayout, RunnersIndex } from './RunnersLayout';
import { RunnerDetail } from './RunnerDetail';
import type { Runner, RunnerSession } from '../types';

const NOW = new Date('2026-09-08T12:00:00Z');

const at = (msFromNow: number) => new Date(NOW.getTime() + msFromNow).toISOString();

const GONE_AFTER = 90;

// Copied from RunnerList.test.tsx's own factory, per the brief: a test never
// imports a factory out of another test file.
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

const live = (name: string, over: Partial<Runner> = {}) => runner({ name, sessions: [session()], ...over });

const QUILL = live('Quill');

const state = vi.hoisted(() => ({
  read: { runners: null as Runner[] | null, error: null as string | null },
  phone: false,
  isAdmin: true,
}));
vi.mock('../lib/useRunners', () => ({ useRunners: () => state.read }));
vi.mock('../lib/viewport', () => ({ usePhone: () => state.phone }));
vi.mock('../lib/useMe', () => ({
  useMe: () => ({ me: { kind: 'person', name: 'a', role: state.isAdmin ? 'admin' : 'user' }, isAdmin: state.isAdmin }),
}));

// Copied from pages/DocsPage.test.tsx's own helper, nested the same way App.tsx nests the runners routes.
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

describe('RunnersLayout', () => {
  beforeEach(() => {
    state.isAdmin = true;
  });

  it('desk: /runners draws the list beside the empty detail', () => {
    state.read = { runners: [QUILL], error: null };
    const html = renderAt('/runners');
    expect(html).toContain('Quill');
    expect(html).toContain('Pick a runner to see its sessions and settings.');
  });

  it('desk: /runners/Quill draws the selected row and the detail header', () => {
    state.read = { runners: [QUILL], error: null };
    const html = renderAt('/runners/Quill');
    expect(html).toMatch(/aria-current="page"/);
    expect(html).toMatch(/<h2[^>]*>Quill<\/h2>/);
  });

  it('an unknown name draws the not-found state', () => {
    state.read = { runners: [QUILL], error: null };
    const html = renderAt('/runners/Nobody');
    expect(html).toContain('No runner called Nobody.');
  });

  it('a revoked runner resolves to its header, not the not-found state', () => {
    const revoked = live('Gone-One', { revokedAt: at(-1000) });
    state.read = { runners: [revoked], error: null };
    const html = renderAt('/runners/Gone-One');
    expect(html).toMatch(/<h2[^>]*>Gone-One<\/h2>/);
    expect(html).not.toContain('No runner called');
  });

  it('a name with a slash resolves, proving the param is decoded once', () => {
    const slashed = live('here:/checkouts/one');
    state.read = { runners: [slashed], error: null };
    const html = renderAt(`/runners/${encodeURIComponent('here:/checkouts/one')}`);
    expect(html).toMatch(/<h2[^>]*>here:\/checkouts\/one<\/h2>/);
    expect(html).not.toContain('No runner called');
  });

  it('phone: /runners draws the h1 and the list, not the empty-detail message', () => {
    state.read = { runners: [QUILL], error: null };
    const html = renderAt('/runners', true);
    expect(html).toMatch(/<h1[^>]*>Runners<\/h1>/);
    expect(html).toContain('Quill');
    expect(html).not.toContain('Pick a runner to see its sessions and settings.');
  });

  it('phone: /runners/Quill draws the h1, the back link, and not the list', () => {
    const other = live('Other');
    state.read = { runners: [QUILL, other], error: null };
    const html = renderAt('/runners/Quill', true);
    expect(html).toMatch(/<h1[^>]*>Quill<\/h1>/);
    expect(html).toContain('‹');
    expect(html).not.toContain('Other');
  });

  it('runners: null draws neither the list nor the not-found state', () => {
    state.read = { runners: null, error: null };
    const html = renderAt('/runners/Quill');
    expect(html).not.toContain('hatch-runner-list');
    expect(html).not.toContain('No runner called');
  });

  it('a read error draws above the list', () => {
    state.read = { runners: [QUILL], error: 'the runners could not be read' };
    const html = renderAt('/runners');
    expect(html).toMatch(/text-danger[^>]*>the runners could not be read/);
  });
});

describe('RunnersLayout: Create runner', () => {
  beforeEach(() => {
    state.isAdmin = true;
  });

  it('an admin sees Create runner in the desk list header', () => {
    state.read = { runners: [QUILL], error: null };
    expect(renderAt('/runners')).toContain('>Create runner</button>');
  });

  it('an admin sees Create runner in the phone list header', () => {
    state.read = { runners: [QUILL], error: null };
    expect(renderAt('/runners', true)).toContain('>Create runner</button>');
  });

  it('an admin does not see Create runner at the phone detail route', () => {
    state.read = { runners: [QUILL], error: null };
    expect(renderAt('/runners/Quill', true)).not.toContain('Create runner');
  });

  it('a non-admin sees no Create runner button', () => {
    state.isAdmin = false;
    state.read = { runners: [QUILL], error: null };
    expect(renderAt('/runners')).not.toContain('Create runner');
  });
});
