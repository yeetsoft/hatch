import { describe, expect, it } from 'vitest';
import { panelTakesFocus } from '@hatch/ui/modalFocus';

/* No jsdom here, so the rule is driven with stubs. What it cannot reach is the
   effect in Modal.tsx around it: the opener recorded during render and given
   focus back on close. That is covered in the browser, by
   NewIssueFocusTests. */

function stubPanel(...inside: object[]) {
  return { contains: (n: unknown) => inside.includes(n as object) };
}

describe('panelTakesFocus', () => {
  it('takes focus when the panel itself is active', () => {
    const panel = stubPanel();
    expect(panelTakesFocus(panel, panel)).toBe(true);
  });

  it('takes focus when nothing is active', () => {
    expect(panelTakesFocus(stubPanel(), null)).toBe(true);
  });

  it('leaves focus on a child that took it on open', () => {
    const title = {};
    expect(panelTakesFocus(stubPanel(title), title)).toBe(false);
  });

  it('takes focus from an element outside the panel', () => {
    expect(panelTakesFocus(stubPanel({}), {})).toBe(true);
  });
});
