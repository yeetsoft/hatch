import { describe, expect, it } from 'vitest';
import { ISSUE_TYPES } from '../types';
import { ISSUE_TYPE_ROWS, issueTypeRow } from './issueType';

describe('ISSUE_TYPE_ROWS', () => {
  it('has one row per type, in ISSUE_TYPES order', () => {
    expect(ISSUE_TYPE_ROWS.map((row) => row.name)).toEqual(ISSUE_TYPES);
  });

  it('gives each type its own shape', () => {
    expect(new Set(ISSUE_TYPE_ROWS.map((row) => row.shape)).size).toBe(5);
  });

  it('says what each type is, and prints its own name', () => {
    for (const row of ISSUE_TYPE_ROWS) {
      expect(row.title.length).toBeGreaterThan(0);
      expect(row.word).toBe(row.name);
      expect(row.class).toBe(row.name);
    }
  });

  it('finds a row by type', () => {
    expect(issueTypeRow('bug').shape).toBe('triangle');
  });
});
