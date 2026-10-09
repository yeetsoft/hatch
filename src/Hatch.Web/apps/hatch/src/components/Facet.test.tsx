import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { Facet } from './Facet';

const noop = () => {};
const options = [
  { value: '', text: 'All projects' },
  { value: 'HA', text: 'HA — Hatch' },
  { value: 'AER', text: 'AER — Aerie' },
];

function render(value: string, lit = false) {
  return renderToStaticMarkup(<Facet label="Project" lit={lit} value={value} options={options} onChange={noop} />);
}

describe('Facet', () => {
  it('renders the native select inside the label, one option per option', () => {
    const html = render('HA');

    const label = html.match(/<label [^>]*>([\s\S]*)<\/label>/);
    expect(label).not.toBeNull();
    expect(label![1]).toContain('<select');
    expect([...html.matchAll(/<option /g)]).toHaveLength(options.length);
  });

  it('draws the value aria-hidden, with only the selected option shown', () => {
    const html = render('HA');

    expect(html).toContain('class="hatch-facet__value" aria-hidden="true"');
    expect([...html.matchAll(/class="hatch-facet__option[ "]/g)]).toHaveLength(options.length);
    const shown = [...html.matchAll(/<span class="hatch-facet__option hatch-facet__option--shown">([^<]*)</g)];
    expect(shown.map((m) => m[1])).toEqual(['HA — Hatch']);
  });

  it('is lit only when asked', () => {
    expect(render('', true)).toContain('hatch-facet--lit');
    expect(render('')).not.toContain('hatch-facet--lit');
  });
});
