import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { ThemeProvider } from '@hatch/ui';
import { ISSUE_TYPE_ROWS } from '../../lib/issueType';
import { SECTIONS } from '../sections';
import { IssueTypesPage } from './IssueTypesPage';

/* A static render, as ThemeSwitch.test.tsx does: no jsdom here, so this holds
   the structure. How the tiles look is for the driver. The provider is there
   because useTokenValues reads the theme. */
const html = renderToStaticMarkup(
  <ThemeProvider>
    <IssueTypesPage />
  </ThemeProvider>,
);

/* The last row runs on into the next section, so each is cut at the section's end. */
const TOKEN_ROWS = ISSUE_TYPE_ROWS.filter((row) => row.name !== 'ambition');
const rows = html
  .split('class="design-type-row"')
  .slice(1)
  .map((row) => row.split('</section>')[0]);
const card = html.slice(html.indexOf('class="design-type-card"'));

describe('IssueTypesPage', () => {
  it('has one row per type with a token, each naming it, and none for the ambition', () => {
    expect(rows).toHaveLength(TOKEN_ROWS.length);
    expect(rows.join('')).not.toContain('--type-ambition');
    TOKEN_ROWS.forEach((row, i) => {
      expect(rows[i]).toContain(`--type-${row.name}`);
      expect(rows[i]).toContain(`>${row.word}<`);
    });
  });

  it('draws each row at the four sizes, the real tile in each', () => {
    for (const row of rows) {
      for (const size of [14, 16, 24, 72]) {
        expect(row).toMatch(new RegExp(`design-type-tile design-type-tile--${size}"><svg`));
      }
      expect(row.match(/design-type-tile design-type-tile--/g)).toHaveLength(4);
    }
  });

  it('shows the ambition tile at four sizes, naming both gradient tokens and what it stands for', () => {
    const row = html.split('design-type-row design-type-row--ambition')[1].split('</section>')[0];

    for (const size of [14, 16, 24, 72]) {
      expect(row).toMatch(new RegExp(`design-type-tile design-type-tile--${size}"><svg`));
    }
    expect(row.match(/design-type-tile design-type-tile--/g)).toHaveLength(4);
    expect(row).toContain('--gradient-ink-from');
    expect(row).toContain('--gradient-ink-to');
    expect(html).toContain('The ambition is an issue type');
    expect(html).not.toContain('not an issue type on the trunk yet');
  });

  it('shows the six tiles side by side at 14 pixels on a card', () => {
    expect(card.match(/design-type-tile design-type-tile--14"><svg/g)).toHaveLength(ISSUE_TYPE_ROWS.length);
    expect(card).not.toMatch(/design-type-tile--(16|24|72)/);
  });
});

/* Each marks section is cut from its heading to the next section's. */
const section = (title: string) => {
  const from = html.indexOf(`>${title}</`);
  return html.slice(from, html.indexOf('</section>', from));
};

describe.each([
  ['Trail', ['none', 'express', 'preapproved'], 'Lines for pre-approval'],
  ['Heat', ['normal', 'economy', 'expedited', 'emergency', 'emergency, pre-approved'], 'Flame for priority'],
  ['Fill', ['outline', 'solid', 'landed'], 'Fill for where a ticket stands'],
])('the %s section', (title, states, rule) => {
  const markup = section(title);
  const rowsOf = markup.split('class="design-mark-row"').slice(1);

  it('has a row for every type and a cell for every state in it', () => {
    expect(rowsOf).toHaveLength(ISSUE_TYPE_ROWS.length);
    ISSUE_TYPE_ROWS.forEach((row, i) => {
      expect(rowsOf[i]).toContain(`>${row.word}<`);
      expect(rowsOf[i].match(/class="design-mark-cell"/g)).toHaveLength(states.length);
      states.forEach((state) => expect(rowsOf[i]).toContain(`class="design-mark-state">${state}<`));
    });
  });

  it('draws every cell at the card\'s 14 pixels and at 72, the real tile in each', () => {
    for (const row of rowsOf) {
      expect(row.match(/design-type-tile design-type-tile--14"><svg/g)).toHaveLength(states.length);
      expect(row.match(/design-type-tile design-type-tile--72"><svg/g)).toHaveLength(states.length);
      expect(row).not.toMatch(/design-type-tile--(16|24)/);
    }
  });

  it('carries its rule in the note', () => {
    expect(markup).toContain(rule);
  });
});

describe('the marks sections', () => {
  it('sit after the ambition and before the card size, in order', () => {
    const at = ['The ambition', 'Trail', 'Heat', 'Fill', 'At the size a card uses'].map((title) => html.indexOf(`>${title}</`));

    expect(at.every((index) => index > 0)).toBe(true);
    expect([...at].sort((a, b) => a - b)).toEqual(at);
  });

  it('leaves red to the bug', () => {
    expect(section('Heat')).toContain('red is left to the bug');
    expect(section('Heat')).toContain('Only an emergency moves');
  });

  it('draws each flame state, and the trail and tick, in the right cells', () => {
    const heat = section('Heat');
    const trail = section('Trail');
    const fill = section('Fill');

    expect(heat).toContain('fill:var(--heat)');
    expect(heat).toContain('rotate(-74)');
    expect(trail).toContain('hatch-type-tile-trail');
    expect(trail).not.toContain('--heat');
    expect(fill).toContain('hatch-type-tile-landed');
    expect(fill).toContain('hatch-type-tile-outlined');
  });
});

describe('SECTIONS', () => {
  it('lists Issue types straight after Project mark', () => {
    const slugs = SECTIONS.map((section) => section.slug);
    expect(slugs.indexOf('issue-types')).toBe(slugs.indexOf('project-mark') + 1);
  });
});
