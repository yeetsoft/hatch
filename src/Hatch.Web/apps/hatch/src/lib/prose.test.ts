import { describe, expect, it } from 'vitest';
import { clampBlocks, splitBlocks } from './prose';

const FENCE = '```\ncode line 1\n\ncode line 2\n```';
const LOOSE_LIST = '- one\n\n- two\n\n- three';
const BLOCKQUOTE = '> one\n>\n> two';
const TABLE = '| a | b |\n| - | - |\n| 1 | 2 |';

describe('splitBlocks', () => {
  it('keeps a fence with a blank line inside it as one block', () => {
    expect(splitBlocks(FENCE)).toEqual([FENCE]);
  });

  it('keeps a loose list as one block', () => {
    expect(splitBlocks(LOOSE_LIST)).toEqual([LOOSE_LIST]);
  });

  it('keeps a block quote as one block', () => {
    expect(splitBlocks(BLOCKQUOTE)).toEqual([BLOCKQUOTE]);
  });

  it('keeps a table as one block', () => {
    expect(splitBlocks(TABLE)).toEqual([TABLE]);
  });

  it('drops leading and trailing blank runs', () => {
    expect(splitBlocks('\n\none\n\n\ntwo\n\n')).toEqual(['one', 'two']);
  });

  it('is empty for the empty string', () => {
    expect(splitBlocks('')).toEqual([]);
  });
});

describe('clampBlocks', () => {
  it('is not clamped when the source is short', () => {
    const result = clampBlocks('one\n\ntwo', 3);

    expect(result).toEqual({ head: 'one\n\ntwo', clamped: false });
  });

  it('is not clamped at exactly the limit', () => {
    const result = clampBlocks('one\n\ntwo\n\nthree', 3);

    expect(result).toEqual({ head: 'one\n\ntwo\n\nthree', clamped: false });
  });

  it('clamps a long source at the right boundary', () => {
    const result = clampBlocks('one\n\ntwo\n\nthree\n\nfour', 3);

    expect(result).toEqual({ head: 'one\n\ntwo\n\nthree', clamped: true });
  });

  it('never cuts inside a fence holding a blank line', () => {
    const source = `intro\n\n${FENCE}\n\nafter\n\nlast`;
    const result = clampBlocks(source, 2);

    expect(result).toEqual({ head: `intro\n\n${FENCE}`, clamped: true });
  });

  it('keeps a loose list whole when it is the block cut after', () => {
    const source = `${LOOSE_LIST}\n\nafter`;
    const result = clampBlocks(source, 1);

    expect(result).toEqual({ head: LOOSE_LIST, clamped: true });
  });

  it('keeps a block quote whole when it is the block cut after', () => {
    const source = `${BLOCKQUOTE}\n\nafter`;
    const result = clampBlocks(source, 1);

    expect(result).toEqual({ head: BLOCKQUOTE, clamped: true });
  });

  it('keeps a table whole when it is the block cut after', () => {
    const source = `${TABLE}\n\nafter`;
    const result = clampBlocks(source, 1);

    expect(result).toEqual({ head: TABLE, clamped: true });
  });

  it('is empty and not clamped for the empty string', () => {
    expect(clampBlocks('', 3)).toEqual({ head: '', clamped: false });
  });

  it('ignores leading and trailing blank runs when counting blocks', () => {
    const result = clampBlocks('\n\none\n\ntwo\n\n', 3);

    expect(result).toEqual({ head: '\n\none\n\ntwo\n\n', clamped: false });
  });
});
