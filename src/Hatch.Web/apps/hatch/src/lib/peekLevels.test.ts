import { describe, expect, it } from 'vitest';
import { preApprovalStamp, priorityStamp } from './peekLevels';

const levels = {
  priority: 'normal',
  priorityOwn: 'normal',
  priorityFrom: null,
  preApproval: 'none',
  preApprovalOwn: 'none',
  preApprovalFrom: null,
} as const;

describe('priorityStamp', () => {
  it('is null with no card, as is the pre-approval one', () => {
    expect(priorityStamp(null)).toBeNull();
    expect(preApprovalStamp(null)).toBeNull();
  });

  it('changes when the level in effect, the own level or the source changes', () => {
    const before = priorityStamp(levels);
    expect(priorityStamp({ ...levels, priority: 'paused' })).not.toBe(before);
    expect(priorityStamp({ ...levels, priorityOwn: 'paused' })).not.toBe(before);
    expect(priorityStamp({ ...levels, priorityFrom: 'AER-9' })).not.toBe(before);
  });

  it('does not change when only pre-approval does', () => {
    expect(priorityStamp({ ...levels, preApproval: 'express', preApprovalOwn: 'express' })).toBe(priorityStamp(levels));
  });
});

describe('preApprovalStamp', () => {
  it('changes when the level in effect, the own level or the source changes', () => {
    const before = preApprovalStamp(levels);
    expect(preApprovalStamp({ ...levels, preApproval: 'express' })).not.toBe(before);
    expect(preApprovalStamp({ ...levels, preApprovalOwn: 'express' })).not.toBe(before);
    expect(preApprovalStamp({ ...levels, preApprovalFrom: 'AER-9' })).not.toBe(before);
  });

  it('does not change when only priority does', () => {
    expect(preApprovalStamp({ ...levels, priority: 'paused', priorityOwn: 'paused' })).toBe(preApprovalStamp(levels));
  });
});
