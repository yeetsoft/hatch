import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { BoundsForm, BoundsSummary, HostTotal, Row, RunnerDetails, RunnersPage, TokenBudget } from './RunnersPage';
import type { Runner, RunnerOrderRequest, RunnerPatchRequest } from '../types';

const listed = vi.hoisted(() => ({ runners: [] as unknown[] }));

vi.mock('../lib/useRunners', () => ({
  useRunners: () => ({ runners: listed.runners, error: null, reload: async () => undefined }),
}));

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

const noop = (_request: RunnerPatchRequest) => undefined;
const noopOrder = (_request: RunnerOrderRequest) => undefined;
const noopWithdraw = () => undefined;

const renderRow = (r: Runner) =>
  renderToStaticMarkup(
    <MemoryRouter>
      <table>
        <tbody>
          <Row runner={r} now={NOW} onPatch={noop} onOrder={noopOrder} onWithdraw={noopWithdraw} />
        </tbody>
      </table>
    </MemoryRouter>,
  );

describe('RunnersPage Row', () => {
  it('draws a working row\'s key as a link to the ticket, badged Working', () => {
    const html = renderRow(runner({ claimKey: 'HA-12' }));

    expect(html).toContain('href="/issues/HA-12"');
    expect(html).toContain('Working');
  });

  it('draws no ticket link for an idle row', () => {
    expect(renderRow(runner())).not.toContain('href="/issues/');
  });

  it('draws no ticket link for a paused row', () => {
    expect(renderRow(runner({ state: 'paused' }))).not.toContain('href="/issues/');
  });

  it('draws no ticket link for a stopping row', () => {
    expect(renderRow(runner({ state: 'stopping', claimKey: 'HA-12' }))).not.toContain('href="/issues/');
  });

  it('draws no ticket link for a gone row', () => {
    const html = renderRow(runner({ lastSeenAt: at(-3 * 3_600_000), claimKey: 'HA-12' }));
    expect(html).not.toContain('href="/issues/');
  });

  it('draws no ticket link for an exhausted row', () => {
    const html = renderRow(runner({ exhaustedUntil: at(3_600_000), claimKey: 'HA-12' }));
    expect(html).not.toContain('href="/issues/');
  });

  it('says one increment for a once row, and nothing for a loop row', () => {
    expect(renderRow(runner())).not.toContain('one increment');
    expect(renderRow(runner({ kind: 'once' }))).toContain('one increment');
  });

  it('badges only a row whose build is Behind the board\'s', () => {
    expect(renderRow(runner({ boardDrift: 'Behind' }))).toContain('Behind');
    for (const drift of ['Ahead', 'Current', 'Unknown'] as const) {
      expect(renderRow(runner({ boardDrift: drift }))).not.toContain('Behind');
    }
  });

  it('badges only a row whose build is Behind its own checkout\'s source', () => {
    expect(renderRow(runner({ sourceDrift: 'Behind' }))).toContain('Rebuild');
    for (const drift of ['Ahead', 'Current', 'Unknown'] as const) {
      expect(renderRow(runner({ sourceDrift: drift }))).not.toContain('Rebuild');
    }
  });

  it('keeps the two badges apart, and draws both on a row behind both', () => {
    const board = renderRow(runner({ boardDrift: 'Behind' }));
    expect(board).toContain('Behind');
    expect(board).not.toContain('Rebuild');

    const source = renderRow(runner({ sourceDrift: 'Behind' }));
    expect(source).toContain('Rebuild');
    expect(source).not.toContain('Behind');

    const both = renderRow(runner({ boardDrift: 'Behind', sourceDrift: 'Behind' }));
    expect(both).toContain('Behind');
    expect(both).toContain('Rebuild');

    const neither = renderRow(runner());
    expect(neither).not.toContain('Behind');
    expect(neither).not.toContain('Rebuild');
  });

  it('keeps where, agent and drives off the closed row', () => {
    const html = renderRow(runner({ name: 'distinct-name', agent: 'claude', drives: ['HA-12'] }));
    expect(html).not.toContain('here:/checkouts/one');
    expect(html).not.toContain('claude');
    expect(html).not.toContain('driving');
  });

  it('draws the controls on a loop row this caller can control', () => {
    const html = renderRow(runner({ canControl: true }));
    expect(html).toContain('Pause');
    expect(html).toContain('Stop after this one');
  });

  it('draws no controls and an owner sentence on a loop row this caller cannot control', () => {
    const html = renderRow(runner({ canControl: false, for: 'Some Person' }));
    expect(html).not.toContain('Pause');
    expect(html).not.toContain('Stop after this one');
    expect(html).not.toContain('Details');
    expect(html).toContain('Some Person');
  });

  it('draws no controls and no owner sentence on a once row', () => {
    const html = renderRow(runner({ kind: 'once' }));
    expect(html).not.toContain('Pause');
    expect(html).not.toContain('Stop after this one');
    expect(html).not.toContain('Details');
    expect(html).not.toContain('to control');
  });

  it('draws the key box and both Order buttons on a controllable row with no order', () => {
    const html = renderRow(runner());
    expect(html).toContain('aria-label="Ticket to work"');
    expect(html).toContain('Work this ticket');
    expect(html).toContain('Run one pass');
    expect(html).not.toContain('ordered to');
    expect(html).not.toContain('Withdraw');
  });

  it('draws a held work order as a sentence with Withdraw, in place of the Order control', () => {
    const html = renderRow(runner({ order: { kind: 'work', key: 'HA-12', by: 'Ada', at: at(-120_000) } }));
    expect(html).toContain('ordered to work HA-12 by Ada, 2 minutes ago');
    expect(html).toContain('Withdraw');
    expect(html).not.toContain('Work this ticket');
    expect(html).not.toContain('Run one pass');
  });

  it('draws a held pass order without a key, by somebody when nobody is named', () => {
    const html = renderRow(runner({ order: { kind: 'pass', key: null, by: null, at: at(-120_000) } }));
    expect(html).toContain('ordered to pass by somebody, 2 minutes ago');
  });

  it('disables the Order buttons on a stopping row', () => {
    const html = renderRow(runner({ state: 'stopping' }));
    for (const label of ['Work this ticket', 'Run one pass']) {
      expect(html).toMatch(new RegExp(`<button[^>]*disabled[^>]*>${label}</button>`));
    }
  });

  it('leaves Run one pass enabled on a running row', () => {
    expect(renderRow(runner())).not.toMatch(/<button[^>]*disabled[^>]*>Run one pass<\/button>/);
  });

  it('disables Work this ticket until the box holds a key', () => {
    expect(renderRow(runner())).toMatch(/<button[^>]*disabled[^>]*>Work this ticket<\/button>/);
  });

  it('draws no Order control and no Withdraw on a once row or one this caller cannot control', () => {
    const order = { kind: 'pass' as const, key: null, by: 'Ada', at: at(-60_000) };
    for (const r of [runner({ kind: 'once', order }), runner({ canControl: false, order })]) {
      const html = renderRow(r);
      expect(html).not.toContain('Ticket to work');
      expect(html).not.toContain('Run one pass');
      expect(html).not.toContain('Withdraw');
      expect(html).not.toContain('ordered to');
    }
  });
});

