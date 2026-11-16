import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { ExpediteGlyph } from './ExpediteGlyph';

describe('ExpediteGlyph', () => {
  it('draws an unfilled outline at rest', () => {
    const html = renderToStaticMarkup(<ExpediteGlyph state="outline" />);
    expect(html).toContain('fill="none"');
    expect(html).not.toContain('<clipPath');
    expect(html).not.toContain('fill="currentColor"');
  });

  it('fills the whole shape at its own level', () => {
    const html = renderToStaticMarkup(<ExpediteGlyph state="filled" />);
    expect(html).toContain('<polygon');
    expect(html).toContain('fill="currentColor"');
    expect(html).not.toContain('fill="none"');
    expect(html).not.toContain('clip-path');
  });

  it('fills only the left half while active, over an outline of the whole shape', () => {
    const html = renderToStaticMarkup(<ExpediteGlyph state="half" />);
    expect(html).toContain('fill="none"');
    expect(html).toContain('<clipPath');
    expect(html).toContain('fill="currentColor"');
    expect([...html.matchAll(/<polygon/g)]).toHaveLength(2);
  });
});
