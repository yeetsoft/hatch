import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { TypeBadge } from './TypeBadge';
import { TypeTile } from './TypeGlyph';
import { ISSUE_TYPES } from '../types';

describe('TypeBadge', () => {
  it.each(ISSUE_TYPES)('wears the shared face for %s', (type) => {
    const html = renderToStaticMarkup(<TypeBadge type={type} />);
    const classes = /class="([^"]*)"/.exec(html)![1].split(' ');
    expect(classes).toContain('hatch-type-badge');
    expect(classes).toContain(type);
    expect(html).toContain(`>${type}</span>`);
  });

  it.each(ISSUE_TYPES)('shows the %s tile, hidden from assistive technology, before the word', (type) => {
    const html = renderToStaticMarkup(<TypeBadge type={type} />);
    // The ambition's gradient id is per tile, so compare the markup with the id taken out.
    const sameTile = (markup: string) => markup.replace(/hatch-ambition-tile-[^")]+/g, 'id');
    const tile = renderToStaticMarkup(<TypeTile type={type} />);
    expect(sameTile(html)).toContain(
      `<span class="hatch-type-pill-glyph" aria-hidden="true">${sameTile(tile)}</span>${type}</span>`,
    );
  });

  it.each(ISSUE_TYPES)('keeps the word as the only text for %s', (type) => {
    const html = renderToStaticMarkup(<TypeBadge type={type} />);
    expect(html.replace(/<[^>]*>/g, '')).toBe(type);
  });
});