const renderBounds = (r: Runner) =>
  renderToStaticMarkup(
    <MemoryRouter>
      <BoundsSummary runner={r} />
    </MemoryRouter>,
  );

describe('RunnersPage BoundsSummary', () => {
  it('links the under epic to its ticket', () => {
    const html = renderBounds(runner({ under: 'HA-930' }));
    expect(html).toContain('href="/issues/HA-930"');
    expect(html).toContain('under');
  });

  it('draws no link when there is no under', () => {
    expect(renderBounds(runner())).not.toContain('href="/issues/');
  });

  it('joins under and other bounds with commas', () => {
    const html = renderBounds(runner({ under: 'HA-930', maxRuns: 5 }));
    expect(html).toContain('href="/issues/HA-930"');
    expect(html).toContain('5 run(s)');
    expect(html).toContain(', ');
  });

  it('lists the project keys when set, prefixed by the word', () => {
    expect(renderBounds(runner({ projects: ['HA', 'AER'] }))).toContain('projects HA, AER');
  });

  it('says the runner refuses dangerous work when it does', () => {
    expect(renderBounds(runner({ refusesDangerous: true }))).toContain('Refuses dangerous work');
  });

  it('says no bounds for a runner held to no project', () => {
    const html = renderBounds(runner({ projects: [] }));
    expect(html).toContain('no bounds');
    expect(html).not.toContain('projects');
    expect(html).not.toContain('Refuses dangerous work');
  });
});

