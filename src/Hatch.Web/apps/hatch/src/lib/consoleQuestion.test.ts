import { describe, expect, it } from 'vitest';
import { closeQuestion, questionRows } from './consoleQuestion';
import type { CloseOffer } from './closeSubtree';
import type { IssueCard, Status } from '../types';

const offer = (count: number, isDeferred = false): CloseOffer => ({
  key: 'AER-1',
  column: { id: 3, name: isDeferred ? 'Deferred' : 'Done', isDeferred } as Status,
  cards: Array.from({ length: count }, (_, i) => ({ key: `AER-${i + 2}` }) as IssueCard),
});

describe('closeQuestion', () => {
  it('counts the open children', () => {
    expect(closeQuestion(offer(3))).toBe('Close its 3 open children too?');
  });

  it('says child for one', () => {
    expect(closeQuestion(offer(1))).toBe('Close its open child too?');
  });

  it('says shelve under a deferred column, as the dialog does', () => {
    expect(closeQuestion(offer(2, true))).toBe('Shelve its 2 open children too?');
    expect(closeQuestion(offer(1, true))).toBe('Shelve its open child too?');
  });
});

describe('questionRows', () => {
  it('is Yes, then No', () => {
    expect(questionRows()).toEqual([
      { id: 'yes', name: 'Yes' },
      { id: 'no', name: 'No' },
    ]);
  });
});
