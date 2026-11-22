import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { HEALTH_WINDOWS, axisSentence } from '../lib/boardHealth';
import type { HealthWindow } from '../lib/boardHealth';
import { sampleHealth } from '../lib/boardHealthSample';
import type { BoardHealth } from '../types';
import { BoardHealthStripView } from './BoardHealthStrip';

function view(answer: BoardHealth, window: HealthWindow = '8h', scopeKeys: string[] = []) {
  return renderToStaticMarkup(
    <MemoryRouter>
      <BoardHealthStripView
        answer={answer}
        window={window}
        onWindow={() => {}}
        index={null}
        onIndex={() => {}}
        scopeKeys={scopeKeys}
      />
    </MemoryRouter>,
  );
}

describe('BoardHealthStripView', () => {
  it('offers the five windows, one pressed', () => {
    const html = view(sampleHealth(), '12h');
    const buttons = html.match(/<button[^>]*hatch-health-window[^>]*>[^<]*<\/button>/g) ?? [];

    expect(buttons).toHaveLength(HEALTH_WINDOWS.length);
    expect(buttons.filter((b) => b.includes('aria-pressed="true"'))).toHaveLength(1);
    expect(buttons.find((b) => b.includes('aria-pressed="true"'))).toContain('>12h<');
  });

  it('says where the window ends and how wide a bar is', () => {
    const answer = sampleHealth();

    expect(view(answer)).toContain(axisSentence(answer).replace(/&/g, '&amp;'));
  });

  it('draws the four tiles after the control', () => {
    const html = view(sampleHealth());

    expect(html.match(/<section /g)).toHaveLength(4);
    expect(html.indexOf('hatch-health-control')).toBeLessThan(html.indexOf('data-tile="moved"'));
  });

  it('does not name the strip', () => {
    expect(view(sampleHealth()).toLowerCase()).not.toContain('pulse');
  });

  it('draws nothing for an answer with no bars', () => {
    expect(view(sampleHealth({ bars: [] }))).toBe('');
  });

  it('carries the scope into the report link', () => {
    expect(view(sampleHealth(), '8h', ['AER', 'FIL'])).toContain('scope=AER,FIL');
  });
});
