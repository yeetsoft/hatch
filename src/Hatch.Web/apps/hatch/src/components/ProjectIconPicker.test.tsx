import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { PROJECT_ICONS } from '@hatch/ui';
import { ProjectIconPicker } from './ProjectIconPicker';

const noop = () => {};

describe('ProjectIconPicker', () => {
  it('draws None plus every PROJECT_ICONS entry, one label each', () => {
    const html = renderToStaticMarkup(<ProjectIconPicker value={null} color={null} onChange={noop} />);

    const labels = [...html.matchAll(/<label /g)];
    expect(labels).toHaveLength(PROJECT_ICONS.length + 1);
    expect(html).toContain('>None<');
    for (const { title } of PROJECT_ICONS) {
      expect(html).toContain(`title="${title}"`);
    }
  });

  it('checks exactly None when value is null', () => {
    const html = renderToStaticMarkup(<ProjectIconPicker value={null} color={null} onChange={noop} />);

    const checked = [...html.matchAll(/checked=""/g)];
    expect(checked).toHaveLength(1);
    expect(html.indexOf('checked=""')).toBeLessThan(html.indexOf('>None<'));
  });

  it('checks exactly the option matching a real slug', () => {
    const slug = PROJECT_ICONS[0].slug;
    const html = renderToStaticMarkup(<ProjectIconPicker value={slug} color={null} onChange={noop} />);

    const checked = [...html.matchAll(/checked=""/g)];
    expect(checked).toHaveLength(1);
    expect(html.indexOf('checked=""')).toBeLessThan(html.indexOf(`title="${PROJECT_ICONS[0].title}"`));
  });

  it('draws the colour only on the selected mark', () => {
    const slug = PROJECT_ICONS[1].slug;
    const html = renderToStaticMarkup(<ProjectIconPicker value={slug} color="#336699" onChange={noop} />);

    const colored = [...html.matchAll(/--mark-color/g)];
    expect(colored).toHaveLength(1);
  });
});
