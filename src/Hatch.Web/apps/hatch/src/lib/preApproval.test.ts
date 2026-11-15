import { describe, expect, it } from 'vitest';
import { PRE_APPROVAL_LEVELS, approvalLocked } from './preApproval';
import type { PreApproval } from './preApproval';

const LEVELS: PreApproval[] = ['none', 'express', 'preapproved'];

describe('PRE_APPROVAL_LEVELS', () => {
  it('are in the band order', () => {
    expect(PRE_APPROVAL_LEVELS.map((level) => level.name)).toEqual(LEVELS);
    expect(PRE_APPROVAL_LEVELS.map((level) => level.word)).toEqual(['None', 'Express', 'Pre-approved']);
  });
});

describe('approvalLocked', () => {
  it('never locks the own level', () => {
    for (const level of LEVELS) expect(approvalLocked(level, level, false)).toBe(false);
  });

  it('never locks an approver', () => {
    for (const level of LEVELS) for (const own of LEVELS) expect(approvalLocked(level, own, true)).toBe(false);
  });

  it('locks setting preapproved, and lowering out of it, for a non-approver', () => {
    expect(approvalLocked('preapproved', 'none', false)).toBe(true);
    expect(approvalLocked('preapproved', 'express', false)).toBe(true);
    expect(approvalLocked('none', 'preapproved', false)).toBe(true);
    expect(approvalLocked('express', 'preapproved', false)).toBe(true);
  });

  it('leaves the moves that never touch preapproved open to a non-approver', () => {
    expect(approvalLocked('none', 'express', false)).toBe(false);
    expect(approvalLocked('express', 'none', false)).toBe(false);
  });
});
