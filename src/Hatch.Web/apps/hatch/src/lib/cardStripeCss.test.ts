/* A card's emergency stripe is the flame's amber, so red on a card means a bug
   and nothing else. Read as text, the way pullCss.test.ts reads App.css - there
   is no DOM in this runner to draw a card in. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const css = readFileSync(new URL('../App.css', import.meta.url), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');

function declarationsOf(selector: string): Record<string, string> {
  const props: Record<string, string> = {};
  for (const [, selectors, body] of css.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
    if (!selectors.split(',').some((s) => s.trim() === selector)) continue;
    for (const statement of body.split(';')) {
      const colon = statement.indexOf(':');
      if (colon === -1) continue;
      props[statement.slice(0, colon).trim()] = statement.slice(colon + 1).trim().replace(/\s+/g, ' ');
    }
  }
  return props;
}

describe('the emergency stripe', () => {
  it.each(['.hatch-card.emergency', '.hatch-card-preview.emergency', '.hatch-changelog-report-entry.emergency'])(
    'is the flame colour on %s, never the danger red',
    (selector) => {
      const shadow = declarationsOf(selector)['box-shadow'];
      expect(shadow).toContain('inset 3px 0 0 var(--heat)');
      expect(shadow).not.toContain('--danger');
    },
  );

  it('leaves expedited its blue, and low and economy their greys', () => {
    expect(declarationsOf('.hatch-card.expedited')['box-shadow']).toContain('var(--primary)');
    expect(declarationsOf('.hatch-card.low')['box-shadow']).toContain('var(--muted)');
    expect(declarationsOf('.hatch-card.economy')['box-shadow']).toContain('var(--muted)');
    expect(declarationsOf('.hatch-card.paused')['opacity']).toBe('0.6');
  });

  it('keeps no rule for the marks the tile draws now', () => {
    expect(css).not.toContain('hatch-card-express');
    expect(css).not.toContain('hatch-emergency-beacon');
    expect(css).not.toContain('.hatch-card-expedited.emergency');
    expect(css).not.toContain('.hatch-card-expedited.economy');
  });
});

describe('the tile ground', () => {
  it('is the card on a drag preview and the warn wash on a card owed an answer', () => {
    expect(declarationsOf('.hatch-card-preview')['--tile-ground']).toBe('var(--card)');
    expect(declarationsOf('.hatch-card.asking')['--tile-ground']).toBe('var(--warn-ground)');
  });
});
