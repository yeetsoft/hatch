import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { WINDOW_PRESETS, type ReportWindow, type WindowPreset } from '../lib/reportWindow';
import { ReportWindowControls } from './ReportWindowControls';

const TO = '2026-10-18T00:00:00.000Z';
const DAY_MS = 86_400_000;

const windowOf = (ms: number): ReportWindow => ({
  from: new Date(Date.parse(TO) - ms).toISOString(),
  to: TO,
});

function render(ms: number, defaultPreset: WindowPreset = '3m') {
  return renderToStaticMarkup(
    <ReportWindowControls
      defaultPreset={defaultPreset}
      window={windowOf(ms)}
      now={new Date(TO)}
      offsetMinutes={0}
      onMove={() => {}}
    />,
  );
}

const shown = (html: string) =>
  [...html.matchAll(/<span class="hatch-facet__option hatch-facet__option--shown">([^<]*)</g)].map((m) => m[1]);

describe('ReportWindowControls', () => {
  it('is a group named Window', () => {
    expect(render(90 * DAY_MS)).toContain('role="group" aria-label="Window"');
  });

  it('puts Earlier, Today and Later first, in that order', () => {
    const labels = [...render(90 * DAY_MS).matchAll(/<button[^>]*>([^<]*)</g)].map((m) => m[1]);

    expect(labels).toEqual(['Earlier', 'Today', 'Later']);
  });

  it('offers the twelve presets for a preset length, showing the one it is', () => {
    const html = render(90 * DAY_MS);

    expect([...html.matchAll(/<option /g)]).toHaveLength(12);
    expect(html).not.toContain('Custom');
    expect(shown(html)).toEqual(['3 months']);
  });

  it('adds a shown Custom option for a length that is no preset', () => {
    const html = render(45 * DAY_MS);

    expect([...html.matchAll(/<option /g)]).toHaveLength(13);
    expect(shown(html)).toEqual(['Custom']);
  });

  it('offers the twelve lengths narrowest first', () => {
    const labels = [...render(90 * DAY_MS).matchAll(/<option[^>]*>([^<]*)</g)].map((m) => m[1]);

    expect(labels).toEqual([
      '1 hour',
      '2 hours',
      '6 hours',
      '12 hours',
      '24 hours',
      '1 week',
      '2 weeks',
      '1 month',
      '3 months',
      '6 months',
      '1 year',
      'All time',
    ]);
  });

  it('lights the length only when it is not the default', () => {
    expect(render(90 * DAY_MS)).not.toContain('hatch-facet--lit');
    expect(render(365 * DAY_MS)).toContain('hatch-facet--lit');
  });

  it('lights the length against the default it is given, not a fixed one', () => {
    expect(render(30 * DAY_MS, '1m')).not.toContain('hatch-facet--lit');
    expect(render(90 * DAY_MS, '1m')).toContain('hatch-facet--lit');
  });

  it('shows and lights a one-hour window against a 3m default', () => {
    const html = render(3_600_000, '3m');

    expect(shown(html)).toEqual(['1 hour']);
    expect(html).toContain('hatch-facet--lit');
  });

  it('disables Earlier and Later on All time, and no other length', () => {
    const steppers = (html: string) =>
      [...html.matchAll(/<button[^>]*>([^<]*)<\/button>/g)].filter((m) => m[1] !== 'Today').map((m) => m[0]);

    for (const { ms } of WINDOW_PRESETS) {
      const disabled = ms === 36_525 * DAY_MS;
      for (const button of steppers(render(ms))) {
        expect(button.includes(' disabled')).toBe(disabled);
      }
    }
  });
});
