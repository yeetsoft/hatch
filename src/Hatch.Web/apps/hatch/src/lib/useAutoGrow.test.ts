import { describe, expect, it } from 'vitest';
import { clampedHeight, grownHeight, parseCeiling } from './useAutoGrow';

/* The hook itself is layout - it has to be looked at in a browser, and the
   operator does that. What can be pinned down here is the arithmetic, which is
   where the two ends of the range and the border-box correction live. */
describe('grownHeight', () => {
  it('takes the height the content needs, plus the border', () => {
    expect(grownHeight(100, 400, 2)).toBe(402);
  });

  it('never goes under the height the box opened at', () => {
    expect(grownHeight(402, 90, 2)).toBe(402);
  });

  /* A textarea's scrollHeight is at least its own client height, so a box with
     one line in it measures the floor back and lands exactly on it - which is
     what makes deleting lines shrink to that floor and stop. */
  it('lands on the floor when the content is the floor', () => {
    expect(grownHeight(402, 400, 2)).toBe(402);
  });
});

/* The editor measures its own content and so has a ceiling as well as a floor;
   the textarea's ceiling is CSS's, and never reaches this arithmetic. */
describe('clampedHeight', () => {
  it('takes the height the content needs, plus the border, between the two ends', () => {
    expect(clampedHeight(100, 480, 300, 2)).toBe(302);
  });

  it('never goes under the height the box opened at', () => {
    expect(clampedHeight(100, 480, 40, 2)).toBe(100);
  });

  it('stops at the ceiling', () => {
    expect(clampedHeight(100, 480, 900, 2)).toBe(480);
  });

  it('keeps the floor when the ceiling is under it', () => {
    expect(clampedHeight(200, 150, 900, 2)).toBe(200);
  });

  it('has no ceiling when there is none', () => {
    expect(clampedHeight(100, Infinity, 5000, 2)).toBe(5002);
  });
});

describe('parseCeiling', () => {
  it('reads a length in px', () => {
    expect(parseCeiling('480px')).toBe(480);
    expect(parseCeiling('192.5px')).toBe(192.5);
  });

  it('reads none, empty and the unreadable as no ceiling', () => {
    expect(parseCeiling('none')).toBe(Infinity);
    expect(parseCeiling('')).toBe(Infinity);
    expect(parseCeiling('50%')).toBe(Infinity);
    expect(parseCeiling('max(30rem, 10px)')).toBe(Infinity);
  });
});
