import { describe, expect, it } from 'vitest';
import source from '../../public/hatch-at-home.md?raw';
import { DOC_PAGES, docPage, renderDocs } from './docs';

/** The pages split out of hatch-at-home.md: slug and section, in reading order. */
const SPLIT_PAGES: [slug: string, section: string][] = [
  ['hatch-on-your-phone', 'Start here'],
  ['letting-the-loop-merge-for-you', 'Running agents'],
  ['upgrading', 'Deploying and operating'],
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

describe('hatch-at-home.md, the pointer', () => {
  it('carries no CLAUDE.md block', () => {
    expect(source).not.toContain('claude-contract');
  });

  it('points at the docs and at the CLAUDE.md block page', () => {
    expect(source).toContain('/apps/hatch/docs/');
    expect(source).toContain('the-claudemd-block');
  });

  it('keeps the two sentences to send a friend', () => {
    expect(source).toContain('docker compose up -d');
    expect(source).toContain('http://localhost:8080/apps/hatch/');
  });

  it('is short - the walkthrough itself lives on the docs pages now', () => {
    expect(
      source.split(/\r?\n/).length,
      'hatch-at-home.md should stay a pointer; the walkthrough lives on the docs pages now',
    ).toBeLessThanOrEqual(40);
  });

  it('has no ## heading of its own', () => {
    expect(source.split(/\r?\n/).some((l) => l.startsWith('## '))).toBe(false);
  });
});
