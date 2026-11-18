import { describe, expect, it } from 'vitest';
import { cardFromIssue } from './peekCard';
import type { Issue } from '../types';

/* Only the fields the card is read for; the rest of an Issue rides through the
   spread and is not what these are about. */
const issue = {
  key: 'AER-7',
  projectKey: 'AER',
  type: 'story',
  title: 'A story',
  description: 'The whole brief.',
  statusId: 3,
  parentKey: 'AER-1',
  priority: 'expedited',
  priorityOwn: 'normal',
  priorityFrom: 'AER-1',
  preApproval: 'express',
  preApprovalOwn: 'none',
  preApprovalFrom: 'AER-1',
} as Issue;

describe('cardFromIssue', () => {
  it('keeps the key, column, parent, priority and pre-approval fields', () => {
    const card = cardFromIssue(issue, 2);

    expect(card).toMatchObject({
      key: 'AER-7',
      statusId: 3,
      parentKey: 'AER-1',
      priority: 'expedited',
      priorityOwn: 'normal',
      priorityFrom: 'AER-1',
      preApproval: 'express',
      preApprovalOwn: 'none',
      preApprovalFrom: 'AER-1',
    });
  });

  it('sets the open question count it was given, and a neutral project level', () => {
    const card = cardFromIssue(issue, 2);

    expect(card.openQuestions).toBe(2);
    expect(card.projectPriority).toBe('normal');
  });

  it('carries no description', () => {
    expect(cardFromIssue(issue, 0)).not.toHaveProperty('description');
  });
});
