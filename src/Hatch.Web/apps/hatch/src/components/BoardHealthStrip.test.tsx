import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { HEALTH_WINDOWS, axisSentence } from '../lib/boardHealth';
import type { HealthWindow } from '../lib/boardHealth';
import { sampleHealth } from '../lib/boardHealthSample';
import type { BoardHealth } from '../types';
import { BoardHealthPhoneView, BoardHealthStripView } from './BoardHealthStrip';

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

  it('draws the five tiles after the control', () => {
    const html = view(sampleHealth());

    expect(html.match(/<section /g)).toHaveLength(5);
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

function phone(answer: BoardHealth, open: boolean, window: HealthWindow = '8h') {
  return renderToStaticMarkup(
    <MemoryRouter>
      <BoardHealthPhoneView
        answer={answer}
        open={open}
        onOpen={() => {}}
        controls="health-open"
        window={window}
        onWindow={() => {}}
        index={null}
        onIndex={() => {}}
        scopeKeys={[]}
      />
    </MemoryRouter>,
  );
}

describe('BoardHealthPhoneView', () => {
  it('closed, is one button of five numbers with no chart and no tiles', () => {
    const html = phone(sampleHealth(), false);

    expect(html.match(/<button/g)).toHaveLength(1);
    expect(html).toContain('aria-expanded="false"');
    expect(html).toContain('aria-controls="health-open"');
    expect(html.match(/data-part="value"/g)).toHaveLength(5);
    expect(html).not.toContain('<svg');
    expect(html).not.toContain('<section');
  });

  it('says the full sentence for each number and shows the short pair', () => {
    const html = phone(sampleHealth(), false);

    expect(html).toContain('aria-label="Oldest in flight: 4h, 2 past normal"');
    expect(html).toContain('>2 old<');
    expect(html).not.toContain('>2 past normal<');
  });

  it('open, puts the window control above five stacked tiles', () => {
    const html = phone(sampleHealth(), true);

    expect(html).toContain('aria-expanded="true"');
    expect(html).toContain('hatch-health-strip--stacked');
    expect(html.match(/<section /g)).toHaveLength(5);
    expect(html.indexOf('hatch-health-control')).toBeLessThan(html.indexOf('<section'));
  });

  it('always holds the region the row controls', () => {
    expect(phone(sampleHealth(), false)).toContain('id="health-open"');
  });

  it('draws nothing for an answer with no bars', () => {
    expect(phone(sampleHealth({ bars: [] }), false)).toBe('');
  });
});
