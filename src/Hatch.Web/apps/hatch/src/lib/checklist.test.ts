import { describe, expect, it } from 'vitest';
import { toggleChoice } from './checklist';

const order = ['epic', 'story', 'task'];

describe('toggleChoice', () => {
  it('adds a value that is not chosen', () => {
    expect(toggleChoice(['epic'], 'task', order)).toEqual(['epic', 'task']);
  });

  it('removes a value that is chosen', () => {
    expect(toggleChoice(['epic', 'task'], 'epic', order)).toEqual(['task']);
  });

  it("hands back order's order, whatever order chosen came in", () => {
    expect(toggleChoice(['task', 'epic'], 'story', order)).toEqual(['epic', 'story', 'task']);
  });

  it('returns [] when the only one left is unticked, the guard being the caller\'s', () => {
    expect(toggleChoice(['story'], 'story', order)).toEqual([]);
  });

  it('does not mutate chosen', () => {
    const chosen = ['task', 'epic'];
    toggleChoice(chosen, 'story', order);
    toggleChoice(chosen, 'epic', order);
    expect(chosen).toEqual(['task', 'epic']);
  });

  it('does not add a value that is not in order', () => {
    expect(toggleChoice(['epic'], 'bug', order)).toEqual(['epic']);
  });
});
