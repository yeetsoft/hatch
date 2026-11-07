import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { BulkProjectFilter } from './BulkPage';
import type { Project } from '../types';

/* No DOM runner: the filter is rendered from props, the way TimelinePage.test.tsx does it. */
const AER = { id: 1, key: 'AER', name: 'Aerie' } as Project;
const FIL = { id: 2, key: 'FIL', name: 'Filing' } as Project;

const render = (projects: Project[]): string =>
  renderToStaticMarkup(<BulkProjectFilter projects={projects} value="" onChange={() => {}} />);

describe('BulkProjectFilter', () => {
  it('lists the projects it is given, and "any"', () => {
    const html = render([AER, FIL]);
    expect(html.match(/<option/g)).toHaveLength(3);
    expect(html).toContain('AER — Aerie');
    expect(html).toContain('FIL — Filing');
  });

  it('draws nothing for one project, or none', () => {
    expect(render([AER])).toBe('');
    expect(render([])).toBe('');
  });
});
