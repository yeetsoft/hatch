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
const rows = html
  .split('class="design-type-row"')
  .slice(1)
  .map((row) => row.split('</section>')[0]);
const card = html.slice(html.indexOf('class="design-type-card"'));

describe('IssueTypesPage', () => {
  it('has one row per type, each naming its token', () => {
    expect(rows).toHaveLength(ISSUE_TYPE_ROWS.length);
    ISSUE_TYPE_ROWS.forEach((row, i) => {
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

  it('shows the five tiles side by side at 14 pixels on a card', () => {
    expect(card.match(/design-type-tile design-type-tile--14"><svg/g)).toHaveLength(ISSUE_TYPE_ROWS.length);
    expect(card).not.toMatch(/design-type-tile--(16|24|72)/);
  });
});

describe('SECTIONS', () => {
  it('lists Issue types straight after Project mark', () => {
    const slugs = SECTIONS.map((section) => section.slug);
    expect(slugs.indexOf('issue-types')).toBe(slugs.indexOf('project-mark') + 1);
  });
});
