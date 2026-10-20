import type { ReactElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter, Route, Routes, useParams } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { IssuePage } from './IssuePage';
import { IssueRoute } from './IssueRoute';

/* The real page reads eight endpoints and pulls in Monaco, and there is no DOM
   here to click a link in - see ProseClamp.test.tsx. What decides whether a
   link to another issue keeps the last one's page is the key on the element
   the route hands React, so a stub that only says which issue it was mounted
   for is enough, and the element itself is what gets checked. */
vi.mock('./IssuePage', () => ({
  IssuePage: function IssuePage() {
    const { key = '' } = useParams();
    return <p>page for {key}</p>;
  },
}));

/** What IssueRoute returns at `/issues/{key}`, captured mid-render. */
function elementAt(key: string): ReactElement {
  const seen: { element: ReactElement | null } = { element: null };
  function Capture() {
    seen.element = IssueRoute();
    return seen.element;
  }
  renderToStaticMarkup(
    <MemoryRouter initialEntries={[`/issues/${key}`]}>
      <Routes>
        <Route path="/issues/:key" element={<Capture />} />
      </Routes>
    </MemoryRouter>,
  );
  if (!seen.element) throw new Error(`IssueRoute drew nothing at /issues/${key}`);
  return seen.element;
}

describe('IssueRoute', () => {
  it('keys the page by the issue, so a link from one issue to another mounts a fresh one', () => {
    const first = elementAt('HA-1');
    const second = elementAt('HA-2');

    expect(first.type).toBe(IssuePage);
    expect(second.type).toBe(IssuePage);
    expect(first.key).toBe('HA-1');
    expect(second.key).toBe('HA-2');
  });

  it('leaves the page reading its issue off the URL', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter initialEntries={['/issues/HA-2']}>
        <Routes>
          <Route path="/issues/:key" element={<IssueRoute />} />
        </Routes>
      </MemoryRouter>,
    );

    expect(html).toContain('page for HA-2');
    expect(html).not.toContain('HA-1');
  });
});
