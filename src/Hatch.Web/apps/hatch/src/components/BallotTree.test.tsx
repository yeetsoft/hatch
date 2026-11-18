import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { BallotTree } from './BallotTree';
import { questionEmptyWords, waitedWords } from '../lib/attention';
import type { BallotNode, Project, Question } from '../types';

/* No DOM here, so this is structural - react-dom/server, the way
   AttentionHuman.test.tsx reads. */

vi.mock('../lib/clientLogger', () => ({ clientLogger: { info() {}, warn() {}, error() {} } }));

const now = new Date('2026-01-10T12:00:00Z');
const askedAt = '2026-01-10T09:00:00Z';

const question = (over: Partial<Question> = {}): Question => ({
  id: 1,
  issueKey: 'PRJ-3',
  issueTitle: 'A task',
  body: 'Which of the two should it be?',
  askedBy: 'a-runner',
  askedAt,
  options: null,
  answers: [],
  ...over,
});

const node = (over: Partial<BallotNode> = {}): BallotNode => ({
  key: 'PRJ-1',
  projectKey: 'PRJ',
  type: 'epic',
  title: 'The epic',
  excerpt: '',
  isTerminal: false,
  children: [],
  questions: [],
  ...over,
});

const render = (roots: BallotNode[], projects: Project[] = []) =>
  renderToStaticMarkup(
    <MemoryRouter>
      <BallotTree roots={roots} projects={projects} now={now} />
    </MemoryRouter>,
  );

describe('BallotTree', () => {
  it('says nothing is waiting, in the attention panel\'s words, with no roots', () => {
    const html = render([]);

    expect(html).toContain(questionEmptyWords());
    expect(html).toContain('Nothing is waiting on an answer.');
    expect(html).not.toContain('hatch-ballot-node');
  });

  it('draws a root as a container: mark, type, key link, title and excerpt', () => {
    const html = render([node({ excerpt: 'First line.\nSecond line.' })]);

    expect(html).toContain('hatch-ballot-node top');
    expect(html).toContain('href="/issues/PRJ-1"');
    expect(html).toContain('The epic');
    expect(html).toContain('First line.\nSecond line.');
    expect(html).toContain('hatch-ballot-excerpt');
  });

  it('draws the excerpt as the text it is, never as markdown', () => {
    const html = render([node({ excerpt: '**bold** and <b>tags</b>' })]);

    expect(html).toContain('**bold** and &lt;b&gt;tags&lt;/b&gt;');
    expect(html).not.toContain('<strong>');
    expect(html).not.toContain('<b>');
  });

  it('draws no excerpt paragraph for an issue with none', () => {
    expect(render([node()])).not.toContain('hatch-ballot-excerpt');
  });

  it('nests a child inside its parent\'s container, one nested box per level', () => {
    const grandchild = node({ key: 'PRJ-3', type: 'task', title: 'The task' });
    const child = node({ key: 'PRJ-2', type: 'story', title: 'The story', children: [grandchild] });
    const html = render([node({ children: [child] })]);

    const root = html.indexOf('The epic');
    const story = html.indexOf('The story');
    const task = html.indexOf('The task');
    expect(root).toBeLessThan(story);
    expect(story).toBeLessThan(task);
    expect(html.split('hatch-ballot-nested').length - 1).toBe(2);
    expect(html.split('hatch-ballot-node top').length - 1).toBe(1);
  });

  it('draws a question under its own issue: body, asker, how long it has waited, and a link', () => {
    const html = render([node({ questions: [question()] })]);

    expect(html).toContain('Which of the two should it be?');
    expect(html).toContain('a-runner');
    expect(html).toContain(waitedWords(askedAt, now));
    expect(html).toContain('3 hours');
    expect(html).toContain('href="/issues/PRJ-3"');
  });

  it('marks the recommended option, and draws no options for a question asked in prose', () => {
    const withOptions = render([
      node({
        questions: [
          question({
            options: [
              { label: 'keep', detail: 'Costs nothing', recommended: true },
              { label: 'drop', detail: null, recommended: false },
            ],
          }),
        ],
      }),
    ]);

    expect(withOptions).toContain('keep');
    expect(withOptions).toContain('Costs nothing');
    expect(withOptions.split('recommended</span>').length - 1).toBe(1);
    expect(withOptions).not.toContain('<button');

    expect(render([node({ questions: [question()] })])).not.toContain('hatch-options');
  });

  it('draws a terminal ancestor like any other', () => {
    const html = render([node({ isTerminal: true, children: [node({ key: 'PRJ-2', type: 'task' })] })]);

    expect(html).toContain('href="/issues/PRJ-1"');
    expect(html).toContain('href="/issues/PRJ-2"');
  });

  it('draws the mark in the project\'s own colour, and in letters for a project it does not know', () => {
    const known = { key: 'PRJ', name: 'A project', color: '#336699', icon: null, logoUpdatedAt: null } as Project;

    expect(render([node()], [known])).toContain('title="A project"');
    expect(render([node()])).toContain('title="PRJ"');
  });
});
