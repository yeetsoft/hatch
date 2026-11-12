import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom';
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

const state = vi.hoisted(() => ({
  read: { runners: null as Runner[] | null, error: null as string | null },
  phone: false,
  me: { kind: 'local' as 'local' | 'person' | 'none' | 'key', name: 'a', role: 'admin' as 'admin' | 'user' | null },
  isAdmin: true,
}));
vi.mock('../lib/useRunners', () => ({ useRunners: () => state.read }));
vi.mock('../lib/viewport', () => ({ usePhone: () => state.phone }));
vi.mock('../lib/useMe', () => ({ useMe: () => ({ me: state.me, isAdmin: state.isAdmin }) }));

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

beforeEach(() => {
  state.isAdmin = true;
  state.me = { kind: 'person', name: 'a', role: 'admin' };
});

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

  it('draws the owner as text for an owner or a viewer: for {name}, or no owner', () => {
    state.isAdmin = false;
    state.me = { kind: 'person', name: 'a', role: 'user' };
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

  it('draws an agent-down session with its own glyph and words', () => {
    const down = live({
      id: 1,
      agentDownUntil: new Date(Date.now() + 3_600_000).toISOString(),
      agentDownKind: 'signed-out',
    });
    state.read = { runners: [runner({ name: 'Quill', sessions: [down] })], error: null };
    const html = renderAt('/runners/Quill');
    expect(html).toContain('hatch-runner-glyph--agent-down');
    expect(html).toContain('Agent needs signing in');
  });
});

describe('RunnerDetail: Pause all and Stop all', () => {
  const soon = (msFromNow: number) => new Date(Date.now() + msFromNow).toISOString();
  const live = (over: Partial<RunnerSession> = {}) => session({ lastSeenAt: soon(0), ...over });
  const withSessions = (over: Partial<Runner>, sessions: RunnerSession[]) => {
    state.read = { runners: [runner({ name: 'Quill', sessions, ...over })], error: null };
    return renderAt('/runners/Quill');
  };

  it('draws both in the heading row for an owner with a live loop session', () => {
    const html = withSessions({}, [live()]);

    expect(html).toContain('>Pause all</button>');
    expect(html).toContain('>Stop all</button>');
    expect(html.indexOf('Live sessions')).toBeLessThan(html.indexOf('Pause all'));
    expect(html.indexOf('Pause all')).toBeLessThan(html.indexOf('hatch-runner-sessions'));
  });

  it('draws both on the phone too', () => {
    state.read = { runners: [runner({ name: 'Quill', sessions: [live()] })], error: null };
    const html = renderAt('/runners/Quill', true);

    expect(html).toContain('>Pause all</button>');
    expect(html).toContain('>Stop all</button>');
  });

  it('draws neither with no live session, or with only a once session', () => {
    for (const html of [withSessions({}, []), withSessions({}, [live({ kind: 'once' })])]) {
      expect(html).not.toContain('Pause all');
      expect(html).not.toContain('Stop all');
    }
  });

  it('draws neither on a revoked runner', () => {
    const html = withSessions({ revokedAt: at(-1000) }, [live()]);

    expect(html).not.toContain('Pause all');
    expect(html).not.toContain('Stop all');
  });

  it('says whose it is to control to a viewer, and draws no button in the card', () => {
    const html = withSessions({ canControl: false, for: 'Ada' }, [live()]);
    const card = html.slice(html.indexOf('Live sessions'));

    expect(card).toContain("Ada&#x27;s to control");
    expect(card).not.toContain('<button');
  });

  it('falls back to its owner for a viewer on a runner with no owner', () => {
    expect(withSessions({ canControl: false, for: null }, [live()])).toContain('its owner&#x27;s to control');
  });

  it('draws no to-control line for an owner', () => {
    expect(withSessions({ for: 'Ada' }, [live()])).not.toContain("to control");
  });

  it('keeps the Stop all modal shut until pressed', () => {
    expect(withSessions({}, [live(), live({ id: 2 })])).not.toContain('after their current increment');
  });
});

