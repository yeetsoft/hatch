import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { AmbitionList, AmbitionsBody, AmbitionTiles, heldIssues, holdQuestions } from './AmbitionsPage';
import type { AmbitionsView } from './AmbitionsPage';
import type {
  AmbitionOpenEpic,
  AmbitionRow,
  Ballot,
  BallotNode,
  BallotQuestion,
  IssueBudget,
  Question,
  Status,
} from '../types';

/* No DOM runner: the row is rendered from props, the way PlanPage.test.tsx
   does it for EpicCard. */
const statuses = [
  { id: 1, name: 'To Do', color: '#3366cc' },
  { id: 2, name: 'In Progress', color: '#cc6633' },
  { id: 3, name: 'Done', color: '#33aa66', isTerminal: true, isDeferred: false },
  { id: 4, name: 'Shelved', color: '#999999', isTerminal: false, isDeferred: true },
] as Status[];

const epic = (key: string, over: Partial<AmbitionOpenEpic> = {}): AmbitionOpenEpic => ({
  key,
  projectKey: key.split('-')[0],
  title: `title ${key}`,
  statusId: 2,
  statusName: 'In Progress',
  serves: [],
  waitsOnPerson: false,
  why: null,
  ...over,
});

const row = (key: string, over: Partial<AmbitionRow> = {}): AmbitionRow => ({
  key,
  projectKey: key.split('-')[0],
  title: `ambition ${key}`,
  statusId: 1,
  statusName: 'To Do',
  stage: 'ready',
  stageWhy: 'Nothing runs until a person starts it.',
  limit: 2,
  capabilities: 10,
  holds: 2,
  inFlight: 3,
  waitsOnPerson: 0,
  notStarted: 5,
  newlyHolding: 1,
  open: [],
  summary: '',
  decisions: 0,
  budget: { budgetUsd: null, spentUsd: 0, unpricedSessions: 0, spent: false },
  childBeyondScope: false,
  ...over,
});

const budget = (budgetUsd: number | null, spentUsd: number): IssueBudget => ({
  budgetUsd,
  spentUsd,
  unpricedSessions: 0,
  spent: budgetUsd !== null && spentUsd >= budgetUsd,
});

const render = (...rows: AmbitionRow[]): string => renderList(undefined, ...rows);

const renderList = (onNew: (() => void) | undefined, ...rows: AmbitionRow[]): string =>
  renderToStaticMarkup(
    <MemoryRouter>
      <AmbitionList ambitions={rows} statuses={statuses} onNew={onNew} />
    </MemoryRouter>,
  );

const renderScoped = (scopeKeys: string[], ...rows: AmbitionRow[]): string =>
  renderToStaticMarkup(
    <MemoryRouter>
      <AmbitionList ambitions={rows} statuses={statuses} scopeKeys={scopeKeys} />
    </MemoryRouter>,
  );

