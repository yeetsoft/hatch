import { describe, expect, it } from 'vitest';
import { createMenuBar } from '@hatch/ui/menuState';

/* This lives in apps/hatch because packages/ui has no test runner and
   `make test-web` only loops over the apps. It covers the timing and the
   one-open-per-bar rule, which is the state machine. What it cannot reach - no
   jsdom here - is the DOM around it in Menu.tsx: Escape returning focus to the
   trigger, focusout, outside pointerdown, ArrowDown. Those are checked in the
   design gallery by the operator. */

function harness() {
  let now = 0;
  let nextHandle = 1;
  const timers = new Map<number, { at: number; fn: () => void }>();
  const bar = createMenuBar({
    setTimeout: (fn, ms) => {
      const handle = nextHandle++;
      timers.set(handle, { at: now + ms, fn });
      return handle;
    },
    clearTimeout: (handle) => {
      timers.delete(handle as number);
    },
  });
  function advance(ms: number) {
    const target = now + ms;
    for (;;) {
      const due = [...timers.entries()].filter(([, t]) => t.at <= target).sort((a, b) => a[1].at - b[1].at)[0];
      if (!due) break;
      timers.delete(due[0]);
      now = due[1].at;
      due[1].fn();
    }
    now = target;
  }
  return { bar, advance, pending: () => timers.size };
}

describe('menu bar', () => {
  it('opens on hover only after the intent delay', () => {
    const { bar, advance } = harness();
    bar.pointerEnter('a', true);
    advance(119);
    expect(bar.openId).toBeNull();
    advance(1);
    expect(bar.openId).toBe('a');
  });

  it('does not open if the pointer leaves before the delay', () => {
    const { bar, advance } = harness();
    bar.pointerEnter('a', true);
    advance(60);
    bar.pointerLeave('a');
    advance(500);
    expect(bar.openId).toBeNull();
  });

  it('closes after the grace period, not before', () => {
    const { bar, advance } = harness();
    bar.open('a');
    bar.pointerLeave('a');
    advance(199);
    expect(bar.openId).toBe('a');
    advance(1);
    expect(bar.openId).toBeNull();
  });

  it('stays open when the pointer comes back within the grace', () => {
    const { bar, advance } = harness();
    bar.open('a');
    bar.pointerLeave('a');
    advance(150);
    bar.pointerEnter('a', true);
    advance(500);
    expect(bar.openId).toBe('a');
  });

  it('ignores hover when the pointer cannot hover', () => {
    const { bar, advance, pending } = harness();
    bar.pointerEnter('a', false);
    expect(pending()).toBe(0);
    advance(1000);
    expect(bar.openId).toBeNull();
  });

  it('switches at once to another menu in the bar, never two open', () => {
    const { bar, advance } = harness();
    bar.open('a');
    bar.pointerLeave('a');
    bar.pointerEnter('b', true);
    expect(bar.openId).toBe('b');
    expect(bar.isOpen('a')).toBe(false);
    advance(1000);
    expect(bar.openId).toBe('b');
  });

  it('toggles immediately', () => {
    const { bar } = harness();
    bar.toggle('a');
    expect(bar.openId).toBe('a');
    bar.toggle('a');
    expect(bar.openId).toBeNull();
  });

  it('close(id) for a menu that is not open does nothing', () => {
    const { bar } = harness();
    bar.open('a');
    bar.close('b');
    expect(bar.openId).toBe('a');
    bar.close('a');
    expect(bar.openId).toBeNull();
  });

  it('an explicit close cancels a pending hover open', () => {
    const { bar, advance } = harness();
    bar.pointerEnter('a', true);
    advance(50);
    bar.close();
    advance(500);
    expect(bar.openId).toBeNull();
  });

  it('an explicit open cancels a pending close', () => {
    const { bar, advance } = harness();
    bar.open('a');
    bar.pointerLeave('a');
    bar.open('a');
    advance(500);
    expect(bar.openId).toBe('a');
  });

  it('notifies subscribers only when the open menu changes', () => {
    const { bar } = harness();
    let calls = 0;
    const off = bar.subscribe(() => calls++);
    bar.open('a');
    bar.open('a');
    bar.close('a');
    off();
    bar.open('a');
    expect(calls).toBe(2);
  });
});
