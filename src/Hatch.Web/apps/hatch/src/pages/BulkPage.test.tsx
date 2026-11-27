import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { BulkParentChoice, BulkProjectFilter } from './BulkPage';
import { KEEP, PICK } from '../lib/bulk';
import type { IssueCard, Project } from '../types';

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

describe('BulkParentChoice', () => {
  const cards = [{ key: 'AER-9', title: 'Long closed epic', type: 'epic' }] as IssueCard[];
  const render = (value: string): string =>
    renderToStaticMarkup(
      <BulkParentChoice
        value={value}
        onChange={() => {}}
        selectLabel="Parent change"
        pickerLabel="Parent change issue"
        keepLabel="— leave alone —"
        candidates={cards}
        scopeIds={[1]}
      />,
    );

  it('offers the two fixed choices and "An issue"', () => {
    const html = render(KEEP);
    expect(html).toContain('— leave alone —');
    expect(html).toContain('— no parent —');
    expect(html).toContain('An issue');
    expect(html).toContain('aria-label="Parent change"');
  });

  it('draws the picker only once "An issue" is chosen', () => {
    expect(render(KEEP)).not.toContain('hatch-issue-picker');
    expect(render('')).not.toContain('hatch-issue-picker');
    expect(render(PICK)).toContain('hatch-issue-picker');
    expect(render('AER-9')).toContain('hatch-issue-picker');
  });

  it('asks for a pick until there is one, then reads the key from the cards', () => {
    expect(render(PICK)).toContain('Choose an issue…');
    expect(render('AER-9')).toContain('AER-9 — Long closed epic');
  });
});
