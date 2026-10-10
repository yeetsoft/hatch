import { describe, expect, it } from 'vitest';
import { heldBy, markState } from './stall';
import type { IssueEvent } from '../types';

const event = (over: Partial<IssueEvent> = {}): IssueEvent => ({
  id: 1,
  actor: 'Nathan',
  kind: 'status_changed',
  payload: null,
  at: '2026-09-28T00:00:00Z',
  ...over,
});

describe('markState', () => {
  it('reads no mark as none', () => {
    expect(markState({ stalledAt: null, held: false })).toBe('none');
  });

  it('reads a stall with no hold as stalled', () => {
    expect(markState({ stalledAt: '2026-09-28T00:00:00Z', held: false })).toBe('stalled');
  });

  it('reads a proactive hold with no prior stall as held, not none', () => {
    expect(markState({ stalledAt: null, held: true })).toBe('held');
  });

  it('reads a held issue that also stalled as held, held taking precedence', () => {
    expect(markState({ stalledAt: '2026-09-28T00:00:00Z', held: true })).toBe('held');
  });
});

describe('heldBy', () => {
  it('reads null with no held event', () => {
    expect(heldBy([event({ kind: 'status_changed' })])).toBeNull();
  });

  it('reads the actor of a held event', () => {
    expect(heldBy([event({ kind: 'held', actor: 'Pam Beesly' })])).toBe('Pam Beesly');
  });

  it('reads the newest held event, first in the newest-first list', () => {
    expect(
      heldBy([
        event({ kind: 'held', actor: 'Pam Beesly', id: 3 }),
        event({ kind: 'resumed', actor: 'Jeff Winger', id: 2 }),
        event({ kind: 'held', actor: 'Jeff Winger', id: 1 }),
      ]),
    ).toBe('Pam Beesly');
  });
});
