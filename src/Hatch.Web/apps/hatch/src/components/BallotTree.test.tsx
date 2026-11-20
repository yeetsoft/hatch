import { afterEach, describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { BallotTree } from './BallotTree';
import { questionEmptyWords } from '../lib/attention';
import type { BallotNode, Project, Question } from '../types';

/* No DOM here, so this is structural - react-dom/server, the way
   AttentionHuman.test.tsx reads. */

/* The answer row renders the question as markdown, whose sanitiser needs a real
   DOM; the identity stub still lets these check what the row decided to show. */
vi.mock('../lib/markdown', () => ({ renderMarkdown: (source: string) => source }));
vi.mock('../lib/clientLogger', () => ({ clientLogger: { info() {}, warn() {}, error() {} } }));

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
  level: 'hold',
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

const noop = () => undefined;

/* appHref and useStandalone read `window`; there is none here. The same stub
   IssuePeek.test.tsx gives its New tab cases. */
const stubWindow = (standalone: boolean) =>
  vi.stubGlobal('window', {
    matchMedia: () => ({ matches: standalone, addEventListener: noop, removeEventListener: noop }),
    navigator: {},
    location: { pathname: '/' },
  });

afterEach(() => vi.unstubAllGlobals());

const render = (roots: BallotNode[], projects: Project[] = [], onPreview: (n: BallotNode) => void = noop) => {
  stubWindow(false);
  return renderToStaticMarkup(
    <MemoryRouter>
      <BallotTree roots={roots} projects={projects} onAnswered={noop} onPreview={onPreview} />
    </MemoryRouter>,
  );
};

describe('BallotTree', () => {
  it('gives a root and a nested child each a Preview button and an Open in new tab link', () => {
    const child = node({ key: 'PRJ-2', type: 'story', title: 'The story' });
    const html = render([node({ children: [child] })]);

    expect(html.split('>Preview<').length - 1).toBe(2);
    for (const key of ['PRJ-1', 'PRJ-2']) {
      const anchor = html.match(new RegExp(`<a [^>]*href="[^"]*/issues/${key}"[^>]*target="_blank"[^>]*>`));
      expect(anchor?.[0]).toContain('rel="noreferrer noopener"');
      // The key itself is still the in-tab link.
      const keyLink = html.match(new RegExp(`<a [^>]*class="hatch-ballot-key"[^>]*>${key}</a>`));
      expect(keyLink?.[0]).toContain(`href="/issues/${key}"`);
      expect(keyLink?.[0]).not.toContain('_blank');
    }
    expect(html.split('Open in new tab').length - 1).toBe(2);
  });

  it('draws no new-tab link in the installed app, which has no second document', () => {
    stubWindow(true);
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <BallotTree roots={[node()]} projects={[]} onAnswered={noop} onPreview={noop} />
      </MemoryRouter>,
    );

    expect(html).toContain('>Preview<');
    expect(html).not.toContain('Open in new tab');
  });

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

  it('draws a question under its own issue: body, asker and when it was asked', () => {
    const html = render([node({ questions: [question()] })]);

    expect(html).toContain('Which of the two should it be?');
    expect(html).toContain('a-runner');
    expect(html).toContain('asked ');
    expect(html).toContain('hatch-question-list');
    // The key link in the issue's header is the one way to the issue.
    expect(html.split('href="/issues/PRJ-3"').length - 1).toBe(0);
  });

  it('says in words what level a question is at', () => {
    const html = render([
      node({
        questions: [
          question({ id: 1, level: 'build', options: [{ label: 'a', detail: null, recommended: true }] }),
          question({ id: 2, level: 'merge', options: [{ label: 'b', detail: null, recommended: true }] }),
        ],
      }),
    ]);

    expect(html).toContain('built but not merged until this is answered');
    expect(html).toContain('merges now; the answer is looked over after');
  });

  it('gives a question the answer box and the Answer button the issue page has', () => {
    const html = render([node({ questions: [question()] })]);

    expect(html).toContain('aria-label="Your answer"');
    expect(html).toContain('The decision, in a sentence.');
    expect(html).toContain('Answer</button>');
  });

  it('draws the options as buttons to press, the recommended one marked, and asks for prose otherwise', () => {
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
    // The two option buttons; the level control's own three are counted apart.
    expect(withOptions.split('<button type="button" class="hatch-option').length - 1).toBe(2);
    expect(withOptions).toContain('Or say something else.');

    expect(render([node({ questions: [question()] })])).not.toContain('hatch-options');
  });

  it('gives each of two questions on one issue its own box', () => {
    const html = render([node({ questions: [question({ id: 1 }), question({ id: 2, body: 'And the other?' })] })]);

    expect(html.split('aria-label="Your answer"').length - 1).toBe(2);
    expect(html.split('Answer</button>').length - 1).toBe(2);
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
