import { describe, expect, it } from 'vitest';
import {
  isScroller,
  isVerticalPull,
  mayRelease,
  mayStart,
  PULL_START_PX,
  PULL_THRESHOLD_PX,
  pullPhase,
} from './pullToRefresh';

describe('pullPhase', () => {
  it('is idle at 0', () => {
    expect(pullPhase(0)).toBe('idle');
  });

  it('is idle just below the start distance', () => {
    expect(pullPhase(PULL_START_PX - 1)).toBe('idle');
  });

  it('is pulling at the start distance', () => {
    expect(pullPhase(PULL_START_PX)).toBe('pulling');
  });

  it('is pulling just below the threshold', () => {
    expect(pullPhase(PULL_THRESHOLD_PX - 1)).toBe('pulling');
  });

  it('is ready at the threshold', () => {
    expect(pullPhase(PULL_THRESHOLD_PX)).toBe('ready');
  });

  it('is ready past the threshold', () => {
    expect(pullPhase(PULL_THRESHOLD_PX + 50)).toBe('ready');
  });
});

describe('mayStart', () => {
  it('may start when standalone, at top, and no dialog is open', () => {
    expect(mayStart({ standalone: true, atTop: true, dialogOpen: false })).toBe(true);
  });

  it('refuses when not standalone', () => {
    expect(mayStart({ standalone: false, atTop: true, dialogOpen: false })).toBe(false);
  });

  it('refuses when not at top', () => {
    expect(mayStart({ standalone: true, atTop: false, dialogOpen: false })).toBe(false);
  });

  it('refuses when a dialog is open', () => {
    expect(mayStart({ standalone: true, atTop: true, dialogOpen: true })).toBe(false);
  });
});

describe('isVerticalPull', () => {
  it('is vertical when deltaY dominates', () => {
    expect(isVerticalPull(5, 20)).toBe(true);
  });

  it('is not vertical when deltaX dominates', () => {
    expect(isVerticalPull(20, 5)).toBe(false);
  });

  it('is not vertical on a tie, pulling down', () => {
    expect(isVerticalPull(10, 10)).toBe(false);
  });

  it('is not vertical on a tie, pulling up', () => {
    expect(isVerticalPull(10, -10)).toBe(false);
  });
});

describe('mayRelease', () => {
  it('releases when ready', () => {
    expect(mayRelease('ready')).toBe(true);
  });

  it('does not release when idle', () => {
    expect(mayRelease('idle')).toBe(false);
  });

  it('does not release while pulling', () => {
    expect(mayRelease('pulling')).toBe(false);
  });
});

describe('isScroller', () => {
  it('treats auto as a scroller', () => {
    expect(isScroller('auto')).toBe(true);
  });

  it('treats scroll as a scroller', () => {
    expect(isScroller('scroll')).toBe(true);
  });

  it('treats overlay as a scroller', () => {
    expect(isScroller('overlay')).toBe(true);
  });

  it('does not treat visible as a scroller', () => {
    expect(isScroller('visible')).toBe(false);
  });

  it('does not treat hidden as a scroller', () => {
    expect(isScroller('hidden')).toBe(false);
  });
});
