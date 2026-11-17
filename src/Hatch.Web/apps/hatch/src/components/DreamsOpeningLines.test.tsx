/* Read as markup text: this runner has no DOM, as PaletteBoard.test.tsx does. */
import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { DreamsOpeningLines } from './DreamsOpeningLines';

const html = renderToStaticMarkup(<DreamsOpeningLines />);

const items = [...html.matchAll(/<li>([\s\S]*?)<\/li>/g)].map((m) => m[1]);

describe('<DreamsOpeningLines>', () => {
  it('is one list of three items', () => {
    expect(html.match(/<ol[ >]/g)).toHaveLength(1);
    expect(html).toContain('<ol class="hatch-dream-lines" role="list">');
    expect(items).toHaveLength(3);
  });

  it('opens each line with its numeral, hidden from a screen reader', () => {
    const numerals = items.map((li) => li.match(/^<span class="hatch-dream-lines__n" aria-hidden="true">(\d\d)<\/span>/)?.[1]);
    expect(numerals).toEqual(['01', '02', '03']);
  });

  it('sets each leading word in the gradient ink', () => {
    const leads = items.map((li) => li.match(/<span class="hatch-gradient-ink hatch-dream-lines__lead">([^<]*)<\/span>/)?.[1]);
    expect(leads).toEqual(['Drop in', 'Recycle', 'Weave']);
  });

  it('reads as whole sentences and drops the old description', () => {
    const sentences = items.map((li) => li.replace(/<[^>]*>/g, '').replace(/^\d\d/, ''));
    expect(sentences).toEqual([
      'Drop in snippets of ideas that are not epics yet.',
      'Recycle epics that are not ready.',
      'Weave new work out of the cloud of the undefined.',
    ]);
    expect(html).not.toContain('Lines worth keeping before they are work.');
  });
});
