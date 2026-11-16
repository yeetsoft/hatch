import { describe, expect, it } from 'vitest';
import { glyphState, levelAfterPress } from './projectLevel';
import type { ProjectPriority } from '../types';

describe('levelAfterPress', () => {
  it.each<[ProjectPriority, 'expedite' | 'pause', ProjectPriority]>([
    ['normal', 'expedite', 'expedited'],
    ['expedited', 'expedite', 'normal'],
    // The arrow on a paused project expedites it - it does not read back to
    // normal first, since pause is the other glyph's level.
    ['paused', 'expedite', 'expedited'],
    ['normal', 'pause', 'paused'],
    ['paused', 'pause', 'normal'],
    // Pause on an expedited project pauses it, for the same reason.
    ['expedited', 'pause', 'paused'],
  ])('%s, pressing %s, lands on %s', (current, glyph, expected) => {
    expect(levelAfterPress(current, glyph)).toBe(expected);
  });
});

describe('glyphState', () => {
  it.each<[ProjectPriority, 'expedite' | 'pause', boolean, 'outline' | 'filled' | 'half']>([
    ['normal', 'expedite', false, 'outline'],
    ['normal', 'expedite', true, 'half'],
    ['expedited', 'expedite', false, 'filled'],
    ['expedited', 'expedite', true, 'filled'],
    ['paused', 'expedite', false, 'outline'],
    ['normal', 'pause', false, 'outline'],
    ['normal', 'pause', true, 'half'],
    ['paused', 'pause', false, 'filled'],
    ['paused', 'pause', true, 'filled'],
    ['expedited', 'pause', false, 'outline'],
  ])('%s, glyph %s, active %s, draws %s', (current, glyph, active, expected) => {
    expect(glyphState(current, glyph, active)).toBe(expected);
  });
});
