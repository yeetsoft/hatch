import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { ProjectsFacet } from './ProjectsFacet';
import type { Project } from '../types';

const noop = () => {};
const projects = [
  { key: 'HA', name: 'Hatch' },
  { key: 'AER', name: 'Aerie' },
  { key: 'GL', name: 'glasshole' },
] as Project[];

function render(chosen: string[]) {
  return renderToStaticMarkup(<ProjectsFacet projects={projects} chosen={chosen} onChange={noop} />);
}

describe('ProjectsFacet', () => {
  it('draws KEY — Name rows, no count span', () => {
    const html = render(projects.map((p) => p.key));
    expect(html).toContain('<span class="hatch-types-name">HA — Hatch</span>');
    expect(html).toContain('<span class="hatch-types-name">AER — Aerie</span>');
    expect(html).toContain('<span class="hatch-types-name">GL — glasshole</span>');
    expect(html).not.toContain('hatch-types-count');
  });

  it('labels the panel Projects', () => {
    expect(render(['HA'])).toContain('aria-label="Projects"');
  });
});
