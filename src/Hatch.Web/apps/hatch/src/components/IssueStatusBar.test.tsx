import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { IssueStatusBar } from './IssueStatusBar';
import type { AssigneeDirectory, Issue, Status } from '../types';

const directory = (kind: 'person' | 'key' = 'person'): AssigneeDirectory => ({
  me: { kind, id: 'ada', name: 'Ada' },
  assignees: [],
});

const status = { id: 1, name: 'In Progress', color: '#3b82f6' } as Status;

const issue = (over: Partial<Issue> = {}): Issue =>
  ({
    key: 'HA-14',
    statusId: 1,
    priority: 'normal',
    priorityOwn: 'normal',
    priorityFrom: null,
    preApproval: 'none',
    preApprovalOwn: 'none',
    preApprovalFrom: null,
    localModel: 'none',
    localModelOwn: 'none',
    localModelFrom: null,
    ...over,
  }) as Issue;

const render = (i: Issue, d: AssigneeDirectory) =>
  renderToStaticMarkup(
    <IssueStatusBar
      issue={i}
      statuses={[status]}
      directory={d}
      canApprove={false}
      priorityBusy={false}
      preApprovalBusy={false}
      localModelBusy={false}
      onMove={() => {}}
      blocked={() => null}
      onPriority={() => {}}
      onPreApproval={() => {}}
      onLocalModel={() => {}}
    />,
  );

describe('IssueStatusBar', () => {
  it('reads Status, Priority, Pre-approval, Local model left to right, each a closed pill of the large size', () => {
    const html = render(issue(), directory());

    const at = ['>Status<', '>Priority<', '>Pre-approval<', '>Local model<'].map((label) => html.indexOf(label));
    expect(at.every((i) => i >= 0)).toBe(true);
    expect(at).toEqual([...at].sort((a, b) => a - b));

    expect(html.match(/aria-expanded="false"/g)).toHaveLength(4);
    expect(html).toContain('hatch-status-pill-lg');
    expect(html).toContain('hatch-priority-pill-lg');
    expect(html).toContain('hatch-preapproval-pill-lg');
    expect(html).toContain('hatch-localmodel-pill-lg');
  });

  it('says where an inherited level came from', () => {
    const html = render(
      issue({ priority: 'expedited', priorityFrom: 'HA-1', preApproval: 'express', preApprovalFrom: 'HA-2' }),
      directory(),
    );

    expect(html).toContain('Priority: Expedited, inherited from HA-1');
    expect(html).toContain('Pre-approval: Express, inherited from HA-2');
  });

  it('says where an inherited local-model level came from', () => {
    const html = render(issue({ localModel: 'prefer', localModelFrom: 'HA-3' }), directory());

    expect(html).toContain('Local model: Prefer, inherited from HA-3');
  });

  it('gives a key-holder the words and nothing to press for the three levels', () => {
    const html = render(
      issue({
        priority: 'expedited',
        priorityFrom: 'HA-1',
        preApproval: 'express',
        preApprovalFrom: 'HA-2',
        localModel: 'require',
        localModelFrom: 'HA-3',
      }),
      directory('key'),
    );

    expect(html).toContain('hatch-priority-said');
    expect(html).toContain('Expedited · inherited from HA-1');
    expect(html).toContain('hatch-preapproval-said');
    expect(html).toContain('Express · inherited from HA-2');
    expect(html).toContain('hatch-localmodel-said');
    expect(html).toContain('Require · inherited from HA-3');
    expect(html).not.toContain('hatch-localmodel-pill');
    expect(html).not.toContain('hatch-priority-pill');
    expect(html).not.toContain('hatch-preapproval-pill');
  });
});