describe('RunnerDetail: What it may work and Bounds', () => {
  const draws = (html: string) => ({
    settings: html.includes('Save settings'),
    bounds: html.includes('Save bounds'),
  });

  it('draws both editable cards for an owner, between Identity and Live sessions', () => {
    state.read = { runners: [runner({ name: 'Quill' })], error: null };
    const html = renderAt('/runners/Quill');
    expect(draws(html)).toEqual({ settings: true, bounds: true });
    expect(html.indexOf('Identity and key')).toBeLessThan(html.indexOf('What it may work'));
    expect(html.indexOf('What it may work')).toBeLessThan(html.indexOf('Bounds</h2>'));
    expect(html.indexOf('Bounds</h2>')).toBeLessThan(html.indexOf('Live sessions'));
  });

  it('draws both on the phone too', () => {
    state.read = { runners: [runner({ name: 'Quill' })], error: null };
    expect(draws(renderAt('/runners/Quill', true))).toEqual({ settings: true, bounds: true });
  });

  it('draws neither Save for a viewer, but still the values', () => {
    state.read = { runners: [runner({ name: 'Quill', canControl: false, tags: ['gpu'] })], error: null };
    const html = renderAt('/runners/Quill');
    expect(draws(html)).toEqual({ settings: false, bounds: false });
    expect(html).toContain('What it may work');
    expect(html).toContain('Tags: gpu');
    expect(html).not.toContain('<textarea');
  });

  it('draws neither Save on a revoked runner, even for an owner', () => {
    state.read = { runners: [runner({ name: 'Quill', revokedAt: at(-1000) })], error: null };
    expect(draws(renderAt('/runners/Quill'))).toEqual({ settings: false, bounds: false });
  });

  it('draws neither Save on a retired runner', () => {
    state.read = { runners: [runner({ name: 'Quill', retiredAt: at(-1000), supersededBy: 'Other' })], error: null };
    expect(draws(renderAt('/runners/Quill'))).toEqual({ settings: false, bounds: false });
  });

  it('draws Under as an input in What it may work and only the echo in Bounds', () => {
    state.read = { runners: [runner({ name: 'Quill', under: 'AER-930' })], error: null };
    const html = renderAt('/runners/Quill');
    const bounds = html.indexOf('Bounds</h2>');
    expect(html.slice(0, bounds)).toMatch(/<input[^>]*value="AER-930"/);
    expect(html.slice(bounds)).not.toMatch(/<input[^>]*value="AER-930"/);
    expect(html.slice(bounds)).toContain('Also held under');
  });
});

describe('IdentityCard', () => {
  it('draws a refusal inside the card', () => {
    const html = renderToStaticMarkup(<IdentityCard runner={runner({ name: 'Quill' })} refusal="could not save" />);
    expect(html).toMatch(/text-danger[^>]*>could not save/);
  });
});

describe('RunnerDetail: the owner select, Rotate and Revoke', () => {
  it('an admin sees them on an actionable runner', () => {
    state.read = { runners: [runner({ name: 'Quill' })], error: null };
    const html = renderAt('/runners/Quill');
    expect(html).toContain('aria-label="Owner of Quill"');
    expect(html).toContain('>Rotate key</button>');
    expect(html).toContain('>Revoke</button>');
  });

  it('a non-admin sees none of them', () => {
    state.isAdmin = false;
    state.me = { kind: 'person', name: 'a', role: 'user' };
    state.read = { runners: [runner({ name: 'Quill' })], error: null };
    const html = renderAt('/runners/Quill');
    expect(html).not.toContain('aria-label="Owner of Quill"');
    expect(html).not.toContain('Rotate key');
    expect(html).not.toContain('>Revoke<');
  });

  it('wall off sees none of them', () => {
    state.isAdmin = false;
    state.me = { kind: 'local', name: 'a', role: null };
    state.read = { runners: [runner({ name: 'Quill' })], error: null };
    const html = renderAt('/runners/Quill');
    expect(html).not.toContain('aria-label="Owner of Quill"');
    expect(html).not.toContain('Rotate key');
    expect(html).not.toContain('>Revoke<');
  });

  it('a revoked runner has none even for an admin', () => {
    state.read = { runners: [runner({ name: 'Quill', revokedAt: at(-1000) })], error: null };
    const html = renderAt('/runners/Quill');
    expect(html).not.toContain('aria-label="Owner of Quill"');
    expect(html).not.toContain('Rotate key');
    expect(html).not.toContain('>Revoke<');
    // Read-only, as an Owner or a Viewer would see it.
    expect(html).toContain('no owner');
  });
});

describe('RunnerDetail: the secret panel', () => {
  // A harness that supplies the outlet context directly, since the secret
  // lives in RunnersLayout's own useState and this proves RunnerDetail's own
  // rule: drawn only when the held secret names the runner on screen.
  function renderWithSecret(path: string, secret: { name: string; secret: string; rotated: boolean } | null) {
    const outlet = { runners: [runner({ name: 'Quill' }), runner({ name: 'Other' })], secret, setSecret: () => {}, reload: async () => {} };
    return renderToStaticMarkup(
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/runners" element={<Outlet context={outlet} />}>
            <Route path=":name" element={<RunnerDetail />} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
  }

  it('draws on the matching runner', () => {
    const html = renderWithSecret('/runners/Quill', { name: 'Quill', secret: 'hatch_ak_abc', rotated: false });
    expect(html).toContain('New secret for Quill');
  });

  it('draws nothing on a different runner', () => {
    const html = renderWithSecret('/runners/Other', { name: 'Quill', secret: 'hatch_ak_abc', rotated: false });
    expect(html).not.toContain('New secret for');
  });

  it('draws nothing with no secret held', () => {
    const html = renderWithSecret('/runners/Quill', null);
    expect(html).not.toContain('New secret for');
  });
});
