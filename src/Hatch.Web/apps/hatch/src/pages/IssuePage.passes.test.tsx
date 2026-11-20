import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { Passes } from './IssuePage';
import type { AmbitionPass, AmbitionPassVerdict, Comment, Issue } from '../types';

/* No DOM runner, the same reason IssuePage.capabilities.test.tsx gives. */
vi.mock('../lib/clientLogger', () => ({ clientLogger: { info() {}, warn() {}, error() {} } }));

/* renderMarkdown's DOMPurify pair needs a real DOM to sanitize into, which
   this workspace's tests do not have - the same stub ProseClamp.test.tsx
   uses. */
vi.mock('../lib/markdown', () => ({ renderMarkdown: (source: string) => source }));

const ambition = { key: 'AER-1', type: 'ambition' } as Issue;
const epic = { key: 'AER-2', type: 'epic' } as Issue;

const verdict = (over: Partial<AmbitionPassVerdict>): AmbitionPassVerdict => ({
  id: 1,
  capabilityId: 1,
  position: 1,
  statement: 'statement',
  verdict: 'holds',
  lookedAt: 'the tests',
  sha: 'abcdef1234567',
  author: 'nathan',
  at: '2026-01-01T00:00:00Z',
  changed: false,
  ...over,
});

const comment = (over: Partial<Comment>): Comment => ({
  id: 1,
  author: 'someone',
  body: 'said something',
  kind: '',
  answersId: null,
  options: null,
  createdAt: '2026-01-01T00:00:00Z',
  deliveredAt: null,
  deliveredTo: null,
  level: null,
  answeredAfter: false,
  ...over,
});

const pass = (over: Partial<AmbitionPass>): AmbitionPass => ({
  rowId: 1,
  sessionId: 'session-1',
  startedAt: '2026-01-01T00:00:00Z',
  endedAt: '2026-01-01T01:00:00Z',
  title: null,
  summary: null,
  canRecordVerdicts: true,
  sha: 'abcdef1234567',
  verdicts: [],
  comments: [],
  ...over,
});

const render = (passes: AmbitionPass[] | null, issue: Issue = ambition, error: string | null = null): string =>
  renderToStaticMarkup(
    <MemoryRouter>
      <Passes issue={issue} passes={passes} error={error} />
    </MemoryRouter>,
  );

describe('Passes', () => {
  it.each([
    ['an epic', epic],
    ['a story', { key: 'AER-3', type: 'story' } as Issue],
    ['a task', { key: 'AER-4', type: 'task' } as Issue],
  ])('draws nothing on %s', (_, issue) => {
    expect(render([pass({})], issue)).toBe('');
  });

  it('shows an error line rather than blanking the section', () => {
    const html = render(null, ambition, 'could not be read');
    expect(html).toContain('could not be read');
  });

  it('shows Loading… with no error and no passes yet', () => {
    const html = render(null);
    expect(html).toContain('Loading');
  });

  it('reads two passes newest first, each dated and with its sha', () => {
    const html = render([
      pass({ rowId: 2, title: 'Newer pass', endedAt: '2026-01-02T00:00:00Z', sha: '1111111aaaa' }),
      pass({ rowId: 1, title: 'Older pass', endedAt: '2026-01-01T00:00:00Z', sha: '2222222bbbb' }),
    ]);
    expect(html.indexOf('Newer pass')).toBeLessThan(html.indexOf('Older pass'));
    expect(html).toContain('1111111');
    expect(html).toContain('2222222');
    expect(html).not.toContain('1111111aaaa');
    expect(html).toContain('<time');
  });

  it('marks only the verdict whose word changed from the pass before', () => {
    const html = render([
      pass({
        rowId: 1,
        verdicts: [
          verdict({ id: 1, capabilityId: 1, position: 1, changed: true }),
          verdict({ id: 2, capabilityId: 2, position: 2, changed: false }),
        ],
      }),
    ]);
    expect(html).toContain('hatch-pass-changed');
    const firstVerdict = html.indexOf('#1');
    const secondVerdict = html.indexOf('#2');
    expect(html.slice(firstVerdict, secondVerdict)).toContain('changed');
    expect(html.slice(secondVerdict)).not.toContain('changed');
  });

  it('shows the sentence and the comment for an entry whose verdicts could not be recorded', () => {
    const html = render([
      pass({
        rowId: 1,
        canRecordVerdicts: false,
        comments: [comment({ author: 'nathan', body: 'what happened' })],
      }),
    ]);
    expect(html).toContain('Verdicts not recorded as data.');
    expect(html).toContain('what happened');
    expect(html).toContain('nathan');
  });

  it('shows a verdict recorded outside a pass as its own entry', () => {
    const html = render([
      pass({ rowId: null, sessionId: null, title: null, verdicts: [verdict({})] }),
    ]);
    expect(html).toContain('Recorded outside a pass.');
    expect(html).toContain('>Pass<');
  });
});
