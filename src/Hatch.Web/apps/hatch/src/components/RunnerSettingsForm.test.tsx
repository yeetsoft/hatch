import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { RunnerSettingsForm } from './RunnerSettingsForm';
import type { Runner, RunnerSession, Status } from '../types';

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
  hold: [],
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
  launcherBehind: false,
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

const status = (id: number, name: string, sortOrder: number, over: Partial<Status> = {}): Status => ({
  id,
  name,
  sortOrder,
  isTerminal: false,
  isDeferred: false,
  isWip: false,
  expressSkips: false,
  parentPulls: false,
  agentFiles: false,
  isImplementation: false,
  isProtected: false,
  mergesPullRequest: false,
  runnerTags: [],
  color: '#6b7280',
  ...over,
});

const BOARD: Status[] = [
  status(2, 'Breakdown', 20),
  status(3, 'In Progress', 30),
  status(4, 'In Review', 40),
  status(7, 'Later', 70, { isDeferred: true }),
  status(9, 'Done', 90, { isTerminal: true }),
];

const render = (
  r: Runner,
  over: { live?: RunnerSession[]; editable?: boolean; refusal?: string | null; statuses?: Status[] | null } = {},
) =>
  renderToStaticMarkup(
    <MemoryRouter>
      <RunnerSettingsForm
        runner={r}
        live={over.live ?? []}
        statuses={over.statuses === undefined ? BOARD : over.statuses}
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

  it('draws Refuses models beside Tags, one per line, with its own hint', () => {
    const html = render(runner({ refusedModels: ['fable', 'claude-opus-5'] }));
    expect(html).toContain('Refuses models');
    expect(html).toContain('fable\nclaude-opus-5');
    expect(html).toContain('Empty refuses none.');
    expect(html).toContain("Binds on the board&#x27;s very next dispatch read, not on the next heartbeat the way tags do.");
    expect(html.match(/<textarea/g)).toHaveLength(2);
    expect(html.indexOf('Refuses models')).toBeGreaterThan(html.indexOf('Tags'));
    expect(html.indexOf('Refuses models')).toBeLessThan(html.indexOf('Save settings'));
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

  it('reads what a runner refuses when not editable, with no textarea', () => {
    const html = render(runner({ refusedModels: ['fable'] }), { editable: false });
    expect(html).toContain('Refuses: fable');
    expect(html).not.toContain('<textarea');
    expect(render(runner({ refusedModels: ['fable', 'haiku'] }), { editable: false })).toContain(
      'Refuses: fable, haiku',
    );
  });

  it('reads an unrestricted runner in words when not editable', () => {
    const html = render(runner(), { editable: false });
    expect(html).toContain('Tags: any runner');
    expect(html).toContain('Under: the whole board');
    expect(html).toContain('Refuses: nothing');
    expect(html).toContain('Columns: every column');
  });
});

describe('RunnerSettingsForm, the Columns section', () => {
  /** The list's rows, by column name: whether the row is lit and whether it is ticked. */
  const rows = (html: string) =>
    Object.fromEntries(
      html
        .split('<li ')
        .slice(1)
        .map((li) => [
          /hatch-status-pill-sm[^>]*>([^<]+)</.exec(li)![1],
          { lit: !li.includes('hatch-runner-column--dim'), ticked: /type="checkbox"[^>]*checked/.test(li) },
        ]),
    );

  it('draws three choices, with the held one picked', () => {
    const html = render(runner({ statusMode: 'except', statusIds: [3] }));
    expect(html).toContain('<legend>Columns</legend>');
    expect(html).toContain('Every column');
    expect(html).toContain('Every column except');
    expect(html).toContain('Only these');
    expect(html.match(/type="radio"/g)).toHaveLength(3);
    expect(html.match(/type="radio"[^>]*checked/g)).toHaveLength(1);
  });

  it('draws no list under every column', () => {
    const html = render(runner());
    expect(html).not.toContain('<li ');
    expect(html).not.toContain('hatch-runner-columns-bulk');
    expect(html).not.toContain('Nothing ticked');
  });

  it('draws the list dim under only, with the ticked column alone lit', () => {
    expect(rows(render(runner({ statusMode: 'only', statusIds: [] })))).toEqual({
      Breakdown: { lit: false, ticked: false },
      'In Progress': { lit: false, ticked: false },
      'In Review': { lit: false, ticked: false },
    });
    expect(rows(render(runner({ statusMode: 'only', statusIds: [2] })))).toEqual({
      Breakdown: { lit: true, ticked: true },
      'In Progress': { lit: false, ticked: false },
      'In Review': { lit: false, ticked: false },
    });
  });

  it('draws the list lit under except, with the ticked column alone dim', () => {
    expect(rows(render(runner({ statusMode: 'except', statusIds: [] })))).toEqual({
      Breakdown: { lit: true, ticked: false },
      'In Progress': { lit: true, ticked: false },
      'In Review': { lit: true, ticked: false },
    });
    expect(rows(render(runner({ statusMode: 'except', statusIds: [3] })))).toEqual({
      Breakdown: { lit: true, ticked: false },
      'In Progress': { lit: false, ticked: true },
      'In Review': { lit: true, ticked: false },
    });
  });

  it('never lists a terminal or deferred column, even one the rule names', () => {
    const html = render(runner({ statusMode: 'only', statusIds: [7, 9] }));
    expect(Object.keys(rows(html))).toEqual(['Breakdown', 'In Progress', 'In Review']);
    expect(html).not.toContain('Later');
    expect(html).not.toContain('Done');
  });

  it('lists the columns in sortOrder, each a pill in its own colour', () => {
    const html = render(runner({ statusMode: 'only' }), {
      statuses: [status(5, 'Second', 50, { color: '#ff0000' }), status(6, 'First', 10)],
    });
    expect(Object.keys(rows(html))).toEqual(['First', 'Second']);
    expect(html).toContain('--status-color:#ff0000');
  });

  it('draws All, None and an only press on each row', () => {
    const html = render(runner({ statusMode: 'only' }));
    expect(html).toMatch(/<button[^>]*>All</);
    expect(html).toMatch(/<button[^>]*>None</);
    expect(html.match(/hatch-runner-column-only/g)).toHaveLength(3);
    expect(html).toContain('aria-label="Only Breakdown"');
  });

  it('says what nothing ticked means, per mode', () => {
    expect(render(runner({ statusMode: 'only' }))).toContain('Nothing ticked works nothing.');
    expect(render(runner({ statusMode: 'except' }))).toContain('Nothing ticked works everything.');
  });

  it('sits after Refuses models and before Save settings', () => {
    const html = render(runner({ statusMode: 'only' }));
    expect(html.indexOf('<legend>Columns</legend>')).toBeGreaterThan(html.indexOf('Refuses models'));
    expect(html.indexOf('<legend>Columns</legend>')).toBeLessThan(html.indexOf('Save settings'));
  });

  it('leaves Save disabled while the rule is as held', () => {
    expect(render(runner({ statusMode: 'only', statusIds: [2] }))).toMatch(/<button[^>]*disabled[^>]*>Save settings/);
  });

  it('draws nothing until the columns are read, and the rest of the card still works', () => {
    const html = render(runner({ statusMode: 'only', statusIds: [2] }), { statuses: null });
    expect(html).not.toContain('<legend>Columns</legend>');
    expect(html).toContain('Save settings');
    expect(html).toContain('Refuses models');
  });

  it('reads the rule as one line when not editable, with no control', () => {
    const line = (r: Runner) => render(r, { editable: false });
    expect(line(runner())).toContain('Columns: every column');
    expect(line(runner({ statusMode: 'except', statusIds: [3, 4] }))).toContain(
      'Columns: every column except In Progress, In Review',
    );
    expect(line(runner({ statusMode: 'only', statusIds: [4, 2] }))).toContain('Columns: only Breakdown, In Review');
    expect(line(runner({ statusMode: 'only', statusIds: [] }))).toContain('Columns: no column');
    expect(line(runner({ statusMode: 'only', statusIds: [2] }))).not.toContain('type="radio"');
    expect(line(runner({ statusMode: 'only', statusIds: [2] }))).not.toContain('type="checkbox"');
  });

  it('puts the read-only line after Refuses', () => {
    const html = render(runner(), { editable: false });
    expect(html.indexOf('Columns:')).toBeGreaterThan(html.indexOf('Refuses:'));
  });
});
