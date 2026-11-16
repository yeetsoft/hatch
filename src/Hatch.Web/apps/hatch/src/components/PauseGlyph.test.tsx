import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { PauseGlyph } from './PauseGlyph';

function fills(html: string): string[] {
  return [...html.matchAll(/fill="([^"]*)"/g)].map((m) => m[1]);
}

describe('PauseGlyph', () => {
  it('outlines both bars at rest', () => {
    expect(fills(renderToStaticMarkup(<PauseGlyph state="outline" />))).toEqual(['none', 'none']);
  });

  it('fills both bars at its own level', () => {
    expect(fills(renderToStaticMarkup(<PauseGlyph state="filled" />))).toEqual(['currentColor', 'currentColor']);
  });

  it('fills only the left bar while active', () => {
    expect(fills(renderToStaticMarkup(<PauseGlyph state="half" />))).toEqual(['currentColor', 'none']);
  });
});
