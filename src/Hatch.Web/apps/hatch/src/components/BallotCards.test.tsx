import { afterEach, describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { BallotCards } from './BallotCards';
import { questionEmptyWords } from '../lib/attention';
import { LEVEL_WORDS } from '../lib/questions';
import type { BallotNode, BallotQuestion, Question } from '../types';

/* No DOM here, so this is structural - react-dom/server, as BallotTree.test.tsx
   reads. */

vi.mock('../lib/markdown', () => ({ renderMarkdown: (source: string) => source }));
vi.mock('../lib/clientLogger', () => ({ clientLogger: { info() {}, warn() {}, error() {} } }));

const noop = () => undefined;

afterEach(() => vi.unstubAllGlobals());

const question = (over: Partial<Question>): Question => ({
  id: 1,
  issueKey: 'PRJ-2',
  issueTitle: 'A story',
  body: 'Which of the two should it be?',
  askedBy: 'a-runner',
  askedAt: '2026-01-10T09:00:00Z',
  options: null,
  answers: [],
  level: 'hold',
  lookBack: false,
  ...over,
});

const ask = (q: Partial<Question>, holds: number): BallotQuestion => ({
  question: question(q),
  holds,
  wentAheadOn: null,
  issueStatus: null,
  closedAt: null,
  merged: null,
  landings: null,
});

const roots: BallotNode[] = [
  {
    key: 'PRJ-1',
    projectKey: 'PRJ',
    type: 'epic',
    title: 'The epic',
    excerpt: '',
    isTerminal: false,
    children: [
      { key: 'PRJ-2', projectKey: 'PRJ', type: 'story', title: 'A story', excerpt: '', isTerminal: false, children: [], questions: [] },
    ],
    questions: [],
  },
];

const options = [
  { label: 'Keep it', detail: null, recommended: false },
  { label: 'Change it', detail: 'Costs a day.', recommended: true },
];

const lookBackItem: BallotQuestion = {
  ...ask({ id: 3, level: 'merge', lookBack: true, options }, 0),
  wentAheadOn: 'Change it',
  issueStatus: 'Done',
  closedAt: '2026-02-03T10:00:00Z',
  merged: true,
};

const three = [
  ask({ id: 1, level: 'hold', options }, 3),
  ask({ id: 2, level: 'build', issueKey: 'PRJ-3', issueTitle: 'A task' }, 1),
  lookBackItem,
];

const render = (questions: BallotQuestion[]) =>
  renderToStaticMarkup(
    <MemoryRouter>
      <BallotCards questions={questions} roots={roots} onAnswered={noop} onClose={noop} onList={noop} />
    </MemoryRouter>,
  );

describe('BallotCards', () => {
  it('draws the first question with its place in the ballot', () => {
    const html = render(three);

    expect(html).toContain('Ballot');
    expect(html).toContain('1 of 3, most blocking first');
    expect(html).toContain('Close');
    expect(html).toContain('Which of the two should it be?');
  });

  it('draws the chain: the root, then the issue linked with its key and title', () => {
    const html = render(three);

    expect(html).toContain('The epic');
    expect(html).toMatch(/<a [^>]*href="\/issues\/PRJ-2"[^>]*>.*PRJ-2.*A story/);
  });

  it('draws what it holds and its level as two pills', () => {
    const html = render(three);

    expect(html).toContain('Holds 3 issues');
    expect(html).toContain(LEVEL_WORDS.hold);
  });

  it('says issue, not issues, for one', () => {
    const second = render([three[1], three[0]]);
    expect(second).toContain('Holds 1 issue<');
    expect(second).toContain(LEVEL_WORDS.build);
  });

  it('draws a look-back question as what was decided, what happened since, and two presses', () => {
    const html = render([lookBackItem]);

    expect(html).toContain('Decided without you');
    expect(html).toContain('Change it');
    expect(html).toContain('It stands in Done');
    expect(html).toContain('its pull request merged');
    expect(html).toContain('>Keep<');
    expect(html).toContain('>Overturn<');
    expect(html).toContain('Holds nothing');
    expect(html).not.toContain('Say something else');
    expect(html).not.toContain('aria-label="Set level"');
  });

  it('draws a program look-back as answered by a session, with the key, the decision, and what it found', () => {
    const withAnswer: BallotQuestion = {
      ...lookBackItem,
      wentAheadOn: 'Change it.',
      question: question({
        id: 3,
        level: 'merge',
        lookBack: true,
        options,
        answers: [
          {
            id: 10,
            author: 'hatch-loop',
            body: 'Change it.\n\nIt cost less than keeping it.',
            kind: 'answer',
            answersId: 3,
            options: null,
            createdAt: '2026-02-03T09:00:00Z',
            deliveredAt: null,
            deliveredTo: null,
            level: null,
            answeredAfter: false,
            authorKind: 'key',
          },
        ],
      }),
    };
    const html = render([withAnswer]);

    expect(html).toContain('Answered by a session');
    expect(html).toContain('hatch-loop');
    expect(html).toContain('Change it.');
    expect(html).toContain('Found in');
    expect(html).toContain('It cost less than keeping it.');
    expect(html).not.toContain('Decided without you');
    expect(html).toContain('>Keep<');
    expect(html).toContain('>Overturn<');
  });

  it('says its work landed on the trunk where the pull request did not merge', () => {
    const html = render([{ ...lookBackItem, merged: false, landings: [{ canonical: 'a/b', sha: 'abc' }] }]);

    expect(html).toContain('its work landed on the trunk');
    expect(html).not.toContain('its pull request merged');
  });

  it('draws an open question with neither the decided line nor the presses', () => {
    const html = render([three[0]]);

    expect(html).not.toContain('Decided without you');
    expect(html).not.toContain('>Keep<');
    expect(html).not.toContain('>Overturn<');
  });

  it('puts the recommended option first', () => {
    const html = render(three);

    expect(html.indexOf('Change it')).toBeGreaterThan(-1);
    expect(html.indexOf('Change it')).toBeLessThan(html.indexOf('Keep it'));
    expect(html).toContain('recommended');
  });

  it('counts what is behind it: those to answer and those to look over', () => {
    const html = render(three);

    expect(html).toContain('Then 1 more to answer, and 1 decided without you to look over');
    expect(html).toContain('Skip');
  });

  it('disables Skip on the last card', () => {
    const html = render([three[2]]);

    expect(html).toContain('Nothing else is waiting');
    expect(html).toMatch(/<button[^>]*disabled=""[^>]*>Skip</);
  });

  it('offers the other answer and the level buttons, and no principle', () => {
    const html = render(three);

    expect(html).toContain('Say something else');
    expect(html).toContain('aria-label="Set level"');
    expect(html.toLowerCase()).not.toContain('principle');
  });

  it('says the ballot is clear, with the switch to the list, once nothing is left', () => {
    const html = render([]);

    expect(html).toContain(questionEmptyWords());
    expect(html).toContain('Show the list by issue');
  });
});