const renderDetails = (r: Runner) =>
  renderToStaticMarkup(
    <MemoryRouter>
      <RunnerDetails runner={r} now={NOW} />
    </MemoryRouter>,
  );

describe('RunnersPage RunnerDetails', () => {
  it('draws where and agent when set', () => {
    const html = renderDetails(runner({ where: 'here:/checkouts/one', agent: 'claude' }));
    expect(html).toContain('here:/checkouts/one');
    expect(html).toContain('claude');
  });

  it('says driving nothing when the runner drives nothing', () => {
    expect(renderDetails(runner({ drives: [] }))).toContain('driving nothing');
  });

  it('names what it drives when it drives something', () => {
    expect(renderDetails(runner({ drives: ['x'] }))).toContain('driving x');
  });

  it('draws nothing from repositoriesLabel when there are none', () => {
    expect(renderDetails(runner({ repositories: [] }))).not.toContain('clones');
  });

  it('joins repositories and says clones when it clones what it lacks', () => {
    const html = renderDetails(runner({ repositories: ['a', 'b'], clones: true }));
    expect(html).toContain('a, b, clones what it lacks');
  });

  it('draws the build label when revision and builtAt are set', () => {
    const html = renderDetails(runner({ revision: 'a1b2c3d4e5f60718293a4b5c6d7e8f9011121314', builtAt: at(-3_600_000) }));
    expect(html).toContain('a1b2c3d');
  });

  it('draws nothing for a build when revision is null', () => {
    expect(renderDetails(runner({ revision: null }))).not.toContain('dev build');
  });

  it('draws the models a runner has offered', () => {
    expect(renderDetails(runner({ models: ['haiku', 'sonnet'] }))).toContain('models haiku, sonnet');
  });

  it('draws nothing from modelsLabel when there are none', () => {
    expect(renderDetails(runner({ models: [] }))).not.toContain('models');
  });

  it('draws the dependencies a runner has', () => {
    expect(renderDetails(runner({ has: ['node@22.11.0'] }))).toContain('has node 22.11.0');
  });

  it('draws nothing from hasLabel when there are none', () => {
    expect(renderDetails(runner({ has: [] }))).not.toContain('has ');
  });

  it('draws the has line after the repositories line', () => {
    const html = renderDetails(runner({ repositories: ['a'], has: ['node@22.11.0'] }));
    expect(html.indexOf('has node 22.11.0')).toBeGreaterThan(html.indexOf('a'));
  });
});

const renderBudget = (r: Runner) => renderToStaticMarkup(<TokenBudget runner={r} />);

