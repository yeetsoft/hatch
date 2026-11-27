import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { AmbitionList, AmbitionsBody, holdQuestions, WaitingStrip } from './AmbitionsPage';
import type { AmbitionOpenEpic, AmbitionRow, Ballot, BallotNode, Question, Status } from '../types';

/* No DOM runner: the row is rendered from props, the way PlanPage.test.tsx
   does it for EpicCard. */
const statuses = [
  { id: 1, name: 'To Do', color: '#3366cc' },
  { id: 2, name: 'In Progress', color: '#cc6633' },
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
  open: [],
  childBeyondScope: false,
  ...over,
});

const render = (...rows: AmbitionRow[]): string => renderList(undefined, ...rows);

const renderList = (onNew: (() => void) | undefined, ...rows: AmbitionRow[]): string =>
  renderToStaticMarkup(
    <MemoryRouter>
      <AmbitionList ambitions={rows} statuses={statuses} onNew={onNew} />
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

/* The strip above the rows: drawn from a ballot and a fixed clock. */
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

const ballot = (...roots: BallotNode[]): Ballot => ({ roots, questions: [] });

const strip = (b: Ballot | null, error: string | null = null, scopeKeys: string[] = []): string =>
  renderToStaticMarkup(
    <MemoryRouter>
      <WaitingStrip ballot={b} error={error} scopeKeys={scopeKeys} now={NOW} />
    </MemoryRouter>,
  );

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

describe('WaitingStrip', () => {
  it('draws the strip as a card, its heading the card heading the rest of the app uses', () => {
    const html = strip(tree());

    expect(html).toContain('<section class="hatch-card hatch-ambition-strip">');
    expect(html).toContain('<h2 class="hatch-section-title">');
  });

  it('draws one line per hold question, in the ballot order, each linking to the ballot', () => {
    const html = strip(tree());

    expect(html).toContain('2 decisions waiting on you');
    expect(html.match(/hatch-ambition-strip-line"/g)).toHaveLength(2);
    expect(html.match(/href="\/ballot"/g)).toHaveLength(2);
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

  it('says decision, not decisions, for one', () => {
    const html = strip(ballot(node('AER-1', 'ambition', { questions: [question(1, 'AER-1')] })));

    expect(html).toContain('1 decision waiting on you');
    expect(html).not.toContain('1 decisions');
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

  it('draws its sentence for an empty ballot', () => {
    expect(strip(ballot())).toContain('Nothing is waiting on you.');
  });

  it('draws the failure sentence as an error, not an empty state', () => {
    const html = strip(null, 'boom');

    expect(html).toContain('<p class="text-danger hatch-ambition-failure">');
    expect(html).not.toContain('hatch-empty');
  });

  it('links to the ballot under the scope of the tab', () => {
    expect(strip(tree(), null, ['AER'])).toContain('href="/ballot?scope=AER"');
  });
});

describe('AmbitionsBody', () => {
  it('draws the failure sentence and still draws the rows when the ballot could not be read', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AmbitionsBody
          data={{ ambitions: [row('AER-1')], statuses, ballot: null, ballotError: 'boom', scopeKey: '' }}
          scopeKeys={[]}
          now={NOW}
        />
      </MemoryRouter>,
    );

    expect(html).toContain('could not be read: boom');
    expect(html).not.toContain('hatch-ambition-strip-line');
    expect(html).toContain('hatch-ambition-row');
  });

  it('draws the strip above the rows', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <AmbitionsBody
          data={{ ambitions: [row('AER-1')], statuses, ballot: tree(), ballotError: null, scopeKey: '' }}
          scopeKeys={[]}
          now={NOW}
        />
      </MemoryRouter>,
    );

    expect(html.indexOf('decisions waiting on you')).toBeGreaterThan(-1);
    expect(html.indexOf('decisions waiting on you')).toBeLessThan(html.indexOf('hatch-ambition-row'));
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
