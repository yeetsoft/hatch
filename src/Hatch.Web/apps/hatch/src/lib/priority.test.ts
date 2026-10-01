import { describe, expect, it } from 'vitest';
import { priorityLandingFocus } from './priority';

const levels = [{ name: 'emergency' }, { name: 'expedited' }, { name: 'normal' }];

describe('priorityLandingFocus', () => {
  it('lands on the entry after the current one', () => {
    expect(priorityLandingFocus(levels, 'emergency')).toBe('expedited');
  });

  it('wraps to the first entry, from the last one', () => {
    expect(priorityLandingFocus(levels, 'normal')).toBe('emergency');
  });

  it('lands on the first entry, from a name the list does not recognise', () => {
    expect(priorityLandingFocus(levels, 'economy')).toBe('emergency');
  });

  it('has nowhere to land on an empty list', () => {
    expect(priorityLandingFocus([], 'normal')).toBeNull();
  });
});
