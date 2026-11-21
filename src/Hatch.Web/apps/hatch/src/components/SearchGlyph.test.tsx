import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { SearchGlyph } from './SearchGlyph';

describe('SearchGlyph', () => {
  it('draws a circle and a handle, hidden from assistive tech', () => {
    const html = renderToStaticMarkup(<SearchGlyph />);
    expect(html).toContain('<circle cx="10.5" cy="10.5" r="6.5"');
    expect(html).toContain('<path d="M15.5 15.5 21 21"');
    expect(html).toContain('aria-hidden="true"');
  });

  it('is 20px square, the gear trigger\'s own size', () => {
    const html = renderToStaticMarkup(<SearchGlyph />);
    expect(html).toContain('width="20"');
    expect(html).toContain('height="20"');
  });
});