describe('AmbitionList', () => {
  it('says in two sentences what an ambition is when none is in scope', () => {
    const html = render();

    expect(html).toContain('An ambition is what you are driving at, broken into capabilities');
    expect(html).toContain('A person starts one by saying what they want, and a session shapes it.');
    expect(html).not.toContain('No ambition in scope.');
    expect(html).toContain('hatch-empty');
    expect(html).not.toContain('hatch-ambition-row');
  });

  it('offers the New ambition press on the empty page when given one', () => {
    expect(renderList(() => {})).toContain('New ambition');
  });

  it('draws no press on the empty page for a key, but both sentences', () => {
    const html = renderList(undefined);

    expect(html).not.toContain('New ambition');
    expect(html).toContain('An ambition is what you are driving at');
  });


  it('draws one row per ambition, each with its link and project chip', () => {
    const html = render(row('AER-1'), row('OPS-2'));

    expect(html.match(/hatch-ambition-row/g)).toHaveLength(2);
    expect(html).toContain('href="/issues/AER-1"');
    expect(html).toContain('href="/issues/OPS-2"');
    expect(html).toContain('<span class="hatch-plan-project">AER</span>');
    expect(html).toContain('<span class="hatch-plan-project">OPS</span>');
  });

  it("draws the stage as a pill where the column's was, and the sentence under the title", () => {
    const html = render(row('AER-1', { stage: 'budget spent', stageWhy: 'Its budget is spent.' }));

    expect(html).toContain(
      '<span class="hatch-plan-project">AER</span><span class="hatch-stage-pill hatch-stage-budget-spent">Budget spent</span></span>' +
        '<p class="hatch-ambition-why">Its budget is spent.</p>',
    );
    expect(html.indexOf('ambition AER-1')).toBeLessThan(html.indexOf('Its budget is spent.'));
    // The row says its stage, not its column.
    expect(html).not.toContain('To Do');
    expect(html).not.toContain('hatch-status-pill');
  });

  it('heads each stage with its word and count, in the order the rows come', () => {
    const html = render(
      row('AER-1', { stage: 'budget spent' }),
      row('AER-2', { stage: 'running' }),
      row('AER-3', { stage: 'running' }),
      row('AER-4', { stage: 'draft' }),
    );

    const headings = [...html.matchAll(/<h2 class="hatch-ambition-group">(.*?)<\/h2>/g)].map((m) =>
      m[1].replace(/<!-- -->/g, ''),
    );
    expect(headings).toEqual(['Budget spent, 1', 'Running, 2', 'Draft, 1']);
    const places = ['AER-1', 'AER-2', 'AER-3', 'AER-4'].map((key) => html.indexOf(`href="/issues/${key}"`));
    expect(places).toEqual([...places].sort((a, b) => a - b));
    // The second running row sits under the Running heading, not under one of its own.
    expect(places[2]).toBeLessThan(html.indexOf('>Draft'));
  });

  it('folds done and shelved ambitions under one closed line at the foot, counted', () => {
    const html = render(
      row('AER-1', { stage: 'running' }),
      row('AER-2', { stage: 'done' }),
      row('AER-3', { stage: 'shelved' }),
    );

    expect(html).toContain('<details class="hatch-ambition-finished"><summary>2 finished</summary>');
    expect(html).not.toContain('open=""');
    const fold = html.indexOf('<details');
    expect(html.indexOf('href="/issues/AER-1"')).toBeLessThan(fold);
    expect(html.indexOf('href="/issues/AER-2"')).toBeGreaterThan(fold);
    expect(html.indexOf('href="/issues/AER-3"')).toBeGreaterThan(html.indexOf('href="/issues/AER-2"'));
    // The finished ones are under no group heading.
    expect(html.match(/hatch-ambition-group"/g)).toHaveLength(1);
  });

  it('draws no fold when nothing is finished, and only the fold when everything is', () => {
    expect(render(row('AER-1'))).not.toContain('<details');

    const html = render(row('AER-2', { stage: 'done' }));

    expect(html).toContain('<summary>1 finished</summary>');
    expect(html).not.toContain('hatch-ambition-group"');
    expect(html).not.toContain('hatch-empty__action');
    expect(html).not.toContain('A person starts one by saying what they want');
  });

  it('draws one segment per non-zero count, grown in proportion, and a legend naming each', () => {
    const html = render(row('AER-1'));

    expect(html).toContain('10 capabilities');
    expect(html.match(/hatch-ambition-segment/g)).toHaveLength(3);
    expect(html).toContain('hatch-ambition-holds" style="flex-grow:2"');
    expect(html).toContain('hatch-ambition-in-flight" style="flex-grow:3"');
    expect(html).toContain('hatch-ambition-not-started" style="flex-grow:5"');
    expect(html).not.toContain('hatch-ambition-waiting');

    expect(html).toContain('2 hold');
    expect(html).toContain('3 in flight');
    expect(html).toContain('5 not started');
    // The label reads all four out; the legend has no entry for the zero.
    expect(html).not.toContain('</span>0 waiting on a person');
  });

  it('reads all four counts out in the bar label, zeros included', () => {
    const html = render(row('AER-1'));

    expect(html).toContain('role="img"');
    expect(html).toContain('aria-label="2 hold, 3 in flight, 0 waiting on a person, 5 not started"');
  });

  it('draws no bar and no legend when every count is zero, and says so', () => {
    const html = render(row('AER-1', { capabilities: 0, holds: 0, inFlight: 0, waitsOnPerson: 0, notStarted: 0 }));

    expect(html).toContain('0 capabilities');
    expect(html).toContain('This ambition has no capabilities yet.');
    expect(html).toContain('hatch-empty');
    expect(html).not.toContain('hatch-ambition-bar');
    expect(html).not.toContain('hatch-ambition-legend');
  });

  it('says no epic is open when there is none', () => {
    const html = render(row('AER-1', { open: [] }));

    expect(html).toContain('In flight, 0 of 2');
    expect(html).toContain('No epic is open under this ambition.');
    expect(html).toContain('hatch-empty');
    expect(html).not.toContain('runs 2 at once');
  });

  it('carries the legend and the count line in the note style', () => {
    const html = render(row('AER-1', { open: [epic('AER-7')] }));

    expect(html).toContain('<ul class="hatch-ambition-legend">');
    expect(html).toContain('<p class="hatch-ambition-tally">1 open, runs 2 at once</p>');
  });

  it('draws an open epic with its key link, column and served badges, and the count line', () => {
    const html = render(row('AER-1', { open: [epic('AER-7', { serves: [1, 3] }), epic('OPS-8')] }));

    expect(html).toContain('In flight, 2 of 2');
    expect(html).toContain('2 open, runs 2 at once');
    expect(html).toContain('href="/issues/AER-7"');
    expect(html).toContain('title AER-7');
    expect(html).toContain('In Progress');
    expect(html).toContain('<span class="hatch-capability-badge">#1</span>');
    expect(html).toContain('<span class="hatch-capability-badge">#3</span>');
    // An epic serving nothing draws a dash.
    expect(html).toContain('<span class="text-muted">—</span>');
    expect(html).not.toContain('No epic is open');
  });

  it("draws an epic's column the list lacks as muted text", () => {
    const html = render(row('AER-1', { open: [epic('AER-7', { statusId: 99, statusName: 'Gone' })] }));

    expect(html).toContain('<span class="text-muted">Gone</span>');
  });

  it("draws a row as the app's card, and an epic's capability numbers in one cell after its column", () => {
    const html = render(row('AER-1', { open: [epic('AER-7', { serves: [1, 3] }), epic('OPS-8')] }));

    expect(html).toContain('<li class="hatch-card hatch-ambition-row">');
    expect(html).toContain(
      '<span class="hatch-ambition-serves"><span class="hatch-capability-badge">#1</span><span class="hatch-capability-badge">#3</span></span>',
    );
    expect(html).toContain('<span class="hatch-ambition-serves"><span class="text-muted">—</span></span>');
  });
});

describe('what a row says about the ambition', () => {
  it('draws the summary as plain text under the stage sentence', () => {
    const html = render(row('AER-1', { stageWhy: 'Nothing runs yet.', summary: 'People see their load at a glance.' }));

    expect(html).toContain(
      '<p class="hatch-ambition-why">Nothing runs yet.</p><p class="hatch-ambition-summary">People see their load at a glance.</p>',
    );
  });

  it('draws no summary line for an ambition with no description', () => {
    expect(render(row('AER-1', { summary: '' }))).not.toContain('hatch-ambition-summary');
  });
});

describe('the On you part of a row', () => {
  const part = (html: string): string => html.slice(html.indexOf('>On you<'));

  it('is the fourth part, headed On you, after the epics in flight', () => {
    const html = render(row('AER-1'));

    expect(html.match(/hatch-ambition-part/g)).toHaveLength(4);
    expect(html.match(/<h3 class="hatch-ambition-heading">/g)).toHaveLength(3);
    expect(html.indexOf('>In flight, 0 of 2<')).toBeLessThan(html.indexOf('>On you<'));
  });

  it('says 1 decision for one, linking the ballot, after a waiting swatch the screen readers skip', () => {
    const html = part(render(row('AER-1', { decisions: 1 })));

    expect(html).toContain(
      '<p class="hatch-ambition-decisions"><span class="hatch-ambition-swatch hatch-ambition-waiting" aria-hidden="true"></span>' +
        '<a href="/ballot" data-discover="true">1 decision</a></p>',
    );
    expect(html).not.toContain('No decision waits on you.');
  });

  it('says N decisions for more', () => {
    expect(part(render(row('AER-1', { decisions: 3 })))).toContain('<a href="/ballot" data-discover="true">3 decisions</a>');
  });

  it('opens the ballot under the scope of the tab', () => {
    const html = part(renderScoped(['AER', 'OPS'], row('AER-1', { decisions: 2 })));

    expect(html).toMatch(/<a href="\/ballot\?scope=AER(,|%2C)OPS" data-discover="true">2 decisions<\/a>/);
  });

  it('says no decision waits, in muted words and with no link or swatch, when none does', () => {
    const html = part(render(row('AER-1', { decisions: 0 })));

    expect(html).toContain('<p class="hatch-ambition-none text-muted">No decision waits on you.</p>');
    expect(html).not.toContain('/ballot');
    expect(html).not.toContain('hatch-ambition-decisions');
    expect(html).not.toContain('hatch-ambition-waiting');
  });

  it('draws no empty-state box in the part', () => {
    expect(part(render(row('AER-1')))).not.toContain('hatch-empty');
  });

  it('reads the percent spent against a budget, below the decisions', () => {
    const html = part(render(row('AER-1', { budget: budget(100, 41) })));

    expect(html).toContain('<p class="hatch-ambition-budget">41% spent</p>');
    expect(html.indexOf('No decision waits on you.')).toBeLessThan(html.indexOf('41% spent'));
  });

  it('reads the dollars spent where there is no budget', () => {
    expect(part(render(row('AER-1', { budget: budget(null, 12.5) })))).toContain('<p class="hatch-ambition-budget">$12.50 spent</p>');
  });

  it('says the budget is used up, in bold ink, once it is', () => {
    expect(part(render(row('AER-1', { budget: budget(50, 52) })))).toContain(
      '<p class="hatch-ambition-budget hatch-ambition-budget-spent">Budget used up: 104% spent</p>',
    );
  });
});

/* The tiles above the rows: drawn from a ballot and a fixed clock. */
const NOW = new Date('2026-11-20T12:00:00Z');

const question = (id: number, issueKey: string, over: Partial<Question> = {}): Question => ({
  id,
  issueKey,
  issueTitle: `title ${issueKey}`,
  body: `body ${id}
second line ${id}`,
  askedBy: 'runner',
  askedAt: '2026-11-17T12:00:00Z',
  options: null,
  answers: [],
  level: 'hold',
  lookBack: false,
  ...over,
});

const node = (key: string, type: BallotNode['type'], over: Partial<BallotNode> = {}): BallotNode => ({
  key,
  projectKey: key.split('-')[0],
  type,
  title: `${type} ${key}`,
  excerpt: '',
  isTerminal: false,
  children: [],
  questions: [],
  ...over,
});

/** The ballot's own list: one entry per question in the tree, each holding `holds`
    issues, as the server gives it. */
const entries = (roots: BallotNode[], holds: Record<number, number>): BallotQuestion[] => {
  const out: BallotQuestion[] = [];
  const walk = (n: BallotNode) => {
    for (const q of n.questions)
      out.push({ question: q, holds: holds[q.id] ?? 1, wentAheadOn: null, issueStatus: null, closedAt: null, merged: null, landings: null });
    n.children.forEach(walk);
  };
  roots.forEach(walk);
  return out;
};

const ballot = (...roots: BallotNode[]): Ballot => ({ roots, questions: entries(roots, {}) });

const withHolds = (holds: Record<number, number>, ...roots: BallotNode[]): Ballot => ({ roots, questions: entries(roots, holds) });

const view = (over: Partial<AmbitionsView> = {}): AmbitionsView => ({
  ambitions: [row('AER-1')],
  statuses,
  ballot: ballot(),
  ballotError: null,
  scopeKey: '',
  ...over,
});

const tiles = (over: Partial<AmbitionsView> = {}, scopeKeys: string[] = []): string =>
  renderToStaticMarkup(
    <MemoryRouter>
      <AmbitionTiles data={view(over)} scopeKeys={scopeKeys} now={NOW} />
    </MemoryRouter>,
  );

const strip = (b: Ballot | null, error: string | null = null, scopeKeys: string[] = []): string =>
  tiles({ ballot: b, ballotError: error }, scopeKeys);

const tree = (): Ballot =>
  ballot(
    node('AER-1', 'ambition', {
      questions: [question(1, 'AER-1')],
      children: [
        node('AER-2', 'epic', {
          questions: [question(2, 'AER-2'), question(3, 'AER-2', { level: 'build', body: 'a build matter' })],
        }),
      ],
    }),
  );

describe('the Waiting on you tile', () => {
  it("draws as the board's tile, headed Waiting on you, in the first place", () => {
    const html = strip(tree());

    expect(html).toContain('<div class="hatch-ambition-tiles">');
    expect(html).toContain('<section class="hatch-health-tile hatch-ambition-tile hatch-ambition-strip">');
    expect(html).toContain('<h2 class="hatch-health-label">Waiting on you</h2>');
    expect(html).toContain('<p class="hatch-health-value">2 decisions</p>');
    expect(html.indexOf('Waiting on you')).toBeLessThan(html.indexOf('Capabilities that hold'));
  });

  it('draws one line per hold question, in the ballot order, each linking to the ballot', () => {
    const html = strip(tree());

    expect(html.match(/hatch-ambition-strip-line"/g)).toHaveLength(2);
    expect(html.match(/href="\/ballot"/g)).toHaveLength(3);
    expect(html.match(/ambition AER-1/g)).toHaveLength(2);
    expect(html).toContain('>AER-1<');
    expect(html).toContain('>AER-2<');
    expect(html).toContain('body 1');
    expect(html).toContain('body 2');
    expect(html).not.toContain('second line');
    expect(html).toContain('3 days');
    expect(html).not.toContain('a build matter');
    expect(html.indexOf('body 1')).toBeLessThan(html.indexOf('body 2'));
  });

  it('says decision, not decisions, and open issue, not open issues, for one', () => {
    const html = strip(ballot(node('AER-1', 'ambition', { questions: [question(1, 'AER-1')] })));

    expect(html).toContain('<p class="hatch-health-value">1 decision</p>');
    expect(html).toContain('Holding 1 open issue.');
    expect(html).not.toContain('1 decisions');
    expect(html).not.toContain('1 open issues');
  });

  it('counts what the questions hold, an epic and its story once', () => {
    const b = withHolds(
      { 2: 4, 3: 2 },
      node('AER-1', 'ambition', {
        children: [
          node('AER-2', 'epic', {
            questions: [question(2, 'AER-2')],
            children: [node('AER-3', 'story', { questions: [question(3, 'AER-3')] })],
          }),
        ],
      }),
    );

    expect(heldIssues(b)).toBe(4);
    expect(strip(b)).toContain('Holding 4 open issues.');
  });

  it('sums an ambition question and an epic question beneath it', () => {
    const b = withHolds(
      { 1: 1, 2: 3 },
      node('AER-1', 'ambition', {
        questions: [question(1, 'AER-1')],
        children: [node('AER-2', 'epic', { questions: [question(2, 'AER-2')] })],
      }),
    );

    expect(heldIssues(b)).toBe(4);
  });

  it("counts the most any one of a node's questions holds, once", () => {
    const b = withHolds(
      { 1: 2, 2: 5 },
      node('AER-1', 'ambition', { children: [node('AER-2', 'epic', { questions: [question(1, 'AER-2'), question(2, 'AER-2')] })] }),
    );

    expect(heldIssues(b)).toBe(5);
  });

  it('counts several epics and ambitions each in turn, and leaves a look-back and a build question out', () => {
    const b = withHolds(
      { 1: 2, 2: 3, 3: 7, 4: 9 },
      node('AER-1', 'ambition', {
        children: [
          node('AER-2', 'epic', { questions: [question(1, 'AER-2')] }),
          node('AER-3', 'epic', { questions: [question(2, 'AER-3'), question(3, 'AER-3', { level: 'build' })] }),
        ],
      }),
      node('OPS-1', 'ambition', { questions: [question(4, 'OPS-1', { level: 'merge', lookBack: true })] }),
    );

    expect(heldIssues(b)).toBe(5);
  });

  it('names how long the oldest decision has waited', () => {
    const b = ballot(
      node('AER-1', 'ambition', {
        children: [
          node('AER-2', 'epic', { questions: [question(1, 'AER-2', { askedAt: '2026-11-20T03:00:00Z' })] }),
          node('AER-3', 'epic', { questions: [question(2, 'AER-3', { askedAt: '2026-11-20T10:00:00Z' })] }),
        ],
      }),
    );

    expect(strip(b)).toContain('Holding 2 open issues. The oldest has waited 9 hours.');
  });

  it('says the oldest was just asked under a minute', () => {
    const b = ballot(node('AER-1', 'ambition', { questions: [question(1, 'AER-1', { askedAt: '2026-11-20T11:59:30Z' })] }));

    expect(strip(b)).toContain('Holding 1 open issue. The oldest was just asked.');
  });

  it('links the ballot as a link, under the scope of the tab', () => {
    const html = strip(tree(), null, ['AER']);

    expect(html).toMatch(/<a class="hatch-ambition-tile-link" href="\/ballot\?scope=AER"[^>]*>Open the ballot<\/a>/);
    expect(html.match(/href="\/ballot\?scope=AER"/g)).toHaveLength(3);
  });

  it('leaves out a look-back', () => {
    const html = strip(
      ballot(node('AER-1', 'ambition', { questions: [question(1, 'AER-1', { level: 'merge', lookBack: true })] })),
    );

    expect(html).not.toContain('hatch-ambition-strip-line');
    expect(html).toContain('Nothing is waiting on you.');
    expect(html).toContain('hatch-empty');
  });

  it('draws no line for a ballot whose only root is an epic', () => {
    const html = strip(ballot(node('AER-2', 'epic', { questions: [question(1, 'AER-2')] })));

    expect(html).not.toContain('hatch-ambition-strip-line');
    expect(html).toContain('Nothing is waiting on you.');
  });

  it('with none waiting, says so, with no large line and no link', () => {
    const html = strip(ballot());

    expect(html).toContain('Nothing is waiting on you.');
    expect(html).toContain('<h2 class="hatch-health-label">Waiting on you</h2>');
    expect(html).not.toContain('hatch-health-value">0 decisions');
    expect(html).not.toContain('Open the ballot');
    expect(html).not.toContain('Holding');
  });

  it('says the ballot could not be read as an error, and still draws the second tile', () => {
    const html = strip(null, 'boom');

    expect(html).toContain('<p class="text-danger hatch-ambition-failure">What is waiting on you could not be read: boom</p>');
    expect(html).not.toContain('hatch-empty');
    expect(html).not.toContain('Open the ballot');
    expect(html).toContain('Capabilities that hold');
  });
});

describe('the Capabilities that hold tile', () => {
  it('reads N of M with the in-flight and newly holding counts, summed over the rows', () => {
    const html = tiles({
      ambitions: [
        row('AER-1', { capabilities: 4, holds: 1, inFlight: 1, newlyHolding: 1 }),
        row('OPS-2', { capabilities: 2, holds: 0, inFlight: 1, newlyHolding: 0 }),
      ],
    });

    expect(html).toContain('<section class="hatch-health-tile hatch-ambition-tile hatch-ambition-capabilities-tile">');
    expect(html).toContain('<h2 class="hatch-health-label">Capabilities that hold</h2>');
    expect(html).toContain('<p class="hatch-health-value">1 of 6</p>');
    expect(html).toContain('<p class="hatch-ambition-tile-note">2 more in flight. 1 newly holding this week.</p>');
  });

  it('leaves out an ambition in a finished column and one in a shelved column', () => {
    const html = tiles({
      ambitions: [
        row('AER-1', { capabilities: 4, holds: 1, inFlight: 1, newlyHolding: 1 }),
        row('AER-2', { statusId: 3, statusName: 'Done', capabilities: 10, holds: 10, inFlight: 5, newlyHolding: 5 }),
        row('AER-3', { statusId: 4, statusName: 'Shelved', capabilities: 20, holds: 20, inFlight: 6, newlyHolding: 6 }),
      ],
    });

    expect(html).toContain('1 of 4');
    expect(html).toContain('1 more in flight. 1 newly holding this week.');
  });

  it('reads 0 of 0 where only finished ambitions are in scope, and still draws both tiles', () => {
    const html = tiles({ ambitions: [row('AER-2', { statusId: 3, statusName: 'Done' })] });

    expect(html).toContain('0 of 0');
    expect(html).toContain('0 more in flight. 0 newly holding this week.');
    expect(html).toContain('Waiting on you');
  });

  it('is drawn with the decisions tile failed', () => {
    expect(tiles({ ballot: null, ballotError: 'boom' })).toContain('hatch-ambition-capabilities-tile');
  });
});

describe('AmbitionTiles', () => {
  it('draws neither tile where no ambition is in scope', () => {
    expect(tiles({ ambitions: [] })).toBe('');
  });
});

describe('AmbitionsBody', () => {
  it('draws the failure sentence and still draws the second tile and the rows when the ballot could not be read', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AmbitionsBody data={view({ ballot: null, ballotError: 'boom' })} scopeKeys={[]} now={NOW} />
      </MemoryRouter>,
    );

    expect(html).toContain('could not be read: boom');
    expect(html).not.toContain('hatch-ambition-strip-line');
    expect(html).toContain('Capabilities that hold');
    expect(html).toContain('hatch-ambition-row');
  });

  it('draws the tiles above the rows', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AmbitionsBody data={view({ ballot: tree() })} scopeKeys={[]} now={NOW} />
      </MemoryRouter>,
    );

    expect(html.indexOf('Waiting on you')).toBeGreaterThan(-1);
    expect(html.indexOf('Capabilities that hold')).toBeLessThan(html.indexOf('hatch-ambition-row'));
    expect(html.indexOf('Waiting on you')).toBeLessThan(html.indexOf('hatch-ambition-row'));
  });

  it('draws only the explaining sentences where no ambition is in scope', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AmbitionsBody data={view({ ambitions: [] })} scopeKeys={[]} now={NOW} />
      </MemoryRouter>,
    );

    expect(html).toContain('An ambition is what you are driving at');
    expect(html).not.toContain('Waiting on you');
    expect(html).not.toContain('Capabilities that hold');
  });
});

