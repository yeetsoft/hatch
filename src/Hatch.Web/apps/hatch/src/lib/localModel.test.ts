import { describe, expect, it } from 'vitest';
import { LOCAL_MODEL_LEVELS, localModelFact } from './localModel';

describe('LOCAL_MODEL_LEVELS', () => {
  it('are in the band order', () => {
    expect(LOCAL_MODEL_LEVELS.map((level) => level.name)).toEqual(['none', 'prefer', 'require']);
    expect(LOCAL_MODEL_LEVELS.map((level) => level.word)).toEqual(['None', 'Prefer', 'Require']);
  });

  it('draws a glyph for prefer and require only', () => {
    expect(LOCAL_MODEL_LEVELS.map((level) => level.glyph !== '')).toEqual([false, true, true]);
  });
});

describe('localModelFact', () => {
  it('reads None for none, whatever the source', () => {
    expect(localModelFact('none', null)).toBe('None');
    expect(localModelFact('none', 'HA-1')).toBe('None');
  });

  it('reads the word alone for an own level', () => {
    expect(localModelFact('prefer', null)).toBe('Prefer');
    expect(localModelFact('require', null)).toBe('Require');
  });

  it('names the ancestor for an inherited level', () => {
    expect(localModelFact('prefer', 'HA-2405')).toBe('Prefer (inherited from HA-2405)');
  });
});