describe('RunnersPage TokenBudget', () => {
  it('says so where neither cap is set, even when a total was reported', () => {
    const html = renderBudget(runner({ tokensUsed: 900_000 }));
    expect(html).toContain('No token budget');
    expect(html).not.toContain('hatch-usage-bar-track');
  });

  it('draws each window against its cap, with a bar filled to its share', () => {
    const html = renderBudget(
      runner({ maxTokens: 2_000_000, tokensUsed: 1_200_000, maxTokensPerHour: 200_000, tokensLastHour: 140_000 }),
    );
    expect(html).toContain('1.2M / 2M tokens');
    expect(html).toContain('140k / 200k per hour');
    expect(html).toContain('--usage-color:var(--success);width:60%');
    expect(html).toContain('--usage-color:var(--success);width:70%');
    expect(html).toContain('role="img" aria-label="1.2M / 2M tokens"');
  });

  it('turns the fill to warn at 80% and danger at 100%', () => {
    const warn = renderBudget(runner({ maxTokens: 1_000_000, tokensUsed: 800_000 }));
    expect(warn).toContain('--usage-color:var(--warn);width:80%');

    const danger = renderBudget(runner({ maxTokens: 1_000_000, tokensUsed: 1_000_000 }));
    expect(danger).toContain('--usage-color:var(--danger);width:100%');
  });

  it('draws a window with no cap as its figure alone, with no bar', () => {
    const html = renderBudget(runner({ maxTokensPerHour: 200_000, tokensUsed: 1_200_000 }));
    expect(html).toContain('1.2M tokens, no cap');
    expect(html.match(/hatch-usage-bar-track/g)?.length).toBe(1);
  });
});

describe('RunnersPage token bounds', () => {
  it('names the caps in the bounds summary, only when set', () => {
    const html = renderBounds(runner({ maxTokens: 2_000_000, maxTokensPerHour: 200_000 }));
    expect(html).toContain('2M tokens, 200k tokens/hour');
    expect(renderBounds(runner())).toContain('no bounds');
  });

  it('marks a default-sourced cap in the bounds summary and the token budget line', () => {
    const inherited = runner({
      maxTokens: 2_000_000,
      maxTokensPerHour: 200_000,
      maxTokensSource: 'default',
      maxTokensPerHourSource: 'default',
      tokensUsed: 1_200_000,
      tokensLastHour: 140_000,
    });

    const summary = renderBounds(inherited);
    expect(summary).toContain('2M tokens (default)');
    expect(summary).toContain('200k tokens/hour (default)');

    const budget = renderBudget(inherited);
    expect(budget).toContain('1.2M / 2M tokens (default)');
    expect(budget).toContain('140k / 200k per hour (default)');
  });

  it('badges a throttled row with the cap it is over, and draws what it last said', () => {
    const html = renderRow(
      runner({
        maxTokensPerHour: 200_000,
        throttledUntil: at(1_800_000),
        line: 'Stopped: --max-tokens 2M reached',
      }),
    );
    expect(html).toContain('Over 200k tokens/hour until');
    expect(html).toContain('Stopped: --max-tokens 2M reached');
  });

  it('draws the budget cell on the row, and spans seven columns under it', () => {
    const closed = renderRow(runner());
    expect(closed).toContain('No token budget');

    const total = renderToStaticMarkup(
      <table>
        <tbody>
          <HostTotal group={{ host: 'box', runners: [], totalRuns: 1, totalSpent: 1 }} />
        </tbody>
      </table>,
    );
    expect(total).toContain('colSpan="7"');
  });

  it('draws the Token budget column in the page header', () => {
    listed.runners = [runner()];
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <RunnersPage />
      </MemoryRouter>,
    );
    expect(html).toContain('<th>Token budget</th>');
  });

  it('keeps the form out of a closed row, controllable or not', () => {
    for (const r of [runner(), runner({ canControl: false }), runner({ kind: 'once' })]) {
      expect(renderRow(r)).not.toContain('Save bounds');
    }
  });
});

const renderForm = (props: { refusal?: string; refusalField?: 'maxTokens' | 'maxTokensPerHour' } = {}) =>
  renderToStaticMarkup(
    <MemoryRouter>
      <BoundsForm runner={runner({ maxTokens: 2_000_000, maxTokensSource: 'runner' })} onSave={noop} {...props} />
    </MemoryRouter>,
  );

