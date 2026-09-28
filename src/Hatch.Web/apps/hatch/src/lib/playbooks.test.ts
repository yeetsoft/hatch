import { describe, expect, it } from 'vitest';
import { isConflictPlaybook, transitionLabel } from './playbooks';

const move = { fromStatusId: 3, toStatusId: 4, fromStatusName: 'In Progress', toStatusName: 'In Review' };
const conflict = { fromStatusId: 4, toStatusId: 4, fromStatusName: 'In Review', toStatusName: 'In Review' };

describe('isConflictPlaybook', () => {
  it('is a row whose two ends are the same column', () => {
    expect(isConflictPlaybook(conflict)).toBe(true);
  });

  it('is not an ordinary move', () => {
    expect(isConflictPlaybook(move)).toBe(false);
  });
});

describe('transitionLabel', () => {
  it('names an ordinary move by its two ends', () => {
    expect(transitionLabel(move)).toBe('In Progress to In Review');
  });

  it('names the conflict playbook by its column and what it is for', () => {
    expect(transitionLabel(conflict)).toBe('In Review conflicts');
  });
});
