import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { ThemeProvider } from '@hatch/ui';
import { DocsPage } from './DocsPage';

// The real sanitizer needs a DOM, which this workspace's tests do not have -
// see ProseClamp.test.tsx. The identity stub still shows what the page handed it.
vi.mock('dompurify', () => ({ default: { sanitize: (html: string) => html } }));
vi.mock('../lib/viewport', () => ({ usePhone: () => false }));
// clientLogger.ts reads `window` at import, and there is no jsdom here - the
// same stub PlaybookRow.test.tsx uses for the same reason.
vi.mock('../lib/clientLogger', () => ({ clientLogger: { error: () => {} } }));

// A fixture page carrying a fence, and one that does not - DOC_PAGES has
// neither yet (HA-1059 is the ticket that adds a real one), so this file
// swaps in its own pair rather than reaching for a real page that would stop
// having one the moment HA-1059 lands. vi.hoisted: vi.mock's factory below is
// hoisted above this file's own top, so it cannot close over a plain const.
const { FENCE_PAGE, PLAIN_PAGE } = vi.hoisted(() => ({
  FENCE_PAGE: {
    slug: 'has-a-diagram',
    title: 'Has a diagram',
    section: 'Start here',
    order: 1,
    summary: 'A page with a mermaid fence.',
    body: '## Heading\n\n```mermaid\ngraph TD\n  A --> B\n```\n',
  },
  PLAIN_PAGE: {
    slug: 'plain',
    title: 'Plain',
    section: 'Start here',
    order: 2,
    summary: 'A page with no fence.',
    body: '## Heading\n\nSome prose.\n',
  },
}));

vi.mock('../lib/docs', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../lib/docs')>();
  return { ...actual, DOC_PAGES: actual.orderDocPages([FENCE_PAGE, PLAIN_PAGE]) };
});

function renderAt(path: string): string {
  return renderToStaticMarkup(
    <ThemeProvider>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/docs/*" element={<DocsPage />} />
        </Routes>
      </MemoryRouter>
    </ThemeProvider>,
  );
}

describe('a docs page with a mermaid fence', () => {
  it('shows the placeholder and its un-hidden source, and draws no svg before any script runs', () => {
    const html = renderAt('/docs/has-a-diagram');
    expect(html).toContain('<div class="hatch-docs-mermaid">');
    expect(html).toMatch(/<pre class="hatch-docs-mermaid-source">[^<]*<\/pre>/);
    expect(html).not.toContain('<svg');
    expect(html).not.toContain('hatch-docs-mermaid-error');
  });
});

describe('a docs page with no mermaid fence', () => {
  it('carries no mermaid markup at all', () => {
    const html = renderAt('/docs/plain');
    expect(html).not.toContain('hatch-docs-mermaid');
  });
});
