import { describe, expect, it, vi } from 'vitest';
import { glyphState, leveled, levelAfterPress, setLevel } from './projectLevel';
import type { Project, ProjectPriority } from '../types';

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

describe('leveled', () => {
  const at = (key: string, priority: ProjectPriority) => ({ key, priority });

  it('is empty on both sides when every project is normal', () => {
    expect(leveled([at('HA', 'normal'), at('AER', 'normal')])).toEqual({ expedited: [], paused: [] });
    expect(leveled([])).toEqual({ expedited: [], paused: [] });
  });

  it('finds the one expedited project', () => {
    expect(leveled([at('HA', 'normal'), at('AER', 'expedited')])).toEqual({ expedited: [at('AER', 'expedited')], paused: [] });
  });

  it('keeps the list order on each side, for a mix', () => {
    const result = leveled([at('GL', 'paused'), at('HA', 'expedited'), at('X', 'normal'), at('AER', 'expedited'), at('B', 'paused')]);
    expect(result.expedited.map((p) => p.key)).toEqual(['HA', 'AER']);
    expect(result.paused.map((p) => p.key)).toEqual(['GL', 'B']);
  });
});

describe('setLevel', () => {
  const project = { id: 7, key: 'HA', priority: 'expedited' } as Project;

  it('puts the level once, draws it at once, then draws the answer', async () => {
    const answer = { ...project, priority: 'normal', name: 'from the server' } as Project;
    const put = vi.fn().mockResolvedValue(answer);
    const applied: Project[] = [];
    await setLevel(project, 'normal', { put, onApply: (p) => applied.push(p) });
    expect(put).toHaveBeenCalledTimes(1);
    expect(put).toHaveBeenCalledWith({ priority: 'normal' });
    expect(applied).toEqual([{ ...project, priority: 'normal' }, answer]);
  });

  it('puts the original back and throws again when the write is refused', async () => {
    const refusal = new Error('no');
    const put = vi.fn().mockRejectedValue(refusal);
    const applied: Project[] = [];
    await expect(setLevel(project, 'normal', { put, onApply: (p) => applied.push(p) })).rejects.toBe(refusal);
    expect(applied).toEqual([{ ...project, priority: 'normal' }, project]);
  });
});
