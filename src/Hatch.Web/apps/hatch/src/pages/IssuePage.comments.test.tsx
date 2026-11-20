import { afterEach, describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { Comments } from './IssuePage';
import type { Comment } from '../types';

/* No DOM runner, the same way IssuePage.capabilities.test.tsx reads: Comments
   is rendered from props alone. MarkdownEditor reads `window.matchMedia` at
   mount, the same stub BallotTree.test.tsx gives its own no-DOM renders. */
vi.mock('../lib/clientLogger', () => ({ clientLogger: { info() {}, warn() {}, error() {} } }));
vi.mock('../lib/markdown', () => ({ renderMarkdown: (source: string) => source }));

const noop = () => undefined;

const stubWindow = () =>
  vi.stubGlobal('window', {
    matchMedia: () => ({ matches: false, addEventListener: noop, removeEventListener: noop }),
    navigator: {},
    location: { pathname: '/' },
  });

afterEach(() => vi.unstubAllGlobals());

const comment = (over: Partial<Comment>): Comment => ({
  id: 1,
  author: 'a-person',
  body: 'Said plainly.',
  kind: '',
  answersId: null,
  options: null,
  createdAt: '2026-01-10T09:00:00Z',
  deliveredAt: null,
  deliveredTo: null,
  level: null,
  answeredAfter: false,
  authorKind: null,
  ...over,
});

const render = (comments: Comment[]) => {
  stubWindow();
  return renderToStaticMarkup(
    <MemoryRouter>
      <Comments issueKey="HA-12" comments={comments} answered={[]} claim={null} onAdded={noop} onError={noop} />
    </MemoryRouter>,
  );
};

describe('Comments', () => {
  it('marks an answer a key wrote as a session\'s, beside its author', () => {
    const html = render([comment({ author: 'hatch-loop', kind: 'answer', authorKind: 'key' })]);

    expect(html).toContain('hatch-loop');
    expect(html).toContain('a session');
  });

  it('marks nothing on a comment a person wrote', () => {
    const html = render([comment({ author: 'a-person', kind: 'answer', authorKind: 'person' })]);

    expect(html).not.toContain('a session');
  });

  it('marks nothing on a comment written before the column existed', () => {
    const html = render([comment({ kind: 'answer', authorKind: null })]);

    expect(html).not.toContain('a session');
  });
});
