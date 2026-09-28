import { describe, expect, it } from 'vitest';
import { isReviewPlaybook, transitionLabel } from './playbooks';

const move = { fromStatusId: 3, toStatusId: 4, fromStatusName: 'In Progress', toStatusName: 'In Review' };
const review = { fromStatusId: 4, toStatusId: 4, fromStatusName: 'In Review', toStatusName: 'In Review' };

describe('isReviewPlaybook', () => {
  it('is a row whose two ends are the same column', () => {
    expect(isReviewPlaybook(review)).toBe(true);
  });

  it('is not an ordinary move', () => {
    expect(isReviewPlaybook(move)).toBe(false);
  });
});

describe('transitionLabel', () => {
  it('names an ordinary move by its two ends', () => {
    expect(transitionLabel(move)).toBe('In Progress to In Review');
  });

  it('names the review playbook by its column and what it is for', () => {
    expect(transitionLabel(review)).toBe('In Review review');
  });
});
