import { describe, expect, it } from 'vitest';
import { budgetDraft, budgetRequest, isReviewPlaybook, transitionLabel } from './playbooks';

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

describe('the budget field', () => {
  it('shows no cap as a blank box', () => {
    expect(budgetDraft(null)).toBe('');
  });

  it('shows a held budget as its number', () => {
    expect(budgetDraft(3)).toBe('3');
  });

  it('trims the field for the wire', () => {
    expect(budgetRequest(' 3 ')).toBe('3');
  });

  it('a blank field clears the budget', () => {
    expect(budgetRequest('  ')).toBe('');
  });
});
