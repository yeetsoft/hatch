import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { EpicCard } from './PlanPage';
import type { IssueCard, PlanEntry, Status } from '../types';

/* No DOM runner: the card is rendered from props, the way TimelinePage.test.tsx
   does it. */
const statuses = [{ id: 1, name: 'To Do', color: '#3366cc' }] as Status[];

const rollup = { leaves: 1, done: 0, waiting: 0, slices: [{ statusId: 1, count: 1 }] };

const entry = (key: string, over: Partial<IssueCard> = {}, rest: Partial<PlanEntry> = {}): PlanEntry => ({
  issue: { key, projectKey: key.split('-')[0], type: 'epic', title: `title ${key}`, statusId: 1, ...over } as IssueCard,
  isLeaf: false,
  rollup,
  children: [],
  ...rest,
});

const render = (e: PlanEntry): string =>
  renderToStaticMarkup(
    <MemoryRouter>
      <EpicCard entry={e} statuses={statuses} />
    </MemoryRouter>,
  );

describe('EpicCard across projects', () => {
  it('names the project of a nested epic from another project, and only that one', () => {
    const html = render(
      entry('AER-1', {}, { children: [entry('OPS-1', { parentKey: 'AER-1' }), entry('AER-2', { parentKey: 'AER-1' })] }),
    );

    expect(html.match(/hatch-plan-project/g)).toHaveLength(1);
    expect(html).toContain('<span class="hatch-plan-project">OPS</span>');
  });

  it('links the parent from a root whose parent the reader may see', () => {
    const html = render(entry('OPS-1', { parentKey: 'AER-1' }));

    expect(html).toContain('Part of');
    expect(html).toContain('href="/issues/AER-1"');
    expect(html).not.toContain('outside your scope');
  });

  it('says only that a root has a parent the reader may not see', () => {
    const html = render(entry('OPS-1', { parentKey: null, parentBeyondScope: true }));

    expect(html).toContain('Part of work outside your scope');
    expect(html).not.toContain('AER');
  });

  it('says where the tree goes on, and draws no bar for a leaf whose children are hidden', () => {
    const hidden = render(entry('AER-1', { childBeyondScope: true }, { isLeaf: true }));
    expect(hidden).toContain('More of this tree is outside your scope');
    expect(hidden).not.toContain('hatch-meter');

    // With children the reader can see, the bar stays.
    const partly = render(entry('AER-1', { childBeyondScope: true }));
    expect(partly).toContain('More of this tree is outside your scope');
    expect(partly).toContain('hatch-meter');
  });

  it('draws a plain root with none of it', () => {
    const html = render(entry('AER-1'));

    expect(html).not.toContain('Part of');
    expect(html).not.toContain('outside your scope');
    expect(html).not.toContain('hatch-plan-project');
  });
});
