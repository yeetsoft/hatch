import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { RunnerBoundsForm } from './RunnerBoundsForm';
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
      <RunnerBoundsForm
        runner={r}
        live={over.live ?? []}
        editable={over.editable ?? true}
        refusal={over.refusal ?? null}
        onSave={() => {}}
      />
    </MemoryRouter>,
  );

describe('RunnerBoundsForm', () => {
  it('draws the five ceilings as inputs with Save bounds, and no Tags or Under input', () => {
    const html = render(runner({ under: 'AER-930', tags: ['gpu'] }));
    expect(html).toContain('Ceilings. A session&#x27;s own flags narrow these and never raise them.');
    for (const label of ['Max runs', 'Max spend', 'Max tokens this run', 'Max tokens per hour', 'Until']) {
      expect(html).toContain(label);
    }
    expect(html).toContain('Save bounds');
    expect(html).toContain('Revert');
    expect(html).not.toContain('<textarea');
    expect(html).not.toContain('Tags');
    expect(html).not.toContain('Save settings');
    // Under is echoed, linked, never an input here.
    expect(html).toContain('Also held under');
    expect(html).not.toMatch(/<input[^>]*value="AER-930"/);
  });

  it('echoes no under when the runner has none', () => {
    expect(render(runner())).not.toContain('Also held under');
  });

  it('draws a sum line over live sessions, and none without any', () => {
    expect(render(runner())).not.toContain('this hour');

    const html = render(runner(), {
      live: [
        session({ id: 1, runs: 2, spent: 1.5, tokensUsed: 1_000_000, tokensLastHour: 200_000 }),
        session({ id: 2, runs: 1, spent: 0.5, tokensUsed: 500_000, tokensLastHour: 100_000 }),
      ],
    });
    expect(html).toContain('3 runs');
    expect(html).toContain('$2.00');
    expect(html).toContain('1.5M tokens');
    expect(html).toContain('300k this hour');
  });

  it('draws a refusal naming a token flag with its field, not above the buttons', () => {
    const refusal = '--max-tokens-per-hour takes a token count';
    const html = render(runner(), { refusal });
    const at = html.indexOf(refusal);
    expect(at).toBeGreaterThan(html.indexOf('Max tokens per hour'));
    expect(at).toBeLessThan(html.indexOf('hatch-form-actions'));
    expect(html).not.toMatch(/<p class="text-danger"[^>]*>--max-tokens-per-hour/);
    expect(html.slice(html.indexOf('Max tokens this run'), html.indexOf('Max tokens per hour'))).not.toContain(refusal);
  });

  it('draws a refusal naming --max-tokens with the per-run field', () => {
    const refusal = '--max-tokens is not a count';
    const html = render(runner(), { refusal });
    const at = html.indexOf(refusal);
    expect(at).toBeGreaterThan(html.indexOf('Max tokens this run'));
    expect(at).toBeLessThan(html.indexOf('Max tokens per hour'));
  });

  it('draws any other refusal in a text-danger line before the save row', () => {
    const refusal = 'an until in the past stops nothing';
    const html = render(runner(), { refusal });
    const at = html.indexOf(refusal);
    expect(html.slice(html.lastIndexOf('<p', at), at)).toContain('text-danger');
    expect(at).toBeGreaterThan(html.indexOf('Until'));
    expect(at).toBeLessThan(html.indexOf('hatch-form-actions'));
  });

  it('puts (default) beside a label that inherits the site default, and only that one', () => {
    const html = render(
      runner({ maxTokens: 1_000_000, maxTokensSource: 'default', maxTokensPerHour: null, maxTokensPerHourSource: 'none' }),
    );
    expect(html).toMatch(/Max tokens this run<span[^>]*> \(default\)<\/span>/);
    expect(html).not.toMatch(/Max tokens per hour<span[^>]*> \(default\)/);
  });

  it('draws read-only figures with no input, textarea or button when not editable', () => {
    const html = render(
      runner({
        maxRuns: 12,
        maxSpend: 40,
        maxTokens: 2_000_000,
        maxTokensSource: 'runner',
        maxTokensPerHour: null,
        untilAt: '2026-09-09T06:00:00Z',
        under: 'AER-930',
      }),
      { editable: false },
    );
    expect(html).not.toContain('<input');
    expect(html).not.toContain('<textarea');
    expect(html).not.toContain('<button');
    expect(html).toContain('Max runs: 12');
    expect(html).toContain('Max spend: $40.00');
    expect(html).toContain('Max tokens this run: 2M tokens');
    expect(html).toContain('Max tokens per hour: no cap');
    expect(html).toContain('Also held under');
  });
});
