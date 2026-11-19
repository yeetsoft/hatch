import { describe, expect, it } from 'vitest';
import { DIM_OPACITY, HEATS, STREAMING, TRAILS, WIDE_BOX, tileMarks } from './tileMarks';
import type { Priority } from './tileMarks';
import { PRE_APPROVAL_LEVELS } from './preApproval';
import type { PreApproval } from './preApproval';

const LEVELS = PRE_APPROVAL_LEVELS.map((level) => level.name);
const PRIORITIES: Priority[] = ['paused', 'low', 'normal', 'economy', 'expedited', 'emergency'];

describe('TRAILS', () => {
  it('has a row for every pre-approval level, and only the pre-approved one leans', () => {
    expect(Object.keys(TRAILS).sort()).toEqual([...LEVELS].sort());
    expect(TRAILS.none).toEqual({ lines: '', lean: false });
    expect(TRAILS.express).toEqual({ lines: 'M-7.6 8h5.2', lean: false });
    expect(TRAILS.preapproved).toEqual({ lines: 'M-6.3 4.6h3.6 M-9 8h6.6 M-5.8 11.4h3.2', lean: true });
  });
});

describe('HEATS', () => {
  it('draws no flame for paused, low and normal, a fade for economy, an ember and a flame', () => {
    expect(Object.keys(HEATS).sort()).toEqual([...PRIORITIES].sort());
    for (const priority of ['paused', 'low', 'normal'] as const) {
      expect(HEATS[priority]).toEqual({ flame: 'none', transform: '' });
    }
    expect(HEATS.economy).toEqual({ flame: 'dim', transform: '' });
    expect(HEATS.expedited).toEqual({ flame: 'ember', transform: 'translate(8 9) scale(0.7) translate(-12 -22)' });
    expect(HEATS.emergency).toEqual({ flame: 'upright', transform: 'translate(8 10.5) scale(0.95) translate(-12 -22)' });
  });

  it('streams the flame back over two lines for the pre-approved emergency', () => {
    expect(STREAMING).toEqual({
      transform: 'translate(7 9) rotate(-74) scale(0.9) translate(-12 -22)',
      lines: 'M-9.5 12.6h6.4 M-6.4 15.2h3.6',
    });
  });

  it('fades economy to 40%', () => {
    expect(DIM_OPACITY).toBe(0.4);
  });
});

describe('tileMarks', () => {
  it('is bare, and not wide, for none and normal', () => {
    expect(tileMarks('none', 'normal')).toEqual({
      trail: '',
      lean: false,
      flame: false,
      flameTransform: '',
      dim: false,
      wide: false,
    });
  });

  it('is not wide for economy alone: it only fades', () => {
    expect(tileMarks('none', 'economy')).toMatchObject({ dim: true, flame: false, wide: false });
  });

  it('is wide for a trail alone, and for a flame alone', () => {
    expect(tileMarks('express', 'normal').wide).toBe(true);
    expect(tileMarks('none', 'expedited').wide).toBe(true);
  });

  it('draws an ember and the lean over three lines for pre-approved and expedited', () => {
    expect(tileMarks('preapproved', 'expedited')).toEqual({
      trail: TRAILS.preapproved.lines,
      lean: true,
      flame: true,
      flameTransform: HEATS.expedited.transform,
      dim: false,
      wide: true,
    });
  });

  it('leans with no flame for pre-approved alone', () => {
    expect(tileMarks('preapproved', 'normal')).toMatchObject({ lean: true, flame: false, trail: TRAILS.preapproved.lines });
  });

  it('draws the upright flame over one line for express and emergency', () => {
    expect(tileMarks('express', 'emergency')).toMatchObject({
      trail: TRAILS.express.lines,
      lean: false,
      flame: true,
      flameTransform: HEATS.emergency.transform,
    });
  });

  it('streams the flame over two lines, and leans, only for emergency and pre-approved', () => {
    for (const level of LEVELS) {
      for (const priority of PRIORITIES) {
        const streaming = level === 'preapproved' && priority === 'emergency';
        const marks = tileMarks(level as PreApproval, priority);
        expect(marks.flameTransform === STREAMING.transform, `${level} ${priority}`).toBe(streaming);
        if (streaming) {
          expect(marks.trail).toBe(STREAMING.lines);
          expect(marks.lean).toBe(true);
        }
      }
    }
  });

  it('keeps the wide box 29 by 25 around the 16 box', () => {
    expect(WIDE_BOX).toBe('-12 -8 29 25');
  });
});
