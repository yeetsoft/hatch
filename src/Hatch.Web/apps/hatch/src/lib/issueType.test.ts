import { describe, expect, it } from 'vitest';
import { ISSUE_TYPES } from '../types';
import { ISSUE_TYPE_ROWS, issueTypeRow } from './issueType';

describe('ISSUE_TYPE_ROWS', () => {
  it('has one row per type, in ISSUE_TYPES order', () => {
    expect(ISSUE_TYPE_ROWS.map((row) => row.name)).toEqual(ISSUE_TYPES);
  });

  it('gives each type its own picture, and a disc only to bug and reflection', () => {
    expect(new Set(ISSUE_TYPE_ROWS.map((row) => row.picture)).size).toBe(ISSUE_TYPES.length);
    const discs = ISSUE_TYPE_ROWS.filter((row) => row.outline === 'disc').map((row) => row.name);
    expect(discs.sort()).toEqual(['bug', 'reflection']);
  });

  it('says what each type is, and prints its own name', () => {
    for (const row of ISSUE_TYPE_ROWS) {
      expect(row.title.length).toBeGreaterThan(0);
      expect(row.word).toBe(row.name);
      expect(row.class).toBe(row.name);
    }
  });

  it('finds a row by type', () => {
    expect(issueTypeRow('bug').picture).toBe('beetle');
    expect(issueTypeRow('bug').outline).toBe('disc');
  });
});