describe('holdQuestions', () => {
  it('walks an ambition, its epic and its story in order and keeps only hold', () => {
    const b = ballot(
      node('AER-1', 'ambition', {
        questions: [question(1, 'AER-1'), question(2, 'AER-1', { level: 'build' })],
        children: [
          node('AER-2', 'epic', {
            questions: [question(3, 'AER-2')],
            children: [node('AER-3', 'story', { questions: [question(4, 'AER-3')] })],
          }),
          node('AER-4', 'epic', { questions: [question(5, 'AER-4')] }),
        ],
      }),
      node('AER-9', 'epic', { questions: [question(6, 'AER-9')] }),
    );

    expect(holdQuestions(b).map((h) => h.question.id)).toEqual([1, 3, 4, 5]);
    expect(holdQuestions(b).every((h) => h.ambition === 'ambition AER-1')).toBe(true);
  });

  it('draws the New ambition press in the heading with onNew, and not without', () => {
    const body = (onNew?: () => void) =>
      renderToStaticMarkup(
        <MemoryRouter>
          <AmbitionsBody
            data={{ ambitions: [row('AER-1')], statuses, ballot: tree(), ballotError: null, scopeKey: '' }}
            scopeKeys={[]}
            now={NOW}
            onNew={onNew}
          />
        </MemoryRouter>,
      );

    const withPress = body(() => {});
    const actions = /hatch-page-header__actions">([\s\S]*?)<\/div>/.exec(withPress)?.[1] ?? '';
    expect(actions).toContain('New ambition');
    expect(body()).not.toContain('New ambition');
    expect(body()).not.toContain('hatch-page-header__actions');
  });
});
