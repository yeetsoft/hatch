import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { PrimaryNav } from './PrimaryNav';

vi.mock('../lib/useMe', () => ({
  useMe: () => ({ me: { kind: 'person', name: 'a', role: 'user' }, isAdmin: false }),
}));

function renderAt(path: string): string {
  return renderToStaticMarkup(
    <MemoryRouter initialEntries={[path]}>
      <PrimaryNav tone="accent" />
    </MemoryRouter>,
  );
}

function searchLink(html: string): string {
  const match = html.match(/<a[^>]*href="\/search"[^>]*>.*?<\/a>/);
  if (!match) throw new Error('Search link not found');
  return match[0];
}

describe('PrimaryNav', () => {
  it('gives the Search link an aria-label and title of "Search", holding an svg and no visible text', () => {
    const link = searchLink(renderAt('/ballot'));
    expect(link).toContain('aria-label="Search"');
    expect(link).toContain('title="Search"');
    expect(link).toContain('<svg');
    expect(link).not.toContain('>Search<');
  });

  it('lights the Search link on /search and not elsewhere, by the same rule as the other links', () => {
    expect(searchLink(renderAt('/search'))).toContain('hatch-menu__trigger--active');
    expect(searchLink(renderAt('/ballot'))).not.toContain('hatch-menu__trigger--active');
  });

  it('still shows Ballot in words', () => {
    const html = renderAt('/ballot');
    expect(html).toContain('>Ballot<');
  });
});