describe('RunnersPage BoundsForm', () => {
  const fieldOf = (html: string, label: string) => {
    const start = html.indexOf(label);
    return html.slice(html.lastIndexOf('<div class="hatch-field', start), html.indexOf('</div>', start));
  };

  it('draws both token boxes, the saved cap grouped and the other empty, each with the hint', () => {
    const html = renderForm();
    expect(html).toContain('Max tokens this run');
    expect(html).toContain('Max tokens per hour');
    expect(html).toContain('value="2,000,000"');
    expect(html.match(/A token count, like 500k or 2M\. Empty is no cap\./g)?.length).toBe(2);
    expect(html).not.toContain('role="alert"');
  });

  it('draws a refusal naming a token flag under its own field, as an alert, once', () => {
    const text = '--max-tokens-per-hour takes a token count, like 500k or 2M - not "x"';
    const html = renderForm({ refusal: text, refusalField: 'maxTokensPerHour' });

    expect(html.match(/role="alert"/g)?.length).toBe(1);
    expect(fieldOf(html, 'Max tokens per hour')).toContain('role="alert"');
    expect(fieldOf(html, 'Max tokens per hour')).toContain('--max-tokens-per-hour takes a token count');
    expect(fieldOf(html, 'Max tokens this run')).not.toContain('role="alert"');
    expect(fieldOf(html, 'Max tokens this run')).toContain('A token count');
  });

  it('draws no problem line for a clean runner', () => {
    expect(renderForm()).not.toContain('text-danger');
  });

  it('draws a default-sourced field empty, with a hint naming the inherited figure', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <BoundsForm
          runner={runner({ maxTokens: 1_000_000, maxTokensSource: 'default' })}
          onSave={noop}
        />
      </MemoryRouter>,
    );

    expect(fieldOf(html, 'Max tokens this run')).not.toContain('value="1,000,000"');
    expect(html).toContain('value=""');
    expect(html).toContain('Empty inherits the default: 1M tokens.');
  });
});

describe('RunnersPage grouping by machine', () => {
  const renderPage = (runners: Runner[]) => {
    listed.runners = runners;
    return renderToStaticMarkup(
      <MemoryRouter>
        <RunnersPage />
      </MemoryRouter>,
    );
  };
  const totals = (html: string) => html.match(/hatch-runners-host-total/g)?.length ?? 0;

  it('totals the runs and spend of two runners that share a machine under one line', () => {
    const html = renderPage([
      runner({ name: 'a', host: 'box', runs: 3, spent: 1.5 }),
      runner({ name: 'b', host: 'box', runs: 4, spent: 2.25 }),
    ]);
    expect(totals(html)).toBe(1);
    expect(html).toContain('box');
    expect(html).toContain('7 runs');
    expect(html).toContain('$3.75');
  });

  it('reads a runner that has not reported runs or spend as nothing', () => {
    const html = renderPage([
      runner({ name: 'a', host: 'box', runs: 2, spent: 0.5 }),
      runner({ name: 'b', host: 'box' }),
    ]);
    expect(html).toContain('2 runs');
    expect(html).toContain('$0.50');
  });

  it('draws no summary for a machine with one runner', () => {
    const html = renderPage([runner({ name: 'a', host: 'box', runs: 3, spent: 1.5 })]);
    expect(totals(html)).toBe(0);
    expect(html).toContain('>a<');
  });

  it('never groups two runners that have no host', () => {
    const html = renderPage([
      runner({ name: 'a', host: null, runs: 1, spent: 1 }),
      runner({ name: 'b', host: null, runs: 1, spent: 1 }),
      runner({ name: 'c', host: '', runs: 1, spent: 1 }),
    ]);
    expect(totals(html)).toBe(0);
    for (const name of ['a', 'b', 'c']) expect(html).toContain(`>${name}<`);
  });

  it('gathers the runners of one machine together even when another runner sits between them', () => {
    const html = renderPage([
      runner({ name: 'a', host: 'box' }),
      runner({ name: 'x', host: 'other' }),
      runner({ name: 'b', host: 'box' }),
    ]);
    expect(totals(html)).toBe(1);
    expect(html.indexOf('>b<')).toBeLessThan(html.indexOf('>x<'));
  });
});
