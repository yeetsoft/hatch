import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { ChildComposer } from './ChildComposer';
import { childTypes } from '../lib/childTypes';
import { IssueConfirmationsContext, type IssueConfirmations } from '../lib/useIssueConfirmations';
import type { IssueType } from '../types';

// A static render cannot press the pill to open its band, so the pill is a
// stub that records what the composer hands it - which is exactly what the
// band would offer.
const seen = vi.hoisted(() => [] as { type: IssueType; types: readonly IssueType[]; label?: string }[]);
vi.mock('./TypeControl', () => ({
  TypeControl: (props: { type: IssueType; types: readonly IssueType[]; label?: string }) => {
    seen.push(props);
    return <button data-pill={props.type}>{props.type}</button>;
  },
}));

const CONFIRMATIONS: IssueConfirmations = {
  confirm: () => undefined,
  moved: () => undefined,
  remanded: () => undefined,
  cascaded: () => undefined,
  undo: () => undefined,
  undoNewest: () => false,
  onUndone: () => () => undefined,
};

function render(parentType: IssueType) {
  return renderToStaticMarkup(
    <IssueConfirmationsContext.Provider value={CONFIRMATIONS}>
      <ChildComposer projectId={1} project={null} parentKey="HA-1" parentType={parentType} onFiled={() => undefined} />
    </IssueConfirmationsContext.Provider>,
  );
}

describe('ChildComposer', () => {
  beforeEach(() => {
    seen.length = 0;
  });

  it.each(['ambition', 'epic', 'reflection', 'story', 'bug'] as const)('offers under %s exactly what childTypes returns', (parent) => {
    render(parent);

    expect(seen).toHaveLength(1);
    expect(seen[0].types).toEqual(childTypes(parent));
  });

  it('offers five under an epic, epic alone under an ambition, task and bug under a story, and task under a bug', () => {
    render('ambition');
    render('epic');
    render('story');
    render('bug');

    expect(seen.map((s) => s.types)).toEqual([
      ['epic'],
      ['epic', 'reflection', 'story', 'task', 'bug'],
      ['task', 'bug'],
      ['task'],
    ]);
  });

  it('draws nothing under a task, and no pill', () => {
    expect(render('task')).toBe('');
    expect(seen).toHaveLength(0);
  });

  it.each([
    ['epic', 'epic'],
    ['reflection', 'epic'],
    ['story', 'task'],
    ['bug', 'task'],
  ] as const)('wears the first legal type under %s: %s', (parent, first) => {
    const html = render(parent);

    expect(seen[0].type).toBe(first);
    expect(html).toContain(`data-pill="${first}"`);
  });

  it('names the new child on the pill, not the parent', () => {
    render('epic');

    expect(seen[0].label).toBe('Type of the new child');
  });

  it('draws no native dropdown', () => {
    expect(render('epic')).not.toContain('<select');
  });
});
