import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { WeaveModeControl, WeaveModeSteps } from './WeaveModeControl';
import { WEAVE_MODE_ROWS } from '../lib/dreams';
import type { WeaveMode } from '../types';

const count = (html: string, needle: string) => html.split(needle).length - 1;

describe('WeaveModeControl', () => {
  it('draws the closed pill with the current word, the label and no band', () => {
    const html = renderToStaticMarkup(<WeaveModeControl mode="pressing" onChange={() => {}} />);

    expect(html).toContain('Most pressing');
    expect(html).toContain('aria-label="Mode: Most pressing. The thread the dreams return to with the most urgency."');
    expect(html).toContain('aria-expanded="false"');
    expect(html).toContain('hatch-weave-mode-pill-press');
    expect(html).not.toContain('hatch-weave-mode-pill-band');
    expect(html).not.toContain('Set mode');
  });

});

describe('WeaveModeSteps', () => {
  const steps = (current: WeaveMode = 'seed') =>
    renderToStaticMarkup(<WeaveModeSteps current={current} onPick={() => {}} />);

  it('draws one button per mode under a named group, each with its word and hint', () => {
    const html = steps();

    expect(html).toContain('role="group"');
    expect(html).toContain('aria-label="Set mode"');
    expect(count(html, '<button')).toBe(4);
    for (const row of WEAVE_MODE_ROWS) {
      expect(html).toContain(row.word);
      expect(html).toContain(row.hint);
    }
  });

  it('marks the current mode and disables only it', () => {
    const html = steps('common');

    expect(count(html, 'aria-pressed="true"')).toBe(1);
    expect(count(html, 'aria-pressed="false"')).toBe(3);
    expect(count(html, 'disabled=""')).toBe(1);
    expect(html).toMatch(/class="hatch-weave-mode-step here"[^>]*aria-pressed="true"[^>]*disabled=""/);
  });
});
