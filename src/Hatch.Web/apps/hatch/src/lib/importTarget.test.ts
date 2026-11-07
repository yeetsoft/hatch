import { describe, expect, it } from 'vitest';
import type { Project } from '../types';
import { importTarget } from './importTarget';

// Only `id` is read, so the fixture is the id alone.
const p = (id: number) => ({ id }) as Project;

describe('importTarget', () => {
  it('chooses nothing for two in scope and no pick', () => {
    expect(importTarget([p(1), p(2)], null, true)).toBeNull();
  });

  it('defaults to the sole project of a scoped tab', () => {
    expect(importTarget([p(7)], null, true)).toBe(7);
  });

  it('does not default on a one-project install that is not scoped', () => {
    expect(importTarget([p(7)], null, false)).toBeNull();
  });

  it('keeps a pick that is in scope', () => {
    expect(importTarget([p(1), p(2)], 2, true)).toBe(2);
  });

  it('keeps a pick of id 0', () => {
    expect(importTarget([p(0), p(2)], 0, false)).toBe(0);
  });

  it('drops a pick outside the scope and falls through to the rule', () => {
    expect(importTarget([p(1), p(2)], 9, true)).toBeNull();
    expect(importTarget([p(1)], 9, true)).toBe(1);
    expect(importTarget([p(1)], 9, false)).toBeNull();
  });

  it('chooses nothing from an empty list', () => {
    expect(importTarget([], null, true)).toBeNull();
    expect(importTarget([], 3, false)).toBeNull();
  });
});
