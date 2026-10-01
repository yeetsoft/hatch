import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { PriorityControl } from './PriorityControl';
import type { AssigneeDirectory } from '../types';

const directory = (kind: 'person' | 'key' = 'person'): AssigneeDirectory => ({
  me: { kind, id: 'ada', name: 'Ada' },
  assignees: [],
});

describe('PriorityControl', () => {
  it('draws the pill for an issue holding its own level, closed', () => {
    const html = renderToStaticMarkup(
      <PriorityControl
        issueKey="HA-14"
        priority="expedited"
        priorityOwn="expedited"
        priorityFrom={null}
        directory={directory()}
        onChange={() => {}}
      />,
    );

    expect(html).toContain('Expedited');
    expect(html).toContain('aria-label="Priority: Expedited. Set HA-14&#x27;s priority"');
    expect(html).toContain('aria-expanded="false"');
    expect(html).not.toContain('inherited from');
  });

  it('names the ancestor on a level this issue inherits', () => {
    const html = renderToStaticMarkup(
      <PriorityControl
        issueKey="HA-14"
        priority="expedited"
        priorityOwn="normal"
        priorityFrom="HA-12"
        directory={directory()}
        onChange={() => {}}
      />,
    );

    expect(html).toContain(
      'aria-label="Priority: Expedited, inherited from HA-12. Set HA-14&#x27;s priority"',
    );
  });

  it('is busy and disabled while a press is out', () => {
    const html = renderToStaticMarkup(
      <PriorityControl
        issueKey="HA-14"
        priority="normal"
        priorityOwn="normal"
        priorityFrom={null}
        directory={directory()}
        busy
        onChange={() => {}}
      />,
    );

    expect(html).toContain('aria-busy="true"');
    expect(html).toContain('disabled=""');
  });

  it('draws the word and the ancestor key for a key, with no button', () => {
    const html = renderToStaticMarkup(
      <PriorityControl
        issueKey="HA-14"
        priority="emergency"
        priorityOwn="normal"
        priorityFrom="HA-12"
        directory={directory('key')}
        onChange={() => {}}
      />,
    );

    expect(html).toContain('Emergency');
    expect(html).toContain('inherited from HA-12');
    expect(html).not.toContain('<button');
  });

  it('draws the word alone for a key holding its own level', () => {
    const html = renderToStaticMarkup(
      <PriorityControl
        issueKey="HA-14"
        priority="normal"
        priorityOwn="normal"
        priorityFrom={null}
        directory={directory('key')}
        onChange={() => {}}
      />,
    );

    expect(html).toContain('Normal');
    expect(html).not.toContain('inherited from');
    expect(html).not.toContain('<button');
  });
});
