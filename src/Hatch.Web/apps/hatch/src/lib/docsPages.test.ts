import { describe, expect, it } from 'vitest';
import source from '../../public/hatch-at-home.md?raw';
import block from '../../docs/the-claudemd-block.md?raw';
import { DOC_PAGES, docPage, renderDocs } from './docs';

/** The pages split out of hatch-at-home.md: slug and section, in reading order. */
const SPLIT_PAGES: [slug: string, section: string][] = [
  ['hatch-on-your-phone', 'Start here'],
  ['letting-the-loop-merge-for-you', 'Running agents'],
  ['putting-a-front-door-on-it', 'Deploying and operating'],
  ['upgrading', 'Deploying and operating'],
  ['where-the-data-is-and-how-to-back-it-up', 'Deploying and operating'],
  ['how-to-stop', 'Deploying and operating'],
  ['if-something-is-wrong', 'Deploying and operating'],
  ['the-claudemd-block', 'Reference'],
];

describe('the pages split out of hatch-at-home.md', () => {
  it.each(SPLIT_PAGES)('loads %s under %s', (slug, section) => {
    expect(DOC_PAGES.find((p) => p.slug === slug)?.section).toBe(section);
  });

  it('keeps them in reading order relative to each other', () => {
    const slugs = new Set(SPLIT_PAGES.map(([slug]) => slug));
    expect(DOC_PAGES.filter((p) => slugs.has(p.slug)).map((p) => p.slug)).toEqual(SPLIT_PAGES.map(([slug]) => slug));
  });
});

describe('the links retargeted when the document was split', () => {
  const cli = '/apps/hatch/docs/one-increment-and-the-loop';
  const gettingStarted = renderDocs(docPage(DOC_PAGES, 'getting-started')!.body).html;

  it.each([
    `href="${cli}"`,
    `href="${cli}#one-increment-hatch-work"`,
    `href="${cli}#the-loop-hatch-go-to-work"`,
  ])('getting started has %s', (href) => {
    expect(gettingStarted).toContain(href);
  });

  it('lands them on headings the page has', () => {
    const { outline } = renderDocs(docPage(DOC_PAGES, 'one-increment-and-the-loop')!.body);
    for (const [depth, id] of [
      [2, 'one-increment-hatch-work'],
      [2, 'the-loop-hatch-go-to-work'],
      [3, 'one-pass-in-order'],
    ] as const) {
      expect(outline).toContainEqual(expect.objectContaining({ depth, id }));
    }
  });

  it.each([
    'one-increment-hatch-work',
    'the-loop-hatch-go-to-work',
    'one-pass-in-order',
    'bounds-timeouts-and-stopping',
  ])('has %s as an id on exactly one page, and it is the new one', (id) => {
    const pages = DOC_PAGES.filter((p) => renderDocs(p.body).ids.includes(id)).map((p) => p.slug);
    expect(pages).toEqual(['one-increment-and-the-loop']);
  });
});

const START = '<!-- claude-contract:start -->';
const END = '<!-- claude-contract:end -->';

/** The text between the contract marks, LF-only, or null if either mark is missing. */
function contract(text: string): string | null {
  const lf = text.replace(/\r\n/g, '\n');
  const start = lf.indexOf(START);
  if (start === -1) return null;
  const end = lf.indexOf(END, start + START.length);
  if (end === -1) return null;
  return lf.slice(start + START.length, end);
}

describe('the CLAUDE.md block', () => {
  const whole = contract(source);
  const page = contract(block);

  // HA-668 may cut the block out of hatch-at-home.md; until then there are two copies.
  it.runIf(whole !== null && page !== null)('is the same block on its page as in hatch-at-home.md', () => {
    expect(
      page,
      'the block in docs/the-claudemd-block.md has drifted: copy the block from public/hatch-at-home.md, which DocsContractTests holds to the CLI',
    ).toBe(whole);
  });
});
