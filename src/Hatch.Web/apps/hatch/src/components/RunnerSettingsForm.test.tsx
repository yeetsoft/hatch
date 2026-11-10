import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { RunnerSettingsForm } from './RunnerSettingsForm';
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

const render = (r: Runner, over: { live?: RunnerSession[]; editable?: boolean; refusal?: string | null } = {}) =>
  renderToStaticMarkup(
    <MemoryRouter>
      <RunnerSettingsForm
        runner={r}
        live={over.live ?? []}
        editable={over.editable ?? true}
        refusal={over.refusal ?? null}
        onSave={() => {}}
      />
    </MemoryRouter>,
  );

describe('RunnerSettingsForm', () => {
  it('draws Tags and Under as inputs with Save settings, and no ceiling', () => {
    const html = render(runner({ tags: ['gpu', 'fast'], under: 'AER-930' }));
    expect(html).toContain('What it may work');
    expect(html).toContain('Save settings');
    expect(html).toContain('Revert');
    expect(html).toContain('<textarea');
    expect(html).toContain('gpu\nfast');
    expect(html).toMatch(/<input[^>]*value="AER-930"/);
    expect(html).toContain('href="/issues/AER-930"');
    expect(html).not.toContain('Max runs');
    expect(html).not.toContain('Until');
    expect(html).not.toContain('Save bounds');
  });

  it('links Under only while it is shaped like an issue key', () => {
    expect(render(runner({ under: 'not an epic!' }))).not.toContain('/issues/');
    expect(render(runner({ under: 'AER-930' }))).toContain('href="/issues/AER-930"');
  });

  it('leaves Save disabled until something changes', () => {
    expect(render(runner())).toMatch(/<button[^>]*disabled[^>]*>Save settings/);
  });

  it('draws any refusal in a text-danger line before the save row, whatever it names', () => {
    for (const refusal of ['an epic called NOPE does not exist', 'something about --max-tokens-per-hour']) {
      const html = render(runner(), { refusal });
      const at = html.indexOf(refusal);
      expect(at).toBeGreaterThan(-1);
      expect(html.slice(html.lastIndexOf('<p', at), at)).toContain('text-danger');
      expect(at).toBeLessThan(html.indexOf('hatch-form-actions'));
    }
  });

  it('draws no refusal when there is none', () => {
    expect(render(runner())).not.toContain('text-danger');
  });

  it('draws what its sessions declare: projects, Dangerous per session, and the closing sentence', () => {
    const html = render(runner(), {
      live: [
        session({ id: 1, where: 'Mac:/one', projects: ['b', 'a'], refusesDangerous: false }),
        session({ id: 2, where: 'Mac:/two', projects: ['a'], refusesDangerous: true }),
      ],
    });
    expect(html).toContain('Declared by its sessions');
    expect(html).toContain('Projects: a, b');
    expect(html).toContain('Does not change the merge train');
    expect(html).toContain('Dangerous on Mac:/one: Yes');
    expect(html).toContain('Dangerous on Mac:/two: No');
    expect(html).toContain('Not editable here.');
  });

  it('says every project when its sessions declare none', () => {
    expect(render(runner())).toContain('Projects: every project');
  });

  it('draws read-only text with no input, textarea or button when not editable', () => {
    const html = render(runner({ tags: ['gpu', 'fast'], under: 'AER-930' }), { editable: false });
    expect(html).not.toContain('<input');
    expect(html).not.toContain('<textarea');
    expect(html).not.toContain('<button');
    expect(html).toContain('Tags: gpu, fast');
    expect(html).toContain('href="/issues/AER-930"');
  });

  it('reads an unrestricted runner in words when not editable', () => {
    const html = render(runner(), { editable: false });
    expect(html).toContain('Tags: any runner');
    expect(html).toContain('Under: the whole board');
  });
});
